using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class UsernameAuthService
{
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(40), "PickfaceDamage1291-UsernameAuth");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<FirebaseSession> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        username = username.Trim();
        await EnsureGatewayAsync(ct);

        JsonElement response;
        try
        {
            response = await PostAsync(new
            {
                action = "login_by_username",
                username,
                password
            }, ct, retryTransient: true);
        }
        catch (Exception ex) when (IsTransientTransportError(ex) && !ct.IsCancellationRequested)
        {
            AppLog.Warning(
                "USERNAME_LOGIN_GATEWAY_TRANSIENT",
                "Google Gateway đăng nhập phản hồi chậm hoặc mất kết nối; thử xác thực Firebase trực tiếp bằng email đã lưu an toàn trên máy.",
                new Dictionary<string, object?>
                {
                    ["username"] = username,
                    ["error"] = ex.GetType().Name
                });

            var direct = await TryDirectCachedEmailSignInAsync(username, password, ct);
            if (direct is not null)
            {
                AppLog.Info(
                    "USERNAME_LOGIN_DIRECT_FALLBACK_OK",
                    "Đăng nhập thành công bằng Firebase direct sau khi Google Gateway bị lỗi tạm thời.",
                    new Dictionary<string, object?> { ["username"] = username });
                return direct;
            }

            throw new InvalidOperationException(
                "Google Gateway đăng nhập chưa phản hồi ổn định sau khi đã thử lại. Nếu máy này đã có phiên hợp lệ, có thể dùng “Tiếp tục offline”; nếu cần đăng nhập online, kiểm tra mạng rồi thử lại.",
                ex);
        }

        if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException(ReadError(response, "Tài khoản hoặc mật khẩu không đúng."));

        var uid = response.GetProperty("local_id").GetString() ?? string.Empty;
        var idToken = response.GetProperty("id_token").GetString() ?? string.Empty;
        var refreshToken = response.GetProperty("refresh_token").GetString() ?? string.Empty;
        var expires = response.TryGetProperty("expires_in", out var exp) && long.TryParse(exp.ToString(), out var parsed) ? parsed : 3600;
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Gateway đăng nhập không trả đủ thông tin phiên Firebase.");

        var profile = await FirebaseClient.GetProfileAsync(uid, idToken, ct)
                      ?? throw new InvalidOperationException("Tài khoản chưa được cấp hồ sơ sử dụng ứng dụng. Liên hệ ADMIN.");
        if (!profile.Active) throw new InvalidOperationException("Tài khoản đã bị khóa hoặc ngừng hoạt động.");
        if (!string.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase))
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
        try
        {
            var response = await PostAsync(new
            {
                action = "password_reset_by_username",
                username = username.Trim()
            }, ct, retryTransient: true);
            if (!response.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException(ReadError(response, "Không gửi được yêu cầu đặt lại mật khẩu."));
        }
        catch (Exception ex) when (IsTransientTransportError(ex) && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Google Gateway chưa phản hồi ổn định. Kiểm tra kết nối mạng rồi thử gửi lại yêu cầu quên mật khẩu.", ex);
        }
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
        }, ct, retryTransient: true);
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

    private static async Task<JsonElement> PostAsync(object payload, CancellationToken ct, bool retryTransient = false)
    {
        var json = JsonSerializer.Serialize(payload);
        var action = ReadAction(payload);

        async Task<JsonElement> SendOnceAsync()
        {
            await GoogleGatewayResilienceV1428.TransportGate.WaitAsync(ct);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (retryTransient && !string.IsNullOrWhiteSpace(action))
                    linked.CancelAfter(GoogleGatewayResilienceV1428.AttemptTimeout(action));

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl) { Content = content };
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
                var text = await response.Content.ReadAsStringAsync(linked.Token);
                var mediaType = response.Content.Headers.ContentType?.MediaType;

                if (!response.IsSuccessStatusCode)
                {
                    if (retryTransient && GoogleGatewayResilienceV1428.IsTransientHttpStatus(response.StatusCode))
                        throw new HttpRequestException($"Google Gateway tạm thời lỗi HTTP {(int)response.StatusCode}.");
                    throw new InvalidOperationException($"Gateway lỗi HTTP {(int)response.StatusCode}.");
                }

                if (GoogleGatewayResilienceV1428.LooksLikeHtmlOrInvalidEnvelope(text, mediaType))
                {
                    if (retryTransient)
                        throw new HttpRequestException("Google Gateway trả HTML/nội dung không phải JSON.");
                    throw new InvalidOperationException("Google Gateway trả dữ liệu không hợp lệ.");
                }

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    return doc.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    if (retryTransient) throw new HttpRequestException("Google Gateway trả JSON không hoàn chỉnh.", ex);
                    throw new InvalidOperationException("Google Gateway trả dữ liệu không hợp lệ.", ex);
                }
            }
            finally
            {
                GoogleGatewayResilienceV1428.TransportGate.Release();
            }
        }

        return retryTransient
            ? await NetworkHttpClientFactory.RetryAsync(SendOnceAsync, attempts: 2, ct)
            : await SendOnceAsync();
    }

    private static string ReadAction(object payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            return doc.RootElement.TryGetProperty("action", out var actionNode) ? actionNode.GetString() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<FirebaseSession?> TryDirectCachedEmailSignInAsync(string username, string password, CancellationToken ct)
    {
        FirebaseSession? cached;
        try
        {
            cached = SecureSessionStore.Load();
        }
        catch
        {
            return null;
        }

        if (cached is null ||
            !string.Equals(cached.Profile.Username, username, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cached.Profile.Email))
            return null;

        try
        {
            var session = await FirebaseClient.SignInAsync(cached.Profile.Email, password, ct);
            return string.Equals(session.Profile.Username, username, StringComparison.OrdinalIgnoreCase)
                ? session
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warning(
                "USERNAME_LOGIN_DIRECT_FALLBACK_FAILED",
                "Firebase direct fallback không đăng nhập được sau khi Google Gateway bị lỗi tạm thời.",
                new Dictionary<string, object?>
                {
                    ["username"] = username,
                    ["error"] = ex.GetType().Name
                });
            return null;
        }
    }

    private static bool IsTransientTransportError(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException) return true;
            if (current is TaskCanceledException) return true;
            if (current is TimeoutException) return true;
        }
        return false;
    }

    private static string ReadError(JsonElement response, string fallback)
    {
        return response.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString())
            ? error.GetString()!
            : fallback;
    }
}
