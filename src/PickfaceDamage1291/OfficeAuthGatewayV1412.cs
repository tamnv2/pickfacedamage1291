using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

/// <summary>
/// Firebase Authentication control-plane fallback for restricted corporate networks.
/// Runtime session credentials are sent only over HTTPS to the fixed project Apps Script gateway
/// and are never embedded in source code or written to logs.
/// </summary>
internal static class OfficeAuthGatewayV1412
{
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(35), "PickfaceDamage1291-AuthGateway-v1412");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task RefreshSessionAsync(FirebaseSession session, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
            throw new InvalidOperationException("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.");

        var root = await PostAsync(new
        {
            action = "firebase_refresh_session",
            session_refresh = session.RefreshToken
        }, ct);

        var idToken = ReadString(root, "id_token");
        var nextRefresh = ReadString(root, "session_refresh");
        var uid = ReadString(root, "user_id");
        var expires = root.TryGetProperty("expires_in", out var exp) && long.TryParse(exp.ToString(), out var parsed)
            ? parsed
            : 3600;

        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("Google Gateway không trả ID token mới.");
        if (!string.IsNullOrWhiteSpace(session.Uid) && !string.IsNullOrWhiteSpace(uid) &&
            !string.Equals(session.Uid, uid, StringComparison.Ordinal))
            throw new InvalidOperationException("UID sau khi làm mới phiên không khớp tài khoản hiện tại.");

        session.IdToken = idToken;
        if (!string.IsNullOrWhiteSpace(nextRefresh)) session.RefreshToken = nextRefresh;
        if (!string.IsNullOrWhiteSpace(uid)) session.Uid = uid;
        session.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
        SecureSessionStore.Save(session);

        AppLog.Info("AUTH_REFRESH_GOOGLE_GATEWAY_OK", "Đã làm mới phiên Firebase qua Google Gateway.",
            new Dictionary<string, object?>
            {
                ["route"] = "google_gateway",
                ["firebase_rtdb_route"] = NetworkHttpClientFactory.IsRtdbRelayPreferred ? "google_relay" : "direct_or_auto"
            });
    }

    public static async Task<FirebaseUserProfile> CreateUserAsync(
        FirebaseSession adminSession,
        string email,
        string username,
        string displayName,
        Dictionary<string, bool> permissions,
        CancellationToken ct = default)
    {
        if (!adminSession.Profile.IsAdmin)
            throw new InvalidOperationException("Chỉ ADMIN được tạo USER.");

        var root = await PostAsync(new
        {
            action = "firebase_admin_create_user",
            id_token = adminSession.IdToken,
            payload = new
            {
                email = email.Trim(),
                username = username.Trim(),
                display_name = displayName.Trim(),
                permissions
            }
        }, ct);

        if (!root.TryGetProperty("profile", out var profileNode))
            throw new InvalidOperationException("Google Gateway không trả hồ sơ USER vừa tạo.");
        var profile = JsonSerializer.Deserialize<FirebaseUserProfile>(profileNode.GetRawText(), JsonOptions)
                      ?? throw new InvalidOperationException("Không đọc được hồ sơ USER vừa tạo.");
        if (string.IsNullOrWhiteSpace(profile.Uid) || string.IsNullOrWhiteSpace(profile.Username))
            throw new InvalidOperationException("Hồ sơ USER vừa tạo không đầy đủ.");
        return profile;
    }

    private static async Task<JsonElement> PostAsync(object payload, CancellationToken ct)
    {
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            await RuntimeConfigService.InitializeAsync(ct);
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Google Gateway chưa sẵn sàng.");

        var json = JsonSerializer.Serialize(payload);
        using var message = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google Gateway lỗi HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google Gateway từ chối yêu cầu xác thực." : error);
        }
        return root.Clone();
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var node) ? node.GetString() ?? string.Empty : string.Empty;
}
