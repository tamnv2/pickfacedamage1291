using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

/// <summary>
/// Google access for v1.2.2+ is performed by the approved server-side Apps Script gateway.
/// The Windows EXE never contains a Google OAuth client secret, refresh token or service-account key.
/// A valid Firebase session is the client credential; the gateway verifies that token and the app profile
/// before touching the fixed Drive folder / Sheet IDs.
/// </summary>
internal static class GoogleService
{
    private static readonly HttpClient Http = CreateClient();

    public static bool IsClientConfigured() => RuntimeConfigService.IsGoogleGatewayConfigured;

    public static bool IsConnected() =>
        RuntimeConfigService.IsGoogleGatewayConfigured &&
        AppSession.Current is { OfflineMode: false };

    public static bool NeedsReconnect() => false;

    public static Task AuthorizeAsync() => VerifyBindingAsync(writeHeaders: true);

    public static Task ProvisionAsync() => VerifyBindingAsync(writeHeaders: true);

    public static async Task VerifyBindingAsync(bool writeHeaders = false)
    {
        var payload = new
        {
            root_folder_id = CloudConfig.DriveRootFolderId,
            image_folder_id = CloudConfig.DriveImageFolderId,
            spreadsheet_id = CloudConfig.DamageSpreadsheetId,
            write_headers = writeHeaders
        };
        _ = await CallGatewayAsync("verify", payload);
    }

    /// <summary>
    /// Manual "Đồng bộ lại" is intentionally a reconciliation pass over every local report, not only
    /// reports currently marked pending. v1.3.0 could mark a local row SYNCED even though the gateway
    /// had overwritten a previous Google Sheet row. Re-sending all reports is safe because report_id is
    /// the idempotency key on the gateway and restores missing rows/images without creating duplicates.
    /// </summary>
    public static async Task SyncAllPendingAsync(IProgress<string>? progress = null)
    {
        EnsureGatewayReady();
        var reports = Database.GetReports(int.MaxValue)
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var index = 0;
        foreach (var report in reports)
        {
            index++;
            progress?.Report($"Đối soát {index:N0}/{reports.Count:N0}: {report.Sku} - {report.ReportId[..Math.Min(8, report.ReportId.Length)]}...");
            await SyncReportAsync(report);
        }
    }

    public static async Task SyncReportAsync(DamageReport report)
    {
        EnsureGatewayReady();
        try
        {
            Database.SetReportStatus(report.ReportId, "SYNCING");
            var localImages = Database.GetImages(report.ReportId);
            var imagePayload = new List<object>();

            foreach (var image in localImages.OrderBy(x => x.Sequence))
            {
                string? base64 = null;
                string? mimeType = null;
                string? originalName = null;
                if (string.IsNullOrWhiteSpace(image.DriveFileId))
                {
                    if (!File.Exists(image.LocalPath))
                        throw new FileNotFoundException("Không tìm thấy ảnh local để đồng bộ.", image.LocalPath);
                    var bytes = await File.ReadAllBytesAsync(image.LocalPath);
                    base64 = Convert.ToBase64String(bytes);
                    mimeType = GetMimeType(image.LocalPath);
                    originalName = Path.GetFileName(image.LocalPath);
                }

                imagePayload.Add(new
                {
                    sequence = image.Sequence,
                    drive_file_id = image.DriveFileId ?? string.Empty,
                    drive_link = image.DriveLink ?? string.Empty,
                    original_name = originalName ?? string.Empty,
                    mime_type = mimeType ?? string.Empty,
                    data_base64 = base64 ?? string.Empty
                });
            }

            var payload = new
            {
                report = new
                {
                    report_id = report.ReportId,
                    occurred_date = report.OccurredDate.ToString("yyyy-MM-dd"),
                    hour = report.Hour,
                    minute = report.Minute,
                    shift = report.Shift,
                    sku = report.Sku,
                    product_name = report.ProductName,
                    location = report.Location,
                    quantity = decimal.Truncate(report.Quantity),
                    base_unit = report.BaseUnit,
                    created_at = report.CreatedAt.ToString("O"),
                    created_by = report.CreatedBy,
                    version = report.Version,
                    updated_at = report.UpdatedAt?.ToString("O") ?? string.Empty,
                    updated_by = report.UpdatedBy ?? string.Empty
                },
                images = imagePayload
            };

            var root = await CallGatewayAsync("sync_report", payload);
            ValidateGatewayResult(report, localImages, root);

            if (root.TryGetProperty("images", out var imagesNode) && imagesNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in imagesNode.EnumerateArray())
                {
                    var sequence = item.TryGetProperty("sequence", out var seqNode) ? seqNode.GetInt32() : 0;
                    var fileId = item.TryGetProperty("file_id", out var fileNode) ? fileNode.GetString() ?? string.Empty : string.Empty;
                    var link = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                    if (sequence > 0 && !string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(link))
                        Database.UpdateImageDrive(report.ReportId, sequence, fileId, link);
                }
            }

            Database.SetReportStatus(report.ReportId, "SYNCED");
        }
        catch (Exception ex)
        {
            Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);
            throw;
        }
    }

    private static void ValidateGatewayResult(DamageReport report, IReadOnlyList<DamageImage> localImages, JsonElement root)
    {
        var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
        if (!string.Equals(returnedId, report.ReportId, StringComparison.Ordinal))
            throw new InvalidOperationException("Google gateway xác nhận sai report_id. Phiếu chưa được đánh dấu đồng bộ.");

        var row = root.TryGetProperty("row", out var rowNode) && rowNode.TryGetInt32(out var rowValue) ? rowValue : 0;
        if (row < 2)
            throw new InvalidOperationException("Google gateway không xác nhận được dòng dữ liệu hợp lệ trên Google Sheet.");

        var returnedImages = new Dictionary<int, (string FileId, string Url)>();
        if (root.TryGetProperty("images", out var imagesNode) && imagesNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in imagesNode.EnumerateArray())
            {
                var sequence = item.TryGetProperty("sequence", out var seqNode) && seqNode.TryGetInt32(out var seq) ? seq : 0;
                var fileId = item.TryGetProperty("file_id", out var fileNode) ? fileNode.GetString() ?? string.Empty : string.Empty;
                var url = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                if (sequence > 0) returnedImages[sequence] = (fileId, url);
            }
        }

        if (returnedImages.Count != localImages.Count)
            throw new InvalidOperationException($"Google gateway chỉ xác nhận {returnedImages.Count}/{localImages.Count} ảnh. Phiếu giữ trạng thái lỗi để đồng bộ lại.");

        foreach (var image in localImages)
        {
            if (!returnedImages.TryGetValue(image.Sequence, out var saved) ||
                string.IsNullOrWhiteSpace(saved.FileId) || string.IsNullOrWhiteSpace(saved.Url))
                throw new InvalidOperationException($"Google gateway chưa xác nhận ảnh {image.Sequence} trên Drive. Phiếu giữ trạng thái lỗi để đồng bộ lại.");
        }
    }

    private static async Task<JsonElement> CallGatewayAsync(string action, object payload, CancellationToken ct = default)
    {
        EnsureGatewayReady();
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        await FirebaseClient.EnsureFreshAsync(session, ct);

        var requestBody = JsonSerializer.Serialize(new
        {
            action,
            id_token = session.IdToken,
            payload
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google gateway không phản hồi thành công ({(int)response.StatusCode}). {TrimError(text)}");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch { throw new InvalidOperationException("Google gateway trả dữ liệu không hợp lệ."); }
        using (doc)
        {
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okNode) && okNode.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google gateway từ chối yêu cầu." : error);
            }
            return root.Clone();
        }
    }

    private static void EnsureGatewayReady()
    {
        if (AppSession.Current is null)
            throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        if (AppSession.Current.OfflineMode)
            throw new InvalidOperationException("Đang làm việc offline. Dữ liệu sẽ giữ local và đồng bộ khi có mạng.");
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Kết nối Google dùng chung chưa được OWNER cấu hình. Ứng dụng không yêu cầu từng người dùng đăng nhập Google; dữ liệu hiện vẫn được giữ local an toàn.");
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _ => "application/octet-stream"
    };

    private static string TrimError(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 600 ? text : text[..600] + "...";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-GoogleGateway");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
