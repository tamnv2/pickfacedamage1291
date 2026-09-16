using System.Text.Json;

namespace PickfaceDamage1291;

internal static class RuntimeConfigService
{
    private const string ConfigUrl = "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json";
    private const string BuiltInGatewayUrl = "https://script.google.com/macros/s/AKfycbwfB-NdKPRj-GN4T_MRS89aaJ8ihRsPfT9qd2wUhBntOAOtBF1ycuaNJRdYN5JCFf12/exec";
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(8), "PickfaceDamage1291-RuntimeConfig");

    public static string GoogleGatewayUrl { get; private set; } = BuiltInGatewayUrl;
    public static bool IsGoogleGatewayConfigured => IsApprovedGatewayUrl(GoogleGatewayUrl);
    public static bool GitHubRuntimeConfigReachable { get; private set; }

    public static async Task InitializeAsync(CancellationToken ct = default)
    {
        // The approved Google endpoint is always built in. GitHub only acts as optional public metadata
        // and as a route hint: on a Google-only Office network we immediately keep Firebase RTDB on the
        // Apps Script relay instead of waiting for repeated direct Firebase timeouts.
        GoogleGatewayUrl = BuiltInGatewayUrl;
        GitHubRuntimeConfigReachable = false;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await Http.GetAsync(ConfigUrl, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                SelectGoogleOnlyRoute($"github_runtime_config_http_{(int)response.StatusCode}");
                return;
            }

            GitHubRuntimeConfigReachable = true;
            var json = await response.Content.ReadAsStringAsync(linked.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("google_gateway_url", out var node))
            {
                var candidate = (node.GetString() ?? string.Empty).Trim();
                if (IsApprovedGatewayUrl(candidate)) GoogleGatewayUrl = candidate;
            }

            AppLog.Info("CONNECTIVITY_ROUTE_HINT", "GitHub truy cập được; Firebase ưu tiên direct và tự fallback Google khi cần.",
                new Dictionary<string, object?> { ["mode"] = "adaptive_direct_first" });
        }
        catch (Exception ex)
        {
            AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", NetworkHttpClientFactory.Friendly(ex, "GitHub runtime config"));
            SelectGoogleOnlyRoute("github_runtime_config_unreachable");
        }
    }

    private static void SelectGoogleOnlyRoute(string reason)
    {
        NetworkHttpClientFactory.PreferRtdbRelayForCurrentNetwork(reason);
        AppLog.Info("CONNECTIVITY_ROUTE_HINT", "Mạng hiện tại không truy cập được GitHub; ưu tiên Google Gateway/RTDB relay cho phiên mạng này.",
            new Dictionary<string, object?>
            {
                ["mode"] = "google_only_restricted",
                ["reason"] = reason
            });
    }

    private static bool IsApprovedGatewayUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "script.google.com", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.StartsWith("/macros/s/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/exec", StringComparison.Ordinal);
    }
}
