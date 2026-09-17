using System.Net;

namespace PickfaceDamage1291;

internal static class GoogleGatewayResilienceV1428
{
    private static readonly HashSet<string> RetryableActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "gateway_info",
        "pull_changes",
        "pull_products",
        "append_audit",
        "list_audit",
        "delete_audit_range",
        "get_image",
        "verify",
        "login_by_username",
        "password_reset_by_username",
        "sync_login_aliases"
    };

    internal static SemaphoreSlim TransportGate { get; } = new(2, 2);

    internal static bool ShouldRetry(string action) => RetryableActions.Contains(action);

    internal static bool IsTransientHttpStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return code is 404 or 408 or 425 or 429 || code >= 500;
    }

    internal static bool LooksLikeHtmlOrInvalidEnvelope(string text, string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0) return true;
        if (trimmed[0] == '<') return true;
        if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)) return true;
        return trimmed[0] is not ('{' or '[');
    }

    internal static TimeSpan AttemptTimeout(string action)
    {
        if (string.Equals(action, "get_image", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromSeconds(30);
        if (string.Equals(action, "login_by_username", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "password_reset_by_username", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "sync_login_aliases", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromSeconds(30);
        if (string.Equals(action, "pull_changes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "pull_products", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "list_audit", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromSeconds(22);
        return TimeSpan.FromSeconds(18);
    }

    internal static TimeSpan RetryDelay(int completedAttempt)
        => completedAttempt switch
        {
            <= 1 => TimeSpan.FromMilliseconds(450),
            2 => TimeSpan.FromMilliseconds(1200),
            _ => TimeSpan.FromMilliseconds(1800)
        };
}
