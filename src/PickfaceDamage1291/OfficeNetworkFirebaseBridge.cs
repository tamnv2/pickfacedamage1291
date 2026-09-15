using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

/// <summary>
/// Fallback transport for the small active_operator coordination record when a corporate network
/// allows Google Apps Script but blocks/intercepts direct Firebase Realtime Database traffic.
/// Direct Firebase remains primary; this bridge is only used after the direct path fails.
/// </summary>
internal static class OfficeNetworkFirebaseBridge
{
    private static readonly HttpClient Http = NetworkHttpClientFactory.Create(TimeSpan.FromSeconds(20), "PickfaceDamage1291-OfficeFirebaseBridge");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ActiveOperatorSnapshot> GetSnapshotAsync(FirebaseSession session, CancellationToken ct = default)
    {
        var root = await CallAsync(session, "get", null, null, ct);
        var etag = root.TryGetProperty("etag", out var etagNode) ? etagNode.GetString() ?? "*" : "*";
        ActiveOperatorRecord? value = null;
        if (root.TryGetProperty("value", out var valueNode) && valueNode.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            value = valueNode.Deserialize<ActiveOperatorRecord>(JsonOptions);
        return new ActiveOperatorSnapshot(value, etag);
    }

    public static async Task<bool> PutAsync(FirebaseSession session, ActiveOperatorRecord value, string etag, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var root = await CallAsync(session, "put", etag, payload, ct);
        return !root.TryGetProperty("applied", out var node) || node.GetBoolean();
    }

    public static async Task<bool> DeleteAsync(FirebaseSession session, string etag, CancellationToken ct = default)
    {
        var root = await CallAsync(session, "delete", etag, null, ct);
        return !root.TryGetProperty("applied", out var node) || node.GetBoolean();
    }

    public static async Task<long> GetServerNowMsAsync(FirebaseSession session, CancellationToken ct = default)
    {
        var root = await CallAsync(session, "now", null, null, ct);
        if (root.TryGetProperty("server_now", out var node) && node.TryGetInt64(out var value)) return value;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static async Task<JsonElement> CallAsync(
        FirebaseSession session,
        string operation,
        string? etag,
        string? payloadBase64,
        CancellationToken ct)
    {
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Google Gateway dự phòng chưa được cấu hình.");

        var query = new StringBuilder(RuntimeConfigService.GoogleGatewayUrl)
            .Append("?action=operator_proxy")
            .Append("&op=").Append(Uri.EscapeDataString(operation))
            .Append("&id_token=").Append(Uri.EscapeDataString(session.IdToken))
            .Append("&nonce=").Append(Guid.NewGuid().ToString("N"));
        if (!string.IsNullOrWhiteSpace(etag)) query.Append("&etag=").Append(Uri.EscapeDataString(etag));
        if (!string.IsNullOrWhiteSpace(payloadBase64)) query.Append("&payload_b64=").Append(Uri.EscapeDataString(payloadBase64));

        using var response = await Http.GetAsync(query.ToString(), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Gateway operator proxy HTTP {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okNode) || okNode.ValueKind != JsonValueKind.True)
        {
            var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Google Gateway không xử lý được trạng thái active_operator."
                : error);
        }

        AppLog.Info("OFFICE_FIREBASE_BRIDGE_OK", "Đã dùng Google Gateway thay cho kết nối Firebase RTDB trực tiếp.",
            new Dictionary<string, object?> { ["operation"] = operation });
        return root.Clone();
    }
}
