namespace PickfaceDamage1291;

internal sealed record ProductRecord(
    string Sku,
    string ProductName,
    string BaseUnit,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string SourceFile);

internal sealed record ProductCandidate(string Sku, string ProductName, string BaseUnit);

internal sealed record ProductConflict(
    string Sku,
    string OldName,
    string NewName,
    string OldBaseUnit,
    string NewBaseUnit)
{
    public bool UseNew { get; set; }
}

internal sealed class ImportPreview
{
    public string FilePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int UniqueSkus { get; init; }
    public int InvalidRows { get; init; }
    public int DuplicateRows { get; init; }
    public List<ProductCandidate> NewProducts { get; init; } = [];
    public List<ProductCandidate> UnchangedProducts { get; init; } = [];
    public List<ProductConflict> Conflicts { get; init; } = [];
    public List<string> SourceConflicts { get; init; } = [];
}

internal sealed record DamageReport(
    string ReportId,
    DateTime OccurredDate,
    int Hour,
    int Minute,
    string Shift,
    string Sku,
    string ProductName,
    string Location,
    decimal Quantity,
    string BaseUnit,
    DateTime CreatedAt,
    string SyncStatus,
    DateTime? SyncedAt,
    string? LastError,
    string CreatedBy = "",
    int Version = 1,
    DateTime? UpdatedAt = null,
    string? UpdatedBy = null);

internal sealed record DamageImage(
    string ReportId,
    int Sequence,
    string LocalPath,
    string? DriveFileId,
    string? DriveLink);

internal sealed class GoogleSettings
{
    // Kept only for backward compatibility with old local settings. New versions do not
    // require selecting an OAuth JSON file.
    public string OAuthClientJsonPath { get; set; } = string.Empty;

    public string RootFolderId { get; set; } = CloudConfig.DriveRootFolderId;
    public string ImageFolderId { get; set; } = CloudConfig.DriveImageFolderId;
    public string SpreadsheetId { get; set; } = CloudConfig.DamageSpreadsheetId;

    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresUtc { get; set; } = DateTime.MinValue;
    public string OAuthClientIdAtGrant { get; set; } = string.Empty;

    // LastShift is retained to migrate existing installations. New builds remember shift per UID.
    public string LastShift { get; set; } = "Ca 1";
    public Dictionary<string, string> LastShiftByUser { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
