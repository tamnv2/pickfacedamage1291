using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class NetworkHttpClientFactory
{
    private static long _lastNetworkChangeUtcTicks;
    private static long _rtdbRelayUntilUtcTicks;
    private static readonly TimeSpan RtdbRelayCooldown = TimeSpan.FromMinutes(2);

    public static event Action? NetworkChanged;

    internal static bool IsRtdbRelayPreferred
    {
        get
        {
            var ticks = Interlocked.Read(ref _rtdbRelayUntilUtcTicks);
            return ticks > DateTime.UtcNow.Ticks;
        }
    }

    static NetworkHttpClientFactory()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => MarkNetworkChanged("address");
        NetworkChange.NetworkAvailabilityChanged += (_, e) => MarkNetworkChanged(e.IsAvailable ? "available" : "unavailable");
    }

    public static HttpClient Create(TimeSpan timeout, string userAgent)
    {
        var sockets = CreateSocketsHandler();
        var handler = new FirebaseRtdbFallbackHandler(sockets);
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    private static SocketsHttpHandler CreateSocketsHandler() => new()
    {
        // Let .NET use the Windows/system proxy directly. Do not wrap IWebProxy:
        // some Windows/PAC configurations can return the destination URI itself for a
        // direct connection; wrapping that value can create an invalid CONNECT tunnel.
        UseProxy = true,
        Proxy = null,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,

        // Keep connections short-lived so DNS/TCP/proxy routes refresh quickly when a laptop
        // moves between Internet/Wi-Fi and an internal Office LAN.
        PooledConnectionLifetime = TimeSpan.FromSeconds(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(5),
        ConnectTimeout = TimeSpan.FromSeconds(12)
    };

    public static bool NetworkChangedRecently(TimeSpan window)
    {
        var ticks = Interlocked.Read(ref _lastNetworkChangeUtcTicks);
        if (ticks <= 0) return false;
        var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    public static async Task<T> RetryAsync<T>(Func<Task<T>> action, int attempts = 3, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < attempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < attempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }
        throw last ?? new HttpRequestException("Kết nối mạng không thành công.");
    }

    public static string Friendly(Exception ex, string target)
    {
        var socket = FindInner<SocketException>(ex);
        if (socket is not null && socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
            return $"Không phân giải được địa chỉ {target} từ ứng dụng. Trình duyệt có thể dùng DNS/proxy khác với ứng dụng Windows. Ứng dụng đã thử proxy hệ thống nhưng kết nối vẫn chưa thành công.";

        if (ex is TaskCanceledException)
            return $"Kết nối tới {target} quá thời gian chờ.";

        if (ex is HttpRequestException)
            return $"Không kết nối được tới {target}: {ex.Message}";

        return ex.Message;
    }

    private static void MarkNetworkChanged(string state)
    {
        Interlocked.Exchange(ref _lastNetworkChangeUtcTicks, DateTime.UtcNow.Ticks);

        // A network change may mean the laptop has left the restricted Office network.
        // Probe direct Firebase again instead of pinning the relay decision from the old network.
        Interlocked.Exchange(ref _rtdbRelayUntilUtcTicks, 0);
        try
        {
            AppLog.Info("NETWORK_CHANGED", "Windows báo thay đổi kết nối mạng.", new Dictionary<string, object?>
            {
                ["state"] = state
            });
        }
        catch
        {
            // Network notifications can arrive before logging is initialized.
        }

        try { NetworkChanged?.Invoke(); } catch { }
    }

    private static void PreferRtdbRelay(string reason)
    {
        Interlocked.Exchange(ref _rtdbRelayUntilUtcTicks, DateTime.UtcNow.Add(RtdbRelayCooldown).Ticks);
        try
        {
            AppLog.Warning("RTDB_RELAY_ACTIVE", "Firebase direct không ổn định; tạm chuyển RTDB qua Google gateway.", new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["minutes"] = (int)RtdbRelayCooldown.TotalMinutes
            });
        }
        catch { }
    }

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is T typed) return typed;
        return null;
    }

    /// <summary>
    /// Office networks can allow Google Apps Script/Sheets while denying direct access to the
    /// Firebase Realtime Database host. Business traffic must not be classified as globally
    /// offline in that situation. This handler first uses RTDB directly, then transparently
    /// relays the same authenticated RTDB request through the approved Apps Script gateway.
    /// Firebase ID token + Security Rules remain the authority; the relay is not an admin bypass.
    /// </summary>
    private sealed class FirebaseRtdbFallbackHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private static readonly HttpClient RelayHttp = CreateRelayClient();
        private static readonly HashSet<HttpStatusCode> FallbackStatuses =
        [
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout
        ];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!IsFirebaseRtdbRequest(request.RequestUri))
                return await base.SendAsync(request, cancellationToken);

            var relayRequest = await CaptureRelayRequestAsync(request, cancellationToken);
            if (relayRequest is null)
                return await base.SendAsync(request, cancellationToken);

            var isEventStream = request.Headers.Accept.Any(x =>
                string.Equals(x.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase));

            if (IsRtdbRelayPreferred)
            {
                if (isEventStream)
                {
                    // Apps Script web apps are request/response, not an SSE tunnel. Slow the
                    // realtime reconnect loop while normal snapshot/heartbeat calls use relay.
                    await Task.Delay(TimeSpan.FromSeconds(27), cancellationToken);
                    return ServiceUnavailableForPolling(request);
                }

                var preferred = await TryRelayAsync(relayRequest, cancellationToken);
                if (preferred is not null) return preferred;

                // Gateway may not have the relay capability yet. Fall back to a direct probe.
                Interlocked.Exchange(ref _rtdbRelayUntilUtcTicks, 0);
            }

            HttpResponseMessage? directResponse = null;
            Exception? directException = null;
            try
            {
                directResponse = await base.SendAsync(request, cancellationToken);
                if (!FallbackStatuses.Contains(directResponse.StatusCode))
                    return directResponse;
            }
            catch (HttpRequestException ex)
            {
                directException = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                directException = ex;
            }

            var relay = await TryRelayAsync(relayRequest, cancellationToken);
            if (relay is not null)
            {
                var directStatus = directResponse?.StatusCode;
                var relayProvesAlternatePath = directException is not null ||
                                               directStatus != relay.StatusCode ||
                                               relay.IsSuccessStatusCode ||
                                               relay.StatusCode == HttpStatusCode.PreconditionFailed;
                if (relayProvesAlternatePath)
                    PreferRtdbRelay(directException is null ? $"direct_http_{(int?)directStatus}" : "direct_network_error");

                directResponse?.Dispose();
                if (isEventStream)
                {
                    relay.Dispose();
                    return ServiceUnavailableForPolling(request);
                }
                return relay;
            }

            if (directResponse is not null) return directResponse;
            if (directException is not null) throw directException;
            throw new HttpRequestException("Không kết nối được Firebase Realtime Database.");
        }

        private static bool IsFirebaseRtdbRequest(Uri? uri)
        {
            if (uri is null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            if (!Uri.TryCreate(CloudConfig.FirebaseDatabaseUrl, UriKind.Absolute, out var configured)) return false;
            return string.Equals(uri.Host, configured.Host, StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<RelayRequest?> CaptureRelayRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri is null) return null;
            var token = ReadQueryValue(uri.Query, "auth");
            if (string.IsNullOrWhiteSpace(token)) return null;

            var path = uri.AbsolutePath.Trim('/');
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            path = path[..^5];

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            if (body.Length > 256 * 1024) return null;

            var wantEtag = request.Headers.TryGetValues("X-Firebase-ETag", out var wantValues) &&
                           wantValues.Any(x => string.Equals(x, "true", StringComparison.OrdinalIgnoreCase));
            var ifMatch = request.Headers.TryGetValues("if-match", out var matchValues)
                ? matchValues.FirstOrDefault() ?? string.Empty
                : string.Empty;

            return new RelayRequest(request.Method.Method.ToUpperInvariant(), path, token, body, wantEtag, ifMatch);
        }

        private static async Task<HttpResponseMessage?> TryRelayAsync(RelayRequest request, CancellationToken ct)
        {
            if (!RuntimeConfigService.IsGoogleGatewayConfigured) return null;
            if (request.Method is not ("GET" or "PUT" or "DELETE")) return null;

            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    action = "rtdb_proxy",
                    id_token = request.IdToken,
                    payload = new
                    {
                        method = request.Method,
                        path = request.Path,
                        body = request.Body,
                        want_etag = request.WantEtag,
                        if_match = request.IfMatch
                    }
                });
                using var message = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using var response = await RelayHttp.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct);
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
                if (!root.TryGetProperty("http_status", out var statusNode) || !statusNode.TryGetInt32(out var statusCode) || statusCode is < 100 or > 599)
                    return null;

                var body = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() ?? string.Empty : string.Empty;
                var etag = root.TryGetProperty("etag", out var etagNode) ? etagNode.GetString() ?? string.Empty : string.Empty;
                var proxied = new HttpResponseMessage((HttpStatusCode)statusCode)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrWhiteSpace(etag))
                    proxied.Headers.TryAddWithoutValidation("ETag", etag);
                return proxied;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    AppLog.Warning("RTDB_RELAY_FAILED", "Không dùng được Firebase relay qua Google gateway.", new Dictionary<string, object?>
                    {
                        ["path"] = request.Path,
                        ["method"] = request.Method,
                        ["error"] = ex.GetType().Name
                    });
                }
                catch { }
                return null;
            }
        }

        private static HttpResponseMessage ServiceUnavailableForPolling(HttpRequestMessage request)
            => new(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = new StringContent("{\"error\":\"RTDB realtime stream is using snapshot polling through Google gateway.\"}", Encoding.UTF8, "application/json")
            };

        private static HttpClient CreateRelayClient()
        {
            var client = new HttpClient(CreateSocketsHandler(), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-RTDBRelay");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static string ReadQueryValue(string query, string name)
        {
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                var key = index >= 0 ? part[..index] : part;
                if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase)) continue;
                var value = index >= 0 ? part[(index + 1)..] : string.Empty;
                return Uri.UnescapeDataString(value.Replace("+", "%20", StringComparison.Ordinal));
            }
            return string.Empty;
        }

        private sealed record RelayRequest(
            string Method,
            string Path,
            string IdToken,
            string Body,
            bool WantEtag,
            string IfMatch);
    }
}
