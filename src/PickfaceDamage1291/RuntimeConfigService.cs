using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class RuntimeConfigService
{
    private const string ConfigUrl = "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json";
    private const string BuiltInGatewayUrl = "https://script.google.com/macros/s/AKfycbwfB-NdKPRj-GN4T_MRS89aaJ8ihRsPfT9qd2wUhBntOAOtBF1ycuaNJRdYN5JCFf12/exec";
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(10), "PickfaceDamage1291-RuntimeConfig");
    private static readonly SemaphoreSlim InitializeGate = new(1, 1);
    private static int _backgroundRefreshStarted;

    public static string GoogleGatewayUrl { get; private set; } = BuiltInGatewayUrl;
    public static bool IsGoogleGatewayConfigured => IsApprovedGatewayUrl(GoogleGatewayUrl);
    public static bool GitHubRuntimeConfigReachable { get; private set; }
    public static bool GoogleGatewayReachable { get; private set; }

    public static async Task InitializeAsync(CancellationToken ct = default)
    {
        await InitializeGate.WaitAsync(ct);
        try
        {
            OfficeNetworkRouteV1413.Initialize();
            if (!IsApprovedGatewayUrl(GoogleGatewayUrl)) GoogleGatewayUrl = BuiltInGatewayUrl;
            GitHubRuntimeConfigReachable = false;
            GoogleGatewayReachable = false;

            if (OfficeNetworkRouteV1413.IsExplicitSystemProxyActive())
            {
                NetworkHttpClientFactory.PreferRtdbRelayForCurrentNetwork("windows_system_proxy_detected");
                AppLog.Info("CONNECTIVITY_ROUTE_HINT",
                    "Phát hiện mạng dùng proxy hệ thống; giữ tuyến Office qua proxy/Google relay và kiểm tra gateway ở nền để không chặn khởi động.",
                    new Dictionary<string, object?>
                    {
                        ["mode"] = "office_system_proxy_google_gateway",
                        ["proxy_route"] = OfficeNetworkRouteV1413.GetProxyRouteLabel(),
                        ["google_gateway_reachable"] = false,
                        ["github_raw_skipped"] = true,
                        ["firebase_rtdb_route"] = "google_relay",
                        ["probe_mode"] = "background"
                    });
                StartBackgroundRefresh(officeMode: true);
                return;
            }

            AppLog.Info("CONNECTIVITY_ROUTE_HINT", "Không có proxy hệ thống bắt buộc; dùng direct-first và kiểm tra metadata/gateway ở nền để mở ứng dụng ngay.",
                new Dictionary<string, object?>
                {
                    ["mode"] = "adaptive_direct_first",
                    ["github_runtime_config_reachable"] = false,
                    ["google_gateway"] = IsGoogleGatewayConfigured,
                    ["firebase_rtdb_route"] = NetworkHttpClientFactory.IsRtdbRelayPreferred ? "google_relay" : "direct_firebase",
                    ["probe_mode"] = "background"
                });
            StartBackgroundRefresh(officeMode: false);
        }
        finally
        {
            InitializeGate.Release();
        }
    }

    private static void StartBackgroundRefresh(bool officeMode)
    {
        if (Interlocked.Exchange(ref _backgroundRefreshStarted, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!officeMode)
                    await RefreshOptionalRuntimeConfigAsync();
                GoogleGatewayReachable = await ProbeGatewayAsync(CancellationToken.None);
                AppLog.Info("CONNECTIVITY_BACKGROUND_PROBE_DONE",
                    GoogleGatewayReachable ? "Google Gateway phản hồi ở kiểm tra nền." : "Google Gateway chưa phản hồi ở kiểm tra nền; ứng dụng tiếp tục dùng retry/offline bảo vệ dữ liệu.",
                    new Dictionary<string, object?>
                    {
                        ["office_mode"] = officeMode,
                        ["github_runtime_config_reachable"] = GitHubRuntimeConfigReachable,
                        ["google_gateway_reachable"] = GoogleGatewayReachable,
                        ["gateway_url_approved"] = IsGoogleGatewayConfigured
                    });
            }
            catch (Exception ex)
            {
                AppLog.Warning("CONNECTIVITY_BACKGROUND_PROBE_FAILED", NetworkHttpClientFactory.Friendly(ex, "kết nối nền"));
            }
        });
    }

    private static async Task RefreshOptionalRuntimeConfigAsync()
    {
        try
        {
            using var linked = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await Http.GetAsync(ConfigUrl, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", $"GitHub runtime config trả HTTP {(int)response.StatusCode}; tiếp tục bằng gateway tích hợp sẵn.");
                return;
            }

            GitHubRuntimeConfigReachable = true;
            var json = await response.Content.ReadAsStringAsync(linked.Token);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("google_gateway_url", out var node)) return;
            var candidate = (node.GetString() ?? string.Empty).Trim();
            if (IsApprovedGatewayUrl(candidate)) GoogleGatewayUrl = candidate;
        }
        catch (Exception ex)
        {
            AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", NetworkHttpClientFactory.Friendly(ex, "GitHub runtime config"));
        }
    }

    private static async Task<bool> ProbeGatewayAsync(CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            var body = JsonSerializer.Serialize(new { action = "gateway_info", payload = new { } });
            using var request = new HttpRequestMessage(HttpMethod.Post, GoogleGatewayUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            if (!response.IsSuccessStatusCode) return false;
            var text = await response.Content.ReadAsStringAsync(linked.Token);
            if (GoogleGatewayResilienceV1428.LooksLikeHtmlOrInvalidEnvelope(text, response.Content.Headers.ContentType?.MediaType)) return false;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            AppLog.Warning("GOOGLE_GATEWAY_PROBE_FAILED", NetworkHttpClientFactory.Friendly(ex, "Google Gateway"));
            return false;
        }
    }

    private static bool IsApprovedGatewayUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "script.google.com", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.StartsWith("/macros/s/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/exec", StringComparison.Ordinal);
    }
}
