namespace PickfaceDamage1291;

internal sealed record AuditPage(
    IReadOnlyList<FirebaseAuditEntry> Entries,
    bool HasMore,
    long? NextBeforeServerTime);
