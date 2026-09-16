using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class FirebaseClient
{
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(30), "PickfaceDamage1291-Firebase");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    private static string ApiKey => CloudConfig.FirebaseApiKey;
    private static string DatabaseUrl => CloudConfig.FirebaseDatabaseUrl.TrimEnd('/');

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(DatabaseUrl);

    public static async Task<FirebaseSession> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        EnsureConfigured();
        var payload = new { email = email.Trim(), password, returnSecureToken = true };
        using var response = await Http.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={Uri.EscapeDataString(ApiKey)}",
            JsonContent(payload), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyAuthError(body, "Không đăng nhập được."));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var uid = root.GetProperty("localId").GetString() ?? string.Empty;
        var idToken = root.GetProperty("idToken").GetString() ?? string.Empty;
        var refreshToken = root.GetProperty("refreshToken").GetString() ?? string.Empty;
        var expires = ParseLong(root, "expiresIn", 3600);
        var profile = await GetProfileAsync(uid, idToken, ct)
                      ?? throw new InvalidOperationException("Tài khoản chưa được cấp hồ sơ sử dụng ứng dụng. Liên hệ ADMIN.");
        if (!profile.Active)
            throw new InvalidOperationException("Tài khoản đã bị khóa hoặc ngừng hoạt động.");

        return new FirebaseSession
        {
            Uid = uid,
            Email = root.TryGetProperty("email", out var emailNode) ? emailNode.GetString() ?? email.Trim() : email.Trim(),
            IdToken = idToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30)),
            Profile = profile,
            OfflineMode = false
        };
    }

    public static async Task EnsureFreshAsync(FirebaseSession session, CancellationToken ct = default)
    {
        if (session.OfflineMode) return;
        if (!string.IsNullOrWhiteSpace(session.IdToken) && session.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return;
        if (string.IsNullOrWhiteSpace(session.RefreshToken))
            throw new InvalidOperationException("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.");

        await RefreshGate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(session.IdToken) && session.AccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return;
            await OfficeAuthGatewayV1412.RefreshSessionAsync(session, ct);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public static async Task SendPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var username = AppSession.Current?.Profile.Username;
        if (!string.IsNullOrWhiteSpace(username))
        {
            await UsernameAuthService.SendPasswordResetAsync(username, ct);
            return;
        }

        EnsureConfigured();
        var payload = new { requestType = "PASSWORD_RESET", email = email.Trim() };
        using var response = await Http.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={Uri.EscapeDataString(ApiKey)}",
            JsonContent(payload), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyAuthError(body, "Không gửi được email lấy lại mật khẩu."));
    }

    public static async Task<FirebaseUserProfile?> GetProfileAsync(string uid, string idToken, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(DbUrl($"users/{Uri.EscapeDataString(uid)}", idToken), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        if (body == "null") return null;
        return JsonSerializer.Deserialize<FirebaseUserProfile>(body, JsonOptions);
    }

    public static async Task<FirebaseUserProfile> UpdateOwnDisplayNameAsync(
        FirebaseSession session,
        string displayName,
        CancellationToken ct = default)
    {
        if (session.OfflineMode)
            throw new InvalidOperationException("Cần kết nối mạng để cập nhật họ tên.");

        var value = (displayName ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Họ tên không được để trống.");
        if (value.Length > 120)
            throw new InvalidOperationException("Họ tên tối đa 120 ký tự.");

        await EnsureFreshAsync(session, ct);
        using var response = await Http.PutAsync(
            DbUrl($"users/{Uri.EscapeDataString(session.Uid)}/display_name", session.IdToken),
            JsonContent(value),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));

        var refreshed = await GetProfileAsync(session.Uid, session.IdToken, ct)
                        ?? throw new InvalidOperationException("Không đọc lại được hồ sơ sau khi cập nhật họ tên.");
        session.Profile = refreshed;
        SecureSessionStore.Save(session);
        await AppendAuditAsync(session, "DISPLAY_NAME_UPDATED", new { display_name = refreshed.DisplayName }, ct: ct);
        return refreshed;
    }

    public static async Task<long> GetServerNowMsAsync(FirebaseSession session, CancellationToken ct = default)
    {
        await EnsureFreshAsync(session, ct);
        try
        {
            using var response = await Http.GetAsync(DbUrl(".info/serverTimeOffset", session.IdToken), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode && long.TryParse(body, out var offset))
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + offset;
        }
        catch
        {
            // Local UTC is only a fallback. Security Rules remain the final authority.
        }
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public static async Task<ActiveOperatorSnapshot> GetActiveOperatorSnapshotAsync(FirebaseSession session, CancellationToken ct = default)
    {
        await EnsureFreshAsync(session, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, DbUrl("active_operator", session.IdToken));
        request.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");
        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));

        var etag = response.Headers.ETag?.Tag
                   ?? (response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault() : null)
                   ?? "*";
        var value = body == "null" ? null : JsonSerializer.Deserialize<ActiveOperatorRecord>(body, JsonOptions);
        return new ActiveOperatorSnapshot(value, etag);
    }

    public static async Task<bool> PutActiveOperatorAsync(
        FirebaseSession session,
        ActiveOperatorRecord value,
        string etag,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(session, ct);
        using var request = new HttpRequestMessage(HttpMethod.Put, DbUrl("active_operator", session.IdToken));
        request.Headers.TryAddWithoutValidation("if-match", etag);
        request.Content = JsonContent(value);
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return false;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        return true;
    }

    public static async Task<bool> DeleteActiveOperatorAsync(FirebaseSession session, string etag, CancellationToken ct = default)
    {
        await EnsureFreshAsync(session, ct);
        using var request = new HttpRequestMessage(HttpMethod.Delete, DbUrl("active_operator", session.IdToken));
        request.Headers.TryAddWithoutValidation("if-match", etag);
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return false;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        return true;
    }

    public static async Task AppendAuditAsync(
        FirebaseSession session,
        string action,
        object? details = null,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var entry = new FirebaseAuditEntry
        {
            EventId = eventId,
            Uid = session.Uid,
            Username = session.Profile.Username,
            DeviceId = SecureSessionStore.GetOrCreateDeviceId(),
            SessionId = sessionId ?? session.CachedOperator?.SessionId ?? string.Empty,
            Action = action,
            ClientTime = DateTimeOffset.Now.ToString("O"),
            ServerTime = new Dictionary<string, string> { [".sv"] = "timestamp" },
            Details = details
        };

        AuditOutboxStore.Enqueue(entry);
        if (session.OfflineMode) return;

        try
        {
            await EnsureFreshAsync(session, ct);
            await UploadAuditEntryAsync(session, entry, ct);
            AuditOutboxStore.Delete(entry.EventId);
        }
        catch
        {
            // Keep the event in the local outbox. Audit transport must not break the business action.
        }
    }

    public static async Task FlushAuditOutboxAsync(FirebaseSession session, CancellationToken ct = default)
    {
        if (session.OfflineMode) return;
        await EnsureFreshAsync(session, ct);
        foreach (var entry in AuditOutboxStore.List(500))
        {
            if (!string.Equals(entry.Uid, session.Uid, StringComparison.Ordinal)) continue;
            try
            {
                entry.ServerTime = new Dictionary<string, string> { [".sv"] = "timestamp" };
                await UploadAuditEntryAsync(session, entry, ct);
                AuditOutboxStore.Delete(entry.EventId);
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task UploadAuditEntryAsync(FirebaseSession session, FirebaseAuditEntry entry, CancellationToken ct)
    {
        await GoogleGatewayV140.AppendAuditAsync(entry, ct);
    }

    public static async Task<List<FirebaseUserProfile>> ListUsersAsync(FirebaseSession adminSession, CancellationToken ct = default)
    {
        await EnsureFreshAsync(adminSession, ct);
        using var response = await Http.GetAsync(DbUrl("users", adminSession.IdToken), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        if (body == "null") return [];
        var map = JsonSerializer.Deserialize<Dictionary<string, FirebaseUserProfile>>(body, JsonOptions) ?? [];
        return map.Values.OrderBy(x => x.Username, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<FirebaseUserProfile> CreateUserAsync(
        FirebaseSession adminSession,
        string email,
        string username,
        string displayName,
        Dictionary<string, bool> permissions,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(adminSession, ct);
        var profile = await OfficeAuthGatewayV1412.CreateUserAsync(
            adminSession, email, username, displayName, permissions, ct);
        await AppendAuditAsync(adminSession, "USER_CREATED", new
        {
            target_uid = profile.Uid,
            username = profile.Username,
            email = profile.Email,
            transport = "google_gateway"
        }, ct: ct);
        return profile;
    }

    public static async Task UpdateUserProfileAsync(
        FirebaseSession adminSession,
        FirebaseUserProfile profile,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(adminSession, ct);
        using var response = await Http.PutAsync(
            DbUrl($"users/{Uri.EscapeDataString(profile.Uid)}", adminSession.IdToken), JsonContent(profile), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        await AppendAuditAsync(adminSession, "USER_PROFILE_UPDATED", new { target_uid = profile.Uid, username = profile.Username, active = profile.Active, permissions = profile.Permissions }, ct: ct);
    }

    public static async Task<List<FirebaseAuditEntry>> ListAuditAsync(FirebaseSession adminSession, int limit = 500, CancellationToken ct = default)
    {
        await EnsureFreshAsync(adminSession, ct);
        var url = DbUrl("audit", adminSession.IdToken) + $"&orderBy=%22server_time%22&limitToLast={Math.Clamp(limit, 1, 1000)}";
        using var response = await Http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
        if (body == "null") return [];
        var map = JsonSerializer.Deserialize<Dictionary<string, FirebaseAuditEntry>>(body, JsonOptions) ?? [];
        return map.Values
            .OrderByDescending(x => ParseServerTimestamp(x.ServerTime))
            .ToList();
    }

    public static async Task<HttpResponseMessage> OpenActiveOperatorStreamAsync(FirebaseSession session, CancellationToken ct)
    {
        await EnsureFreshAsync(session, ct);
        var request = new HttpRequestMessage(HttpMethod.Get, DbUrl("active_operator", session.IdToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static long ParseServerTimestamp(object? value)
    {
        if (value is JsonElement e && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var number)) return number;
        if (value is long l) return l;
        if (value is int i) return i;
        if (long.TryParse(Convert.ToString(value), out var parsed)) return parsed;
        return 0;
    }

    private static string GenerateTemporaryPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace("+", "A").Replace("/", "b").Replace("=", "9") + "aA1!";
    }

    private static long ParseLong(JsonElement root, string property, long fallback)
    {
        if (!root.TryGetProperty(property, out var node)) return fallback;
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var n)) return n;
        return long.TryParse(node.GetString(), out var parsed) ? parsed : fallback;
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string DbUrl(string path, string idToken)
    {
        var cleanPath = path.Trim('/');
        return $"{DatabaseUrl}/{cleanPath}.json?auth={Uri.EscapeDataString(idToken)}";
    }

    private static void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Firebase chưa được cấu hình cho project pickface-damage-1291.");
    }

    private static string ToFriendlyDatabaseError(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            return "Firebase từ chối quyền truy cập. Kiểm tra tài khoản, trạng thái active và Security Rules.";
        return $"Firebase Realtime Database lỗi {(int)status}: {body}";
    }

    private static string ToFriendlyAuthError(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;
            if (message.StartsWith("INVALID_LOGIN_CREDENTIALS", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("INVALID_PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("EMAIL_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                return "Tài khoản hoặc mật khẩu không đúng.";
            if (message.StartsWith("USER_DISABLED", StringComparison.OrdinalIgnoreCase))
                return "Tài khoản Authentication đã bị vô hiệu hóa.";
            if (message.StartsWith("EMAIL_EXISTS", StringComparison.OrdinalIgnoreCase))
                return "Email đã tồn tại trong Firebase Authentication.";
            if (message.StartsWith("WEAK_PASSWORD", StringComparison.OrdinalIgnoreCase))
                return "Mật khẩu không đáp ứng yêu cầu của Firebase.";
            if (message.StartsWith("TOO_MANY_ATTEMPTS_TRY_LATER", StringComparison.OrdinalIgnoreCase))
                return "Có quá nhiều lần thử. Hãy chờ một lúc rồi thử lại.";
            if (!string.IsNullOrWhiteSpace(message)) return $"Firebase Authentication: {message}";
        }
        catch
        {
            // Return fallback below.
        }
        return fallback;
    }
}
