using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class GoogleService
{
    private const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    private const string AuthUri = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUri = "https://oauth2.googleapis.com/token";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static bool IsClientConfigured() => !string.IsNullOrWhiteSpace(CloudConfig.GoogleOAuthClientId);

    public static bool IsConnected()
    {
        var s = SettingsStore.Load();
        return IsClientConfigured() &&
               (!string.IsNullOrWhiteSpace(s.RefreshToken) ||
                (!string.IsNullOrWhiteSpace(s.AccessToken) && s.AccessTokenExpiresUtc > DateTime.UtcNow));
    }

    public static async Task AuthorizeAsync()
    {
        if (!IsClientConfigured())
            throw new InvalidOperationException("Google OAuth Client ID chưa được cấu hình trong bản build này.");

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://127.0.0.1:{port}/";
            var authUrl = AuthUri +
                          $"?client_id={Esc(CloudConfig.GoogleOAuthClientId)}" +
                          $"&redirect_uri={Esc(redirectUri)}" +
                          "&response_type=code" +
                          $"&scope={Esc(DriveScope)}" +
                          "&access_type=offline&prompt=consent" +
                          $"&state={Esc(state)}" +
                          $"&code_challenge={Esc(challenge)}&code_challenge_method=S256";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            using var tcp = await listener.AcceptTcpClientAsync();
            using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync() ?? throw new InvalidOperationException("Không nhận được phản hồi OAuth.");
            string? line;
            do { line = await reader.ReadLineAsync(); } while (!string.IsNullOrEmpty(line));

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) throw new InvalidOperationException("Phản hồi OAuth không hợp lệ.");
            var query = ParseQuery(new Uri("http://127.0.0.1" + parts[1]).Query);

            var html = "<html><body style='font-family:Segoe UI'><h3>Đã nhận xác thực Google.</h3><p>Có thể đóng tab này và quay lại ứng dụng.</p></body></html>";
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();

            if (query.TryGetValue("error", out var oauthError))
                throw new InvalidOperationException($"Google OAuth từ chối: {oauthError}");
            if (!query.TryGetValue("state", out var returnedState) || !FixedEquals(state, returnedState))
                throw new InvalidOperationException("Phản hồi OAuth không đúng phiên xác thực hiện tại.");
            if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Không nhận được authorization code từ Google.");

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = CloudConfig.GoogleOAuthClientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });
            using var response = await Http.PostAsync(TokenUri, form);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Không lấy được Google token: {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var settings = SettingsStore.Load();
            BindFixedResourceIds(settings);
            settings.OAuthClientJsonPath = string.Empty;
            settings.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            if (root.TryGetProperty("refresh_token", out var refresh))
                settings.RefreshToken = refresh.GetString() ?? settings.RefreshToken;
            var expires = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            settings.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
            SettingsStore.Save(settings);

            await VerifyBindingAsync();
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Compatibility name kept for the existing UI. This method never creates a parent
    /// folder or searches outside the approved Drive root. It only verifies the three
    /// fixed resources and refreshes the Sheet header.
    /// </summary>
    public static async Task ProvisionAsync() => await VerifyBindingAsync(writeHeaders: true);

    public static async Task VerifyBindingAsync(bool writeHeaders = false)
    {
        var settings = SettingsStore.Load();
        BindFixedResourceIds(settings);
        await EnsureAccessTokenAsync(settings);

        await ValidateDriveResourceAsync(
            settings,
            CloudConfig.DriveRootFolderId,
            "application/vnd.google-apps.folder",
            expectedParentId: null,
            "thư mục gốc CẬP NHẬT HƯ HỎNG PICKFACE");

        await ValidateDriveResourceAsync(
            settings,
            CloudConfig.DriveImageFolderId,
            "application/vnd.google-apps.folder",
            CloudConfig.DriveRootFolderId,
            "thư mục Ảnh hàng hư hỏng");

        await ValidateDriveResourceAsync(
            settings,
            CloudConfig.DamageSpreadsheetId,
            "application/vnd.google-apps.spreadsheet",
            CloudConfig.DriveRootFolderId,
            "Google Sheet dữ liệu hư hỏng");

        if (writeHeaders) await WriteHeadersAsync(settings);
        SettingsStore.Save(settings);
    }

    public static async Task SyncAllPendingAsync(IProgress<string>? progress = null)
    {
        var pending = Database.GetPendingReports();
        foreach (var report in pending)
        {
            progress?.Report($"Đồng bộ {report.Sku} - {report.ReportId[..8]}...");
            await SyncReportAsync(report);
        }
    }

    public static async Task SyncReportAsync(DamageReport report)
    {
        try
        {
            var settings = SettingsStore.Load();
            BindFixedResourceIds(settings);
            await EnsureAccessTokenAsync(settings);
            Database.SetReportStatus(report.ReportId, "SYNCING");

            if (await ReportExistsAsync(settings, report.ReportId))
            {
                Database.SetReportStatus(report.ReportId, "SYNCED");
                return;
            }

            foreach (var image in Database.GetImages(report.ReportId))
            {
                if (!string.IsNullOrWhiteSpace(image.DriveFileId)) continue;
                if (!File.Exists(image.LocalPath))
                    throw new FileNotFoundException("Không tìm thấy ảnh local để đồng bộ.", image.LocalPath);

                var remoteName = BuildImageFileName(report, image.Sequence, Path.GetExtension(image.LocalPath));
                var uploaded = await UploadImageAsync(settings, image.LocalPath, remoteName);
                Database.UpdateImageDrive(report.ReportId, image.Sequence, uploaded.Id, uploaded.WebViewLink);
            }

            if (!await ReportExistsAsync(settings, report.ReportId))
            {
                var images = Database.GetImages(report.ReportId);
                await AppendReportAsync(settings, report, images);
            }
            Database.SetReportStatus(report.ReportId, "SYNCED");
        }
        catch (Exception ex)
        {
            Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);
            throw;
        }
    }

    private static void BindFixedResourceIds(GoogleSettings settings)
    {
        settings.RootFolderId = CloudConfig.DriveRootFolderId;
        settings.ImageFolderId = CloudConfig.DriveImageFolderId;
        settings.SpreadsheetId = CloudConfig.DamageSpreadsheetId;
    }

    private static async Task EnsureAccessTokenAsync(GoogleSettings settings)
    {
        if (!IsClientConfigured())
            throw new InvalidOperationException("Google OAuth Client ID chưa được cấu hình trong bản build này.");
        if (!string.IsNullOrWhiteSpace(settings.AccessToken) && settings.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2))
            return;
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
            throw new InvalidOperationException("Chưa kết nối tài khoản Google trên laptop này.");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = CloudConfig.GoogleOAuthClientId,
            ["refresh_token"] = settings.RefreshToken,
            ["grant_type"] = "refresh_token"
        });
        using var response = await Http.PostAsync(TokenUri, form);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Không refresh được Google token: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        settings.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        var expires = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        settings.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
        BindFixedResourceIds(settings);
        SettingsStore.Save(settings);
    }

    private static async Task ValidateDriveResourceAsync(
        GoogleSettings settings,
        string fileId,
        string expectedMimeType,
        string? expectedParentId,
        string label)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Esc(fileId)}?supportsAllDrives=true&fields=id,name,mimeType,parents";
        using var req = CreateRequest(settings, HttpMethod.Get, url);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không truy cập được {label}. Ứng dụng sẽ không tự tìm hoặc tạo tài nguyên ở nơi khác. Chi tiết: {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var mime = root.TryGetProperty("mimeType", out var mimeNode) ? mimeNode.GetString() : null;
        if (!string.Equals(mime, expectedMimeType, StringComparison.Ordinal))
            throw new InvalidOperationException($"Tài nguyên {label} không đúng loại dữ liệu mong đợi.");

        if (!string.IsNullOrWhiteSpace(expectedParentId))
        {
            var inApprovedRoot = root.TryGetProperty("parents", out var parents) &&
                                 parents.EnumerateArray().Any(x => string.Equals(x.GetString(), expectedParentId, StringComparison.Ordinal));
            if (!inApprovedRoot)
                throw new InvalidOperationException($"Tài nguyên {label} không nằm trực tiếp trong Drive root được OWNER cho phép. Dừng đồng bộ để đảm bảo scope.");
        }
    }

    private static async Task WriteHeadersAsync(GoogleSettings settings)
    {
        var headers = new[]
        {
            "ID", "Ngày phát hiện", "Giờ phát hiện", "Ca", "SKU", "Tên sản phẩm", "Vị trí phát hiện",
            "Số lượng hư hỏng", "Base Units", "Ảnh 1", "Ảnh 2", "Ảnh 3", "Ảnh 4", "Ảnh 5",
            "Thời gian nhập", "Thời gian đồng bộ"
        };
        var url = $"https://sheets.googleapis.com/v4/spreadsheets/{Esc(settings.SpreadsheetId)}/values/A1:P1?valueInputOption=RAW";
        using var req = CreateRequest(settings, HttpMethod.Put, url);
        req.Content = JsonContent(new { values = new[] { headers } });
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Lỗi cập nhật tiêu đề Google Sheet: {text}");
    }

    private static async Task<(string Id, string WebViewLink)> UploadImageAsync(GoogleSettings settings, string path, string remoteName)
    {
        using var multipart = new MultipartContent("related");
        var metadata = JsonSerializer.Serialize(new { name = remoteName, parents = new[] { settings.ImageFolderId } });
        multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        var fileContent = new StreamContent(File.OpenRead(path));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(path));
        multipart.Add(fileContent);

        using var req = CreateRequest(settings, HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,webViewLink");
        req.Content = multipart;
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Lỗi upload ảnh Drive: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return (root.GetProperty("id").GetString() ?? string.Empty,
                root.TryGetProperty("webViewLink", out var link) ? link.GetString() ?? string.Empty : string.Empty);
    }

    private static async Task<bool> ReportExistsAsync(GoogleSettings settings, string reportId)
    {
        var url = $"https://sheets.googleapis.com/v4/spreadsheets/{Esc(settings.SpreadsheetId)}/values/A:A?majorDimension=COLUMNS";
        using var req = CreateRequest(settings, HttpMethod.Get, url);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Lỗi kiểm tra Google Sheet: {text}");
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("values", out var values) || values.GetArrayLength() == 0) return false;
        foreach (var value in values[0].EnumerateArray())
            if (string.Equals(value.GetString(), reportId, StringComparison.Ordinal)) return true;
        return false;
    }

    private static async Task AppendReportAsync(GoogleSettings settings, DamageReport report, IReadOnlyList<DamageImage> images)
    {
        var links = new string[5];
        foreach (var image in images.Where(x => x.Sequence is >= 1 and <= 5)) links[image.Sequence - 1] = image.DriveLink ?? string.Empty;
        var values = new object[]
        {
            report.ReportId,
            report.OccurredDate.ToString("dd/MM/yyyy"),
            $"{report.Hour:00}:{report.Minute:00}",
            report.Shift,
            report.Sku,
            report.ProductName,
            report.Location,
            report.Quantity,
            report.BaseUnit,
            links[0], links[1], links[2], links[3], links[4],
            report.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss"),
            DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
        };
        var url = $"https://sheets.googleapis.com/v4/spreadsheets/{Esc(settings.SpreadsheetId)}/values/A:P:append?valueInputOption=USER_ENTERED&insertDataOption=INSERT_ROWS";
        using var req = CreateRequest(settings, HttpMethod.Post, url);
        req.Content = JsonContent(new { values = new[] { values } });
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Lỗi ghi Google Sheet: {text}");
    }

    private static HttpRequestMessage CreateRequest(GoogleSettings settings, HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        return req;
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var value = idx >= 0 ? pair[(idx + 1)..] : string.Empty;
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static string BuildImageFileName(DamageReport report, int sequence, string extension)
    {
        var shift = SafeSegment(report.Shift.Replace(' ', '-'));
        var name = SafeSegment(report.ProductName);
        var location = SafeSegment(report.Location);
        var qty = SafeSegment(report.Quantity.ToString("0"));
        var ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension.ToLowerInvariant();
        return $"{report.OccurredDate:yyyy-MM-dd}_{report.Hour:00}-{report.Minute:00}_{shift}_{SafeSegment(report.Sku)}_{name}_{location}_SL-{qty}_{sequence:00}{ext}";
    }

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= 60 ? cleaned : cleaned[..60].Trim();
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".heic" => "image/heic",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static string Esc(string value) => Uri.EscapeDataString(value);
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedEquals(string a, string b)
    {
        var aa = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return aa.Length == bb.Length && CryptographicOperations.FixedTimeEquals(aa, bb);
    }
}
