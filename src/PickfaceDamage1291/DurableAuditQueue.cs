namespace PickfaceDamage1291;

internal static class DurableAuditQueue
{
    public static void Enqueue(FirebaseSession session, string action, object? details = null, string? sessionId = null)
    {
        var entry = new FirebaseAuditEntry
        {
            EventId = Guid.NewGuid().ToString("N"),
            Uid = session.Uid,
            Username = session.Profile.Username,
            DeviceId = SecureSessionStore.GetOrCreateDeviceId(),
            SessionId = sessionId ?? session.CachedOperator?.SessionId ?? string.Empty,
            Action = action,
            ClientTime = DateTimeOffset.Now.ToString("O"),
            ServerTime = new Dictionary<string, string> { [".sv"] = "timestamp" },
            Details = details
        };
        AuditOutboxStore.Enqueue(entry);
    }
}
