using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class UsernameAuthService
{
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(20), "PickfaceDamage1291-UsernameAuth");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<FirebaseSession> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        await EnsureGatewayAsync(ct);
        var response = await PostAsync(new
        {
            action = "login_by_username",
            username = username.Trim(),
            password
        }, ct);

        if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(ReadError(response, "Tài khoản hoặc mật khẩu không đúng."));

        var uid = response.GetProperty("local_id").GetString() ?? string.Empty;
        var idToken = response.GetProperty("id_token").GetString() ?? string.Empty;
        var refreshToken = response.GetProperty("refresh_token").GetString() ?? string.Empty;
        var expires = response.TryGetProperty("expires_in", out var exp) && long.TryParse(exp.ToString(), out var parsed) ? parsed : 3600;
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Gateway đăng nhập không trả đủ thông tin phiên Firebase.");

        FirebaseUserProfile? profile;
        try
        {
            profile = await FirebaseClient.GetProfileAsync(uid, idToken, ct);
        }
        catch (Exception directEx) when (!ct.IsCancellationRequested)
        {
            AppLog.Warning("DIRECT_FIREBASE_PROFILE_READ_FAILED", NetworkHttpClientFactory.Friendly(directEx, "Firebase user profile"));
            profile = await OfficeNetworkFirebaseBridge.GetProfileAsync(uid, idToken, ct);
            AppLog.Info("OFFICE_LOGIN_PROFILE_FALLBACK_OK", "Đăng nhập đã đọc hồ sơ qua Google Gateway do mạng hiện tại không truy cập được Firebase RTDB trực tiếp.");
        }

        profile ??= throw new InvalidOperationException("Tài khoản chưa được cấp hồ sơ sử dụng ứng dụng. Liên hệ ADMIN.");
        if (!profile.Active) throw new InvalidOperationException("Tài khoản đã bị khóa hoặc ngừng hoạt động.");
        if (!string.Equals(profile.Username, username.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tên tài khoản không khớp hồ sơ Firebase.");

        return new FirebaseSession
        {
            Uid = uid,
            Email = profile.Email,
            IdToken = idToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30)),
            Profile = profile,
            OfflineMode = false
        };
    }

    public static async Task SendPasswordResetAsync(string username, CancellationToken ct = default)
    {
        await EnsureGatewayAsync(ct);
        var response = await PostAsync(new
        {
            action = "password_reset_by_username",
            username = username.Trim()
        }, ct);
        if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(ReadError(response, "Không gửi được yêu cầu đặt lại mật khẩu."));
    }

    public static async Task SyncAliasesAsync(FirebaseSession adminSession, CancellationToken ct = default)
    {
        if (!adminSession.Profile.IsAdmin || adminSession.OfflineMode) return;
        await FirebaseClient.EnsureFreshAsync(adminSession, ct);
        await EnsureGatewayAsync(ct);
        var response = await PostAsync(new
        {
            action = "sync_login_aliases",
            id_token = adminSession.IdToken,
            payload = new { }
        }, ct);
        if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(ReadError(response, "Không đồng bộ được danh sách tài khoản đăng nhập."));
    }

    private static async Task EnsureGatewayAsync(CancellationToken ct)
    {
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            await RuntimeConfigService.InitializeAsync(ct);
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Gateway đăng nhập dùng chung chưa sẵn sàng.");
    }

    private static async Task<JsonElement> PostAsync(object payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(RuntimeConfigService.GoogleGatewayUrl, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gateway lỗi HTTP {(int)response.StatusCode}.");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string ReadError(JsonElement response, string fallback)
    {
        return response.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString())
            ? error.GetString()!
            : fallback;
    }
}
