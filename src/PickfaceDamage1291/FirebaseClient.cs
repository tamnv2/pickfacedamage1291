using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class FirebaseClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
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

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = session.RefreshToken
            });
            using var response = await Http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(ApiKey)}", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ToFriendlyAuthError(body, "Không làm mới được phiên đăng nhập."));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            session.IdToken = root.GetProperty("id_token").GetString() ?? string.Empty;
            session.RefreshToken = root.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString() ?? session.RefreshToken
                : session.RefreshToken;
            session.Uid = root.TryGetProperty("user_id", out var uid) ? uid.GetString() ?? session.Uid : session.Uid;
            var expires = ParseLong(root, "expires_in", 3600);
            session.AccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 30));
            SecureSessionStore.Save(session);
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public static async Task SendPasswordResetAsync(string email, CancellationToken ct = default)
    {
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
        using var response = await Http.PutAsync(DbUrl($"audit/{entry.EventId}", session.IdToken), JsonContent(entry), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyDatabaseError(response.StatusCode, body));
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
        var existing = await ListUsersAsync(adminSession, ct);
        if (existing.Any(x => string.Equals(x.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tên tài khoản đã tồn tại.");
        if (existing.Any(x => string.Equals(x.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Email đã được dùng cho một tài khoản trong ứng dụng.");

        var temporaryPassword = GenerateTemporaryPassword();
        var signupPayload = new { email = email.Trim(), password = temporaryPassword, returnSecureToken = true };
        using var signupResponse = await Http.PostAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={Uri.EscapeDataString(ApiKey)}",
            JsonContent(signupPayload), ct);
        var signupBody = await signupResponse.Content.ReadAsStringAsync(ct);
        if (!signupResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(ToFriendlyAuthError(signupBody, "Không tạo được tài khoản Authentication."));

        using var signupDoc = JsonDocument.Parse(signupBody);
        var signupRoot = signupDoc.RootElement;
        var uid = signupRoot.GetProperty("localId").GetString() ?? throw new InvalidOperationException("Firebase không trả UID.");
        var newUserToken = signupRoot.GetProperty("idToken").GetString() ?? string.Empty;
        var profile = new FirebaseUserProfile
        {
            Uid = uid,
            Username = username.Trim(),
            DisplayName = displayName.Trim(),
            Email = email.Trim(),
            Role = "user",
            Active = true,
            Permissions = permissions
        };

        try
        {
            using var profileResponse = await Http.PutAsync(
                DbUrl($"users/{Uri.EscapeDataString(uid)}", adminSession.IdToken), JsonContent(profile), ct);
            var profileBody = await profileResponse.Content.ReadAsStringAsync(ct);
            if (!profileResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(ToFriendlyDatabaseError(profileResponse.StatusCode, profileBody));

            await SendPasswordResetAsync(email, ct);
            await AppendAuditAsync(adminSession, "USER_CREATED", new { target_uid = uid, username = profile.Username, email = profile.Email }, ct: ct);
            return profile;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(newUserToken))
            {
                try
                {
                    using var deleteResponse = await Http.PostAsync(
                        $"https://identitytoolkit.googleapis.com/v1/accounts:delete?key={Uri.EscapeDataString(ApiKey)}",
                        JsonContent(new { idToken = newUserToken }), ct);
                }
                catch
                {
                    // Best-effort rollback. The orphan Auth account has no RTDB profile and therefore no app access.
                }
            }
            throw;
        }
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
