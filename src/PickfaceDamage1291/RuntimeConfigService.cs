using System.Text.Json;

namespace PickfaceDamage1291;

internal static class RuntimeConfigService
{
    private static readonly HttpClient Http = CreateClient();
    private const string ConfigUrl = "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json";

    public static string GoogleGatewayUrl { get; private set; } = string.Empty;
    public static bool IsGoogleGatewayConfigured => Uri.TryCreate(GoogleGatewayUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await Http.GetAsync(ConfigUrl, linked.Token);
            if (!response.IsSuccessStatusCode) return;
            var json = await response.Content.ReadAsStringAsync(linked.Token);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("google_gateway_url", out var node))
                GoogleGatewayUrl = (node.GetString() ?? string.Empty).Trim();
        }
        catch
        {
            // Runtime metadata is optional for offline/local-first operation.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-RuntimeConfig");
        return client;
    }
}
