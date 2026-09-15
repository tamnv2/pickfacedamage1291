using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class AuditAdminService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static string DatabaseUrl => CloudConfig.FirebaseDatabaseUrl.TrimEnd('/');

    public static async Task<AuditPage> ListPageAsync(FirebaseSession adminSession, long? beforeServerTimeExclusive = null, int pageSize = 100, CancellationToken ct = default)
    {
        EnsureAdmin(adminSession);
        await FirebaseClient.EnsureFreshAsync(adminSession, ct);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var url = DbUrl("audit", adminSession.IdToken) + $"&orderBy=%22server_time%22&limitToLast={pageSize + 1}";
        if (beforeServerTimeExclusive.HasValue)
            url += $"&endAt={Math.Max(0, beforeServerTimeExclusive.Value - 1)}";

        using var response = await Http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không tải được lịch sử: HTTP {(int)response.StatusCode}.");
        if (body == "null") return new AuditPage([], false, null);

        var map = JsonSerializer.Deserialize<Dictionary<string, FirebaseAuditEntry>>(body, JsonOptions) ?? [];
        var ordered = map.Values
            .OrderByDescending(x => ParseServerTime(x.ServerTime))
            .ThenByDescending(x => x.EventId, StringComparer.Ordinal)
            .ToList();
        var hasMore = ordered.Count > pageSize;
        var page = ordered.Take(pageSize).ToList();
        var next = hasMore && page.Count > 0 ? ParseServerTime(page[^1].ServerTime) : (long?)null;
        return new AuditPage(page, hasMore, next);
    }

    public static async Task<int> DeleteRangeAsync(FirebaseSession adminSession, long fromInclusive, long toInclusive, CancellationToken ct = default)
    {
        EnsureAdmin(adminSession);
        if (fromInclusive > toInclusive) throw new ArgumentException("Khoảng ngày xóa không hợp lệ.");
        await FirebaseClient.EnsureFreshAsync(adminSession, ct);

        var queryUrl = DbUrl("audit", adminSession.IdToken) + $"&orderBy=%22server_time%22&startAt={fromInclusive}&endAt={toInclusive}";
        using var readResponse = await Http.GetAsync(queryUrl, ct);
        var body = await readResponse.Content.ReadAsStringAsync(ct);
        if (!readResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không tải được phạm vi log cần xóa: HTTP {(int)readResponse.StatusCode}.");
        if (body == "null") return 0;

        var map = JsonSerializer.Deserialize<Dictionary<string, FirebaseAuditEntry>>(body, JsonOptions) ?? [];
        var keys = map
            .Where(x => !string.Equals(x.Value.Action, "AUDIT_LOGS_DELETED", StringComparison.Ordinal))
            .Select(x => x.Key)
            .ToList();
        if (keys.Count == 0) return 0;

        var patch = keys.ToDictionary(x => x, _ => (object?)null, StringComparer.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), DbUrl("audit", adminSession.IdToken))
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, JsonOptions), Encoding.UTF8, "application/json")
        };
        using var deleteResponse = await Http.SendAsync(request, ct);
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync(ct);
        if (!deleteResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không xóa được log: HTTP {(int)deleteResponse.StatusCode}: {deleteBody}");

        await FirebaseClient.AppendAuditAsync(adminSession, "AUDIT_LOGS_DELETED", new
        {
            from_server_time = fromInclusive,
            to_server_time = toInclusive,
            deleted_count = keys.Count
        }, ct: ct);
        return keys.Count;
    }

    public static long ParseServerTime(object? value)
    {
        if (value is JsonElement e && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n)) return n;
        if (value is long l) return l;
        if (value is int i) return i;
        return long.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0;
    }

    private static string DbUrl(string path, string idToken)
        => $"{DatabaseUrl}/{path.Trim('/')}.json?auth={Uri.EscapeDataString(idToken)}";

    private static void EnsureAdmin(FirebaseSession session)
    {
        if (session.OfflineMode || !session.Profile.IsAdmin)
            throw new InvalidOperationException("Cần ADMIN online để xử lý lịch sử.");
    }
}
