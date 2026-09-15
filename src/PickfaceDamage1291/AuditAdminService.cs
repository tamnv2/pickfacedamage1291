namespace PickfaceDamage1291;

internal static class AuditAdminService
{
    public static async Task<AuditPage> ListPageAsync(
        FirebaseSession adminSession,
        long? beforeServerTimeExclusive = null,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        EnsureAdmin(adminSession);
        return await GoogleGatewayV140.ListAuditPageAsync(beforeServerTimeExclusive, pageSize, ct);
    }

    public static async Task<int> DeleteRangeAsync(
        FirebaseSession adminSession,
        long fromInclusive,
        long toInclusive,
        CancellationToken ct = default)
    {
        EnsureAdmin(adminSession);
        if (fromInclusive > toInclusive) throw new ArgumentException("Khoảng ngày xóa không hợp lệ.");
        return await GoogleGatewayV140.DeleteAuditRangeAsync(fromInclusive, toInclusive, ct);
    }

    public static long ParseServerTime(object? value)
    {
        if (value is System.Text.Json.JsonElement e && e.ValueKind == System.Text.Json.JsonValueKind.Number && e.TryGetInt64(out var n)) return n;
        if (value is long l) return l;
        if (value is int i) return i;
        return long.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0;
    }

    private static void EnsureAdmin(FirebaseSession session)
    {
        if (session.OfflineMode || !session.Profile.IsAdmin)
            throw new InvalidOperationException("Cần ADMIN online để xử lý lịch sử.");
    }
}
