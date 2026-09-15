using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PickfaceDamage1291;

internal enum RemoteApplyResult
{
    Applied,
    Deleted,
    KeptLocalPending,
    Conflict,
    NoChange
}

internal static class SyncCacheStore
{
    private static string ConnectionString => $"Data Source={AppPaths.DatabaseFile};Cache=Shared";

    public static void Initialize()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
CREATE TABLE IF NOT EXISTS sync_state (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
""";
        cmd.ExecuteNonQuery();
        EnsureColumn(cn, "damage_reports", "fingerprint", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, "damage_reports", "remote_change_seq", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, "damage_images", "sha256", "TEXT NOT NULL DEFAULT ''");
    }

    public static long GetLong(string key)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key=$key";
        cmd.Parameters.AddWithValue("$key", key);
        var value = Convert.ToString(cmd.ExecuteScalar());
        return long.TryParse(value, out var parsed) ? parsed : 0;
    }

    public static void SetLong(string key, long value) => SetString(key, value.ToString(CultureInfo.InvariantCulture));

    public static string GetString(string key)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key=$key";
        cmd.Parameters.AddWithValue("$key", key);
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    public static void SetString(string key, string value)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO sync_state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public static bool ProductsDirty => string.Equals(GetString("products_dirty"), "1", StringComparison.Ordinal);
    public static void MarkProductsDirty() => SetString("products_dirty", "1");
    public static void ClearProductsDirty() => SetString("products_dirty", "0");

    public static RemoteApplyResult ApplyRemoteReport(RemoteReportChange change)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(change.ReportId) || change.ChangeSeq <= 0) return RemoteApplyResult.NoChange;

        var existing = GetLocalReportState(change.ReportId);
        if (change.Deleted)
        {
            if (existing is not null && IsPending(existing.Value.Status))
            {
                if (existing.Value.Version > change.Version)
                    return RemoteApplyResult.KeptLocalPending;
                if (existing.Value.Version == change.Version && !string.IsNullOrWhiteSpace(existing.Value.Fingerprint) &&
                    !string.Equals(existing.Value.Fingerprint, change.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    MarkConflict(change.ReportId, $"Google đã xoá/sửa cùng phiên bản {change.Version}. Cần ADMIN kiểm tra xung đột.");
                    return RemoteApplyResult.Conflict;
                }
            }

            if (Database.GetReportById(change.ReportId) is not null)
                Database.DeleteDamageReport(change.ReportId, string.IsNullOrWhiteSpace(change.DeletedBy) ? "REMOTE" : change.DeletedBy, true);
            UpsertRemoteTombstone(change);
            return RemoteApplyResult.Deleted;
        }

        if (existing is not null && IsPending(existing.Value.Status))
        {
            if (change.Version < existing.Value.Version)
                return RemoteApplyResult.KeptLocalPending;

            if (change.Version == existing.Value.Version &&
                !string.IsNullOrWhiteSpace(change.Fingerprint) &&
                !string.IsNullOrWhiteSpace(existing.Value.Fingerprint) &&
                string.Equals(change.Fingerprint, existing.Value.Fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                MarkSyncedMetadata(change.ReportId, change.ChangeSeq, change.Fingerprint);
                return RemoteApplyResult.NoChange;
            }

            MarkConflict(change.ReportId,
                $"Phiếu đã thay đổi trên máy khác. Local v{existing.Value.Version}, Google v{change.Version}. Không tự ghi đè.");
            return RemoteApplyResult.Conflict;
        }

        UpsertRemoteReport(change);
        return RemoteApplyResult.Applied;
    }

    public static void ApplyRemoteProduct(RemoteProductChange product)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(product.Sku)) return;
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
INSERT INTO products(sku,product_name,base_unit,first_seen_at,last_seen_at,source_file)
VALUES($sku,$name,$base,$first,$last,$source)
ON CONFLICT(sku) DO UPDATE SET
 product_name=excluded.product_name,
 base_unit=excluded.base_unit,
 first_seen_at=excluded.first_seen_at,
 last_seen_at=excluded.last_seen_at,
 source_file=excluded.source_file
""";
        cmd.Parameters.AddWithValue("$sku", product.Sku);
        cmd.Parameters.AddWithValue("$name", product.ProductName);
        cmd.Parameters.AddWithValue("$base", product.BaseUnit);
        cmd.Parameters.AddWithValue("$first", product.FirstSeenAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$last", product.LastSeenAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$source", product.SourceFile);
        cmd.ExecuteNonQuery();
    }

    public static string GetImageHash(string reportId, int sequence)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT sha256 FROM damage_images WHERE report_id=$id AND sequence=$seq";
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$seq", sequence);
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    public static void SetImageHash(string reportId, int sequence, string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return;
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE damage_images SET sha256=$hash WHERE report_id=$id AND sequence=$seq";
        cmd.Parameters.AddWithValue("$hash", sha256);
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$seq", sequence);
        cmd.ExecuteNonQuery();
    }

    public static void SetImageLocalPath(string reportId, int sequence, string localPath)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE damage_images SET local_path=$path WHERE report_id=$id AND sequence=$seq";
        cmd.Parameters.AddWithValue("$path", localPath);
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$seq", sequence);
        cmd.ExecuteNonQuery();
    }

    public static void MarkSyncedMetadata(string reportId, long changeSeq, string fingerprint)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
UPDATE damage_reports SET
 fingerprint=$fingerprint,
 remote_change_seq=$seq,
 sync_status='SYNCED',
 synced_at=$synced,
 last_error=NULL
WHERE report_id=$id
""";
        cmd.Parameters.AddWithValue("$fingerprint", fingerprint ?? string.Empty);
        cmd.Parameters.AddWithValue("$seq", changeSeq);
        cmd.Parameters.AddWithValue("$synced", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.ExecuteNonQuery();
    }

    public static void MarkConflict(string reportId, string message)
    {
        Initialize();
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE damage_reports SET sync_status='CONFLICT', last_error=$error WHERE report_id=$id";
        cmd.Parameters.AddWithValue("$error", message);
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.ExecuteNonQuery();
        AppLog.Warning("SYNC_CONFLICT", message, new Dictionary<string, object?> { ["report_id"] = reportId });
    }

    public static void RemoveLocalDuplicate(string reportId, string canonicalReportId)
    {
        if (Database.GetReportById(reportId) is not null)
            Database.DeleteDamageReport(reportId, "SYSTEM_DUPLICATE", false);
        AppLog.Warning("DUPLICATE_REMOVED", "Đã loại bản ghi local trùng dữ liệu trung tâm.", new Dictionary<string, object?>
        {
            ["local_report_id"] = reportId,
            ["canonical_report_id"] = canonicalReportId
        });
    }

    private static void UpsertRemoteReport(RemoteReportChange change)
    {
        using var cn = Open();
        using var tx = cn.BeginTransaction();

        var oldPaths = new Dictionary<int, (string Path, string FileId)>(capacity: 5);
        using (var old = cn.CreateCommand())
        {
            old.Transaction = tx;
            old.CommandText = "SELECT sequence,local_path,COALESCE(drive_file_id,'') FROM damage_images WHERE report_id=$id";
            old.Parameters.AddWithValue("$id", change.ReportId);
            using var reader = old.ExecuteReader();
            while (reader.Read()) oldPaths[reader.GetInt32(0)] = (reader.GetString(1), reader.GetString(2));
        }

        using (var report = cn.CreateCommand())
        {
            report.Transaction = tx;
            report.CommandText = """
INSERT INTO damage_reports(
 report_id,occurred_date,occurred_hour,occurred_minute,shift,sku,product_name,location,quantity,base_unit,
 created_at,sync_status,synced_at,last_error,created_by,version,updated_at,updated_by,fingerprint,remote_change_seq)
VALUES($id,$date,$hour,$minute,$shift,$sku,$name,$location,$quantity,$base,$created,'SYNCED',$synced,NULL,$createdBy,$version,$updatedAt,$updatedBy,$fingerprint,$seq)
ON CONFLICT(report_id) DO UPDATE SET
 occurred_date=excluded.occurred_date,
 occurred_hour=excluded.occurred_hour,
 occurred_minute=excluded.occurred_minute,
 shift=excluded.shift,
 sku=excluded.sku,
 product_name=excluded.product_name,
 location=excluded.location,
 quantity=excluded.quantity,
 base_unit=excluded.base_unit,
 created_at=excluded.created_at,
 sync_status='SYNCED',
 synced_at=excluded.synced_at,
 last_error=NULL,
 created_by=excluded.created_by,
 version=excluded.version,
 updated_at=excluded.updated_at,
 updated_by=excluded.updated_by,
 fingerprint=excluded.fingerprint,
 remote_change_seq=excluded.remote_change_seq
""";
            report.Parameters.AddWithValue("$id", change.ReportId);
            report.Parameters.AddWithValue("$date", change.OccurredDate.ToString("yyyy-MM-dd"));
            report.Parameters.AddWithValue("$hour", change.Hour);
            report.Parameters.AddWithValue("$minute", change.Minute);
            report.Parameters.AddWithValue("$shift", change.Shift);
            report.Parameters.AddWithValue("$sku", change.Sku);
            report.Parameters.AddWithValue("$name", change.ProductName);
            report.Parameters.AddWithValue("$location", change.Location);
            report.Parameters.AddWithValue("$quantity", change.Quantity.ToString(CultureInfo.InvariantCulture));
            report.Parameters.AddWithValue("$base", change.BaseUnit);
            report.Parameters.AddWithValue("$created", change.CreatedAt.ToUniversalTime().ToString("O"));
            report.Parameters.AddWithValue("$synced", DateTime.UtcNow.ToString("O"));
            report.Parameters.AddWithValue("$createdBy", change.CreatedBy);
            report.Parameters.AddWithValue("$version", Math.Max(1, change.Version));
            report.Parameters.AddWithValue("$updatedAt", change.UpdatedAt.HasValue ? change.UpdatedAt.Value.ToUniversalTime().ToString("O") : DBNull.Value);
            report.Parameters.AddWithValue("$updatedBy", string.IsNullOrWhiteSpace(change.UpdatedBy) ? DBNull.Value : change.UpdatedBy);
            report.Parameters.AddWithValue("$fingerprint", change.Fingerprint ?? string.Empty);
            report.Parameters.AddWithValue("$seq", change.ChangeSeq);
            report.ExecuteNonQuery();
        }

        using (var clear = cn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM damage_images WHERE report_id=$id";
            clear.Parameters.AddWithValue("$id", change.ReportId);
            clear.ExecuteNonQuery();
        }

        foreach (var image in change.Images.OrderBy(x => x.Sequence).Take(5))
        {
            oldPaths.TryGetValue(image.Sequence, out var old);
            var keepPath = !string.IsNullOrWhiteSpace(old.Path) && File.Exists(old.Path) &&
                           (string.IsNullOrWhiteSpace(image.FileId) || string.Equals(old.FileId, image.FileId, StringComparison.Ordinal))
                ? old.Path
                : string.Empty;
            using var insert = cn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO damage_images(report_id,sequence,local_path,drive_file_id,drive_link,sha256) VALUES($id,$seq,$path,$file,$link,$hash)";
            insert.Parameters.AddWithValue("$id", change.ReportId);
            insert.Parameters.AddWithValue("$seq", image.Sequence);
            insert.Parameters.AddWithValue("$path", keepPath);
            insert.Parameters.AddWithValue("$file", string.IsNullOrWhiteSpace(image.FileId) ? DBNull.Value : image.FileId);
            insert.Parameters.AddWithValue("$link", string.IsNullOrWhiteSpace(image.Url) ? DBNull.Value : image.Url);
            insert.Parameters.AddWithValue("$hash", image.Sha256 ?? string.Empty);
            insert.ExecuteNonQuery();
        }

        using (var removeTombstone = cn.CreateCommand())
        {
            removeTombstone.Transaction = tx;
            removeTombstone.CommandText = "DELETE FROM deleted_reports WHERE report_id=$id";
            removeTombstone.Parameters.AddWithValue("$id", change.ReportId);
            removeTombstone.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void UpsertRemoteTombstone(RemoteReportChange change)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
INSERT INTO deleted_reports(report_id,deleted_at,deleted_by,remote_tombstoned)
VALUES($id,$at,$by,1)
ON CONFLICT(report_id) DO UPDATE SET deleted_at=excluded.deleted_at,deleted_by=excluded.deleted_by,remote_tombstoned=1
""";
        cmd.Parameters.AddWithValue("$id", change.ReportId);
        cmd.Parameters.AddWithValue("$at", (change.DeletedAt ?? DateTime.Now).ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$by", string.IsNullOrWhiteSpace(change.DeletedBy) ? "REMOTE" : change.DeletedBy);
        cmd.ExecuteNonQuery();
    }

    private static (string Status, int Version, string Fingerprint)? GetLocalReportState(string reportId)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT sync_status,version,fingerprint FROM damage_reports WHERE report_id=$id";
        cmd.Parameters.AddWithValue("$id", reportId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.IsDBNull(1) ? 1 : r.GetInt32(1), r.IsDBNull(2) ? string.Empty : r.GetString(2)) : null;
    }

    private static bool IsPending(string status)
        => status is "PENDING" or "ERROR" or "OFFLINE_PENDING" or "SYNCING" or "CONFLICT";

    private static SqliteConnection Open()
    {
        AppPaths.EnsureCreated();
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        return cn;
    }

    private static void EnsureColumn(SqliteConnection cn, string table, string column, string definition)
    {
        using var info = cn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var reader = info.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var alter = cn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
