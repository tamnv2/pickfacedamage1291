using System.Text.Json.Serialization;

namespace PickfaceDamage1291;

internal sealed class FirebaseUserProfile
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("permissions")]
    public Dictionary<string, bool> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);

    public bool HasPermission(string key)
    {
        if (IsAdmin) return true;
        return Permissions.TryGetValue(key, out var allowed) && allowed;
    }
}

internal sealed class FirebaseSession
{
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; set; } = DateTime.MinValue;
    public FirebaseUserProfile Profile { get; set; } = new();
    public ActiveOperatorRecord? CachedOperator { get; set; }
    public bool OfflineMode { get; set; }
}

internal sealed class ActiveOperatorRecord
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("generation")]
    public long Generation { get; set; }

    [JsonPropertyName("acquired_at")]
    public long AcquiredAt { get; set; }

    [JsonPropertyName("lease_until")]
    public long LeaseUntil { get; set; }

    [JsonPropertyName("last_seen")]
    public long LastSeen { get; set; }

    [JsonIgnore]
    public DateTimeOffset LeaseUntilLocal => DateTimeOffset.FromUnixTimeMilliseconds(LeaseUntil).ToLocalTime();
}

internal sealed record ActiveOperatorSnapshot(ActiveOperatorRecord? Value, string ETag);

internal sealed class FirebaseAuditEntry
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("client_time")]
    public string ClientTime { get; set; } = string.Empty;

    [JsonPropertyName("server_time")]
    public object? ServerTime { get; set; }

    [JsonPropertyName("details")]
    public object? Details { get; set; }
}

internal static class AppSession
{
    public static FirebaseSession? Current { get; set; }
    public static OperatorLeaseManager? OperatorManager { get; set; }

    public static bool IsAuthenticated => Current is not null;
    public static bool IsAdmin => Current?.Profile.IsAdmin == true;

    public static string UserLabel => Current is null
        ? "Chưa đăng nhập"
        : $"{Current.Profile.Username} ({Current.Profile.Role.ToUpperInvariant()})";
}
