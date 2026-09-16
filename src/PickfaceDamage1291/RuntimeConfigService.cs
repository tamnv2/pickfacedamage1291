using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class RuntimeConfigService
{
    private const string ConfigUrl = "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json";
    private const string BuiltInGatewayUrl = "https://script.google.com/macros/s/AKfycbwfB-NdKPRj-GN4T_MRS89aaJ8ihRsPfT9qd2wUhBntOAOtBF1ycuaNJRdYN5JCFf12/exec";
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(10), "PickfaceDamage1291-RuntimeConfig");
    private static readonly SemaphoreSlim InitializeGate = new(1, 1);

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
            GoogleGatewayUrl = BuiltInGatewayUrl;
            GitHubRuntimeConfigReachable = false;
            GoogleGatewayReachable = false;

            // Measured on the Office network: Windows exposes an explicit authenticated/system proxy.
            // Direct TCP to Apps Script/Drive/Firebase RTDB is blocked, while HTTPS through the Windows
            // proxy reaches the Google gateway. In this mode raw.githubusercontent.com and RTDB may return
            // 403, so neither is a valid Internet/offline signal. Skip GitHub runtime metadata completely
            // and pin RTDB business traffic to the Google gateway for this network.
            if (OfficeNetworkRouteV1413.IsExplicitSystemProxyActive())
            {
                NetworkHttpClientFactory.PreferRtdbRelayForCurrentNetwork("windows_system_proxy_detected");
                GoogleGatewayReachable = await ProbeGatewayAsync(ct);
                AppLog.Info("CONNECTIVITY_ROUTE_HINT",
                    GoogleGatewayReachable
                        ? "Phát hiện mạng dùng proxy hệ thống; Google Gateway hoạt động, chọn tuyến Office đã đo thực tế."
                        : "Phát hiện mạng dùng proxy hệ thống nhưng Google Gateway chưa phản hồi; giữ cấu hình gateway và cho phép cơ chế retry/offline bảo vệ dữ liệu.",
                    new Dictionary<string, object?>
                    {
                        ["mode"] = "office_system_proxy_google_gateway",
                        ["proxy_route"] = OfficeNetworkRouteV1413.GetProxyRouteLabel(),
                        ["google_gateway_reachable"] = GoogleGatewayReachable,
                        ["github_raw_skipped"] = true,
                        ["firebase_rtdb_route"] = "google_relay"
                    });
                return;
            }

            // Normal/PDA network: use direct-first. GitHub runtime-config is optional metadata only;
            // failure here must never classify the whole application as offline.
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(5));
                using var response = await Http.GetAsync(ConfigUrl, linked.Token);
                if (response.IsSuccessStatusCode)
                {
                    GitHubRuntimeConfigReachable = true;
                    var json = await response.Content.ReadAsStringAsync(linked.Token);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("google_gateway_url", out var node))
                    {
                        var candidate = (node.GetString() ?? string.Empty).Trim();
                        if (IsApprovedGatewayUrl(candidate)) GoogleGatewayUrl = candidate;
                    }
                }
                else
                {
                    AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", $"GitHub runtime config trả HTTP {(int)response.StatusCode}; tiếp tục bằng gateway tích hợp sẵn.");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", NetworkHttpClientFactory.Friendly(ex, "GitHub runtime config"));
            }

            AppLog.Info("CONNECTIVITY_ROUTE_HINT", "Không có proxy hệ thống bắt buộc; Firebase ưu tiên direct và tự fallback Google khi cần.",
                new Dictionary<string, object?>
                {
                    ["mode"] = "adaptive_direct_first",
                    ["github_runtime_config_reachable"] = GitHubRuntimeConfigReachable,
                    ["google_gateway"] = IsGoogleGatewayConfigured,
                    ["firebase_rtdb_route"] = NetworkHttpClientFactory.IsRtdbRelayPreferred ? "google_relay" : "direct_firebase"
                });
        }
        finally
        {
            InitializeGate.Release();
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
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            if (!response.IsSuccessStatusCode) return false;
            var text = await response.Content.ReadAsStringAsync(linked.Token);
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
