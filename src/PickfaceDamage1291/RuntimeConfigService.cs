using System.Text.Json;

namespace PickfaceDamage1291;

internal static class RuntimeConfigService
{
    private const string ConfigUrl = "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json";
    private const string BuiltInGatewayUrl = "https://script.google.com/macros/s/AKfycbwfB-NdKPRj-GN4T_MRS89aaJ8ihRsPfT9qd2wUhBntOAOtBF1ycuaNJRdYN5JCFf12/exec";
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(8), "PickfaceDamage1291-RuntimeConfig");

    public static string GoogleGatewayUrl { get; private set; } = BuiltInGatewayUrl;
    public static bool IsGoogleGatewayConfigured => IsApprovedGatewayUrl(GoogleGatewayUrl);

    public static async Task InitializeAsync(CancellationToken ct = default)
    {
        // The Google gateway URL is public metadata, not a credential. Keep the approved endpoint
        // built into the EXE so a corporate network that allows Google but blocks GitHub does not
        // accidentally disable the whole application. GitHub runtime-config remains an optional
        // way to update the public endpoint without rebuilding the EXE.
        GoogleGatewayUrl = BuiltInGatewayUrl;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await Http.GetAsync(ConfigUrl, linked.Token);
            if (!response.IsSuccessStatusCode) return;
            var json = await response.Content.ReadAsStringAsync(linked.Token);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("google_gateway_url", out var node)) return;
            var candidate = (node.GetString() ?? string.Empty).Trim();
            if (IsApprovedGatewayUrl(candidate)) GoogleGatewayUrl = candidate;
        }
        catch (Exception ex)
        {
            AppLog.Warning("RUNTIME_CONFIG_FETCH_FAILED", NetworkHttpClientFactory.Friendly(ex, "GitHub runtime config"));
            // Built-in approved Google endpoint remains active.
        }
    }

    private static bool IsApprovedGatewayUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "script.google.com", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.StartsWith("/macros/s/", StringComparison.Ordinal) && uri.AbsolutePath.EndsWith("/exec", StringComparison.Ordinal);
    }
}
