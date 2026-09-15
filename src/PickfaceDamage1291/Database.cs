using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PickfaceDamage1291;

internal static class Database
{
    private static string ConnectionString => $"Data Source={AppPaths.DatabaseFile};Cache=Shared";
    private const string ReportColumns = "report_id,occurred_date,occurred_hour,occurred_minute,shift,sku,product_name,location,quantity,base_unit,created_at,sync_status,synced_at,last_error,created_by,version,updated_at,updated_by";

    public static void Initialize()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS products (
    sku TEXT PRIMARY KEY,
    product_name TEXT NOT NULL,
    base_unit TEXT NOT NULL,
    first_seen_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    source_file TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS product_imports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_name TEXT NOT NULL,
    file_sha256 TEXT NOT NULL,
    imported_at TEXT NOT NULL,
    total_rows INTEGER NOT NULL,
    unique_skus INTEGER NOT NULL,
    new_skus INTEGER NOT NULL,
    unchanged_skus INTEGER NOT NULL,
    conflicts INTEGER NOT NULL,
    invalid_rows INTEGER NOT NULL,
    UNIQUE(file_sha256)
);
CREATE TABLE IF NOT EXISTS product_change_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sku TEXT NOT NULL,
    old_name TEXT,
    new_name TEXT,
    old_base_unit TEXT,
    new_base_unit TEXT,
    decision TEXT NOT NULL,
    changed_at TEXT NOT NULL,
    source_file TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS damage_reports (
    report_id TEXT PRIMARY KEY,
    occurred_date TEXT NOT NULL,
    occurred_hour INTEGER NOT NULL,
    occurred_minute INTEGER NOT NULL,
    shift TEXT NOT NULL,
    sku TEXT NOT NULL,
    product_name TEXT NOT NULL,
    location TEXT NOT NULL,
    quantity TEXT NOT NULL,
    base_unit TEXT NOT NULL,
    created_at TEXT NOT NULL,
    sync_status TEXT NOT NULL,
    synced_at TEXT,
    last_error TEXT,
    created_by TEXT NOT NULL DEFAULT '',
    version INTEGER NOT NULL DEFAULT 1,
    updated_at TEXT,
    updated_by TEXT
);
CREATE TABLE IF NOT EXISTS damage_images (
    report_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    local_path TEXT NOT NULL,
    drive_file_id TEXT,
    drive_link TEXT,
    PRIMARY KEY(report_id, sequence),
    FOREIGN KEY(report_id) REFERENCES damage_reports(report_id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS deleted_reports (
    report_id TEXT PRIMARY KEY,
    deleted_at TEXT NOT NULL,
    deleted_by TEXT NOT NULL,
    remote_tombstoned INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_damage_created ON damage_reports(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_damage_status ON damage_reports(sync_status);
""";
        cmd.ExecuteNonQuery();

        // Safe migration for databases created by v1.0/v1.1.
        EnsureColumn(cn, "damage_reports", "created_by", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, "damage_reports", "version", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(cn, "damage_reports", "updated_at", "TEXT");
        EnsureColumn(cn, "damage_reports", "updated_by", "TEXT");
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

    private static SqliteConnection Open()
    {
        AppPaths.EnsureCreated();
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        return cn;
    }

    public static ProductRecord? GetProduct(string sku)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT sku, product_name, base_unit, first_seen_at, last_seen_at, source_file FROM products WHERE sku=$sku";
        cmd.Parameters.AddWithValue("$sku", sku.Trim());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ProductRecord(
            r.GetString(0), r.GetString(1), r.GetString(2),
            DateTime.Parse(r.GetString(3), null, DateTimeStyles.RoundtripKind),
            DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind),
            r.GetString(5));
    }

    public static int GetProductCount()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM products";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static List<ProductRecord> GetProducts(string search = "", int limit = 500)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
SELECT sku, product_name, base_unit, first_seen_at, last_seen_at, source_file
FROM products
WHERE $q='' OR sku LIKE $like OR product_name LIKE $like
ORDER BY sku
LIMIT $limit
""";
        cmd.Parameters.AddWithValue("$q", search.Trim());
        cmd.Parameters.AddWithValue("$like", $"%{search.Trim()}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var result = new List<ProductRecord>();
        while (r.Read())
        {
            result.Add(new ProductRecord(
                r.GetString(0), r.GetString(1), r.GetString(2),
                DateTime.Parse(r.GetString(3), null, DateTimeStyles.RoundtripKind),
                DateTime.Parse(r.GetString(4), null, DateTimeStyles.RoundtripKind),
                r.GetString(5)));
        }
        return result;
    }

    public static bool HasImportedHash(string sha256)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM product_imports WHERE file_sha256=$hash)";
        cmd.Parameters.AddWithValue("$hash", sha256);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public static void ApplyImport(ImportPreview preview)
    {
        using var cn = Open();
        using var tx = cn.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        var source = Path.GetFileName(preview.FilePath);

        foreach (var p in preview.NewProducts)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO products(sku,product_name,base_unit,first_seen_at,last_seen_at,source_file) VALUES($s,$n,$b,$t,$t,$f)";
            cmd.Parameters.AddWithValue("$s", p.Sku);
            cmd.Parameters.AddWithValue("$n", p.ProductName);
            cmd.Parameters.AddWithValue("$b", p.BaseUnit);
            cmd.Parameters.AddWithValue("$t", now);
            cmd.Parameters.AddWithValue("$f", source);
            cmd.ExecuteNonQuery();
        }

        foreach (var p in preview.UnchangedProducts)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE products SET last_seen_at=$t, source_file=$f WHERE sku=$s";
            cmd.Parameters.AddWithValue("$t", now);
            cmd.Parameters.AddWithValue("$f", source);
            cmd.Parameters.AddWithValue("$s", p.Sku);
            cmd.ExecuteNonQuery();
        }

        foreach (var c in preview.Conflicts)
        {
            using (var history = cn.CreateCommand())
            {
                history.Transaction = tx;
                history.CommandText = "INSERT INTO product_change_history(sku,old_name,new_name,old_base_unit,new_base_unit,decision,changed_at,source_file) VALUES($s,$on,$nn,$ob,$nb,$d,$t,$f)";
                history.Parameters.AddWithValue("$s", c.Sku);
                history.Parameters.AddWithValue("$on", c.OldName);
                history.Parameters.AddWithValue("$nn", c.NewName);
                history.Parameters.AddWithValue("$ob", c.OldBaseUnit);
                history.Parameters.AddWithValue("$nb", c.NewBaseUnit);
                history.Parameters.AddWithValue("$d", c.UseNew ? "USE_NEW" : "KEEP_OLD");
                history.Parameters.AddWithValue("$t", now);
                history.Parameters.AddWithValue("$f", source);
                history.ExecuteNonQuery();
            }

            using var update = cn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = c.UseNew
                ? "UPDATE products SET product_name=$n, base_unit=$b, last_seen_at=$t, source_file=$f WHERE sku=$s"
                : "UPDATE products SET last_seen_at=$t, source_file=$f WHERE sku=$s";
            update.Parameters.AddWithValue("$s", c.Sku);
            update.Parameters.AddWithValue("$t", now);
            update.Parameters.AddWithValue("$f", source);
            if (c.UseNew)
            {
                update.Parameters.AddWithValue("$n", c.NewName);
                update.Parameters.AddWithValue("$b", c.NewBaseUnit);
            }
            update.ExecuteNonQuery();
        }

        using (var import = cn.CreateCommand())
        {
            import.Transaction = tx;
            import.CommandText = "INSERT INTO product_imports(file_name,file_sha256,imported_at,total_rows,unique_skus,new_skus,unchanged_skus,conflicts,invalid_rows) VALUES($f,$h,$t,$tr,$u,$n,$same,$c,$i)";
            import.Parameters.AddWithValue("$f", source);
            import.Parameters.AddWithValue("$h", preview.FileHash);
            import.Parameters.AddWithValue("$t", now);
            import.Parameters.AddWithValue("$tr", preview.TotalRows);
            import.Parameters.AddWithValue("$u", preview.UniqueSkus);
            import.Parameters.AddWithValue("$n", preview.NewProducts.Count);
            import.Parameters.AddWithValue("$same", preview.UnchangedProducts.Count);
            import.Parameters.AddWithValue("$c", preview.Conflicts.Count);
            import.Parameters.AddWithValue("$i", preview.InvalidRows);
            import.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void SaveDamageReport(DamageReport report, IReadOnlyList<string> localImages)
    {
        using var cn = Open();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
INSERT INTO damage_reports(report_id,occurred_date,occurred_hour,occurred_minute,shift,sku,product_name,location,quantity,base_unit,created_at,sync_status,synced_at,last_error,created_by,version,updated_at,updated_by)
VALUES($id,$date,$h,$m,$shift,$sku,$name,$loc,$qty,$base,$created,$status,NULL,NULL,$createdBy,$version,NULL,NULL)
""";
            BindReportParameters(cmd, report);
            cmd.Parameters.AddWithValue("$createdBy", report.CreatedBy ?? string.Empty);
            cmd.Parameters.AddWithValue("$version", Math.Max(1, report.Version));
            cmd.ExecuteNonQuery();
        }

        for (var i = 0; i < localImages.Count; i++)
        {
            using var img = cn.CreateCommand();
            img.Transaction = tx;
            img.CommandText = "INSERT INTO damage_images(report_id,sequence,local_path) VALUES($id,$seq,$path)";
            img.Parameters.AddWithValue("$id", report.ReportId);
            img.Parameters.AddWithValue("$seq", i + 1);
            img.Parameters.AddWithValue("$path", localImages[i]);
            img.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void BindReportParameters(SqliteCommand cmd, DamageReport report)
    {
        cmd.Parameters.AddWithValue("$id", report.ReportId);
        cmd.Parameters.AddWithValue("$date", report.OccurredDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$h", report.Hour);
        cmd.Parameters.AddWithValue("$m", report.Minute);
        cmd.Parameters.AddWithValue("$shift", report.Shift);
        cmd.Parameters.AddWithValue("$sku", report.Sku);
        cmd.Parameters.AddWithValue("$name", report.ProductName);
        cmd.Parameters.AddWithValue("$loc", report.Location);
        cmd.Parameters.AddWithValue("$qty", report.Quantity.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$base", report.BaseUnit);
        cmd.Parameters.AddWithValue("$created", report.CreatedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$status", report.SyncStatus);
    }

    public static DamageReport UpdateDamageReport(DamageReport proposed, IReadOnlyList<DamageImage> images, string updatedBy)
    {
        var current = GetReportById(proposed.ReportId) ?? throw new InvalidOperationException("Không tìm thấy phiếu cần sửa trong dữ liệu local.");
        var now = DateTime.Now;
        var updated = proposed with
        {
            CreatedAt = current.CreatedAt,
            CreatedBy = current.CreatedBy,
            Version = Math.Max(1, current.Version) + 1,
            UpdatedAt = now,
            UpdatedBy = updatedBy,
            SyncStatus = "PENDING",
            SyncedAt = null,
            LastError = null
        };

        using var cn = Open();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
UPDATE damage_reports SET
 occurred_date=$date, occurred_hour=$h, occurred_minute=$m, shift=$shift,
 sku=$sku, product_name=$name, location=$loc, quantity=$qty, base_unit=$base,
 sync_status='PENDING', synced_at=NULL, last_error=NULL,
 version=$version, updated_at=$updatedAt, updated_by=$updatedBy
WHERE report_id=$id
""";
            BindReportParameters(cmd, updated);
            cmd.Parameters.AddWithValue("$version", updated.Version);
            cmd.Parameters.AddWithValue("$updatedAt", now.ToUniversalTime().ToString("O"));
            cmd.Parameters.AddWithValue("$updatedBy", updatedBy);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Không cập nhật được phiếu local.");
        }

        using (var del = cn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM damage_images WHERE report_id=$id";
            del.Parameters.AddWithValue("$id", updated.ReportId);
            del.ExecuteNonQuery();
        }
        var seq = 1;
        foreach (var image in images.Take(5))
        {
            using var ins = cn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO damage_images(report_id,sequence,local_path,drive_file_id,drive_link) VALUES($id,$seq,$path,$file,$link)";
            ins.Parameters.AddWithValue("$id", updated.ReportId);
            ins.Parameters.AddWithValue("$seq", seq++);
            ins.Parameters.AddWithValue("$path", image.LocalPath);
            ins.Parameters.AddWithValue("$file", (object?)image.DriveFileId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$link", (object?)image.DriveLink ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return updated;
    }

    public static List<DamageReport> GetReports(int limit = 500)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {ReportColumns} FROM damage_reports ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var result = new List<DamageReport>();
        while (r.Read()) result.Add(ReadReport(r));
        return result;
    }

    public static DamageReport? GetReportById(string reportId)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {ReportColumns} FROM damage_reports WHERE report_id=$id";
        cmd.Parameters.AddWithValue("$id", reportId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadReport(r) : null;
    }

    public static List<DamageReport> GetPendingReports()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {ReportColumns} FROM damage_reports WHERE sync_status<>'SYNCED' ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        var result = new List<DamageReport>();
        while (r.Read()) result.Add(ReadReport(r));
        return result;
    }

    private static DamageReport ReadReport(SqliteDataReader r) => new(
        r.GetString(0), DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
        decimal.Parse(r.GetString(8), CultureInfo.InvariantCulture), r.GetString(9),
        DateTime.Parse(r.GetString(10), null, DateTimeStyles.RoundtripKind).ToLocalTime(), r.GetString(11),
        r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12), null, DateTimeStyles.RoundtripKind).ToLocalTime(),
        r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? string.Empty : r.GetString(14),
        r.IsDBNull(15) ? 1 : r.GetInt32(15),
        r.IsDBNull(16) ? null : DateTime.Parse(r.GetString(16), null, DateTimeStyles.RoundtripKind).ToLocalTime(),
        r.IsDBNull(17) ? null : r.GetString(17));

    public static List<DamageImage> GetImages(string reportId)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT report_id,sequence,local_path,drive_file_id,drive_link FROM damage_images WHERE report_id=$id ORDER BY sequence";
        cmd.Parameters.AddWithValue("$id", reportId);
        using var r = cmd.ExecuteReader();
        var result = new List<DamageImage>();
        while (r.Read()) result.Add(new DamageImage(r.GetString(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return result;
    }

    public static void UpdateImageDrive(string reportId, int sequence, string fileId, string link)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE damage_images SET drive_file_id=$file, drive_link=$link WHERE report_id=$id AND sequence=$seq";
        cmd.Parameters.AddWithValue("$file", fileId);
        cmd.Parameters.AddWithValue("$link", link);
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$seq", sequence);
        cmd.ExecuteNonQuery();
    }

    public static string? FindExactDuplicateReport(DamageReport candidate, IReadOnlyList<string> candidateImagePaths)
    {
        var candidateHashes = HashImages(candidateImagePaths);
        if (candidateHashes is null) return null;

        var ids = new List<string>();
        using (var cn = Open())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
SELECT report_id
FROM damage_reports
WHERE occurred_date=$date
  AND occurred_hour=$h
  AND occurred_minute=$m
  AND shift=$shift
  AND sku=$sku
  AND product_name=$name
  AND location=$loc
  AND quantity=$qty
  AND base_unit=$base
ORDER BY created_at DESC
""";
            cmd.Parameters.AddWithValue("$date", candidate.OccurredDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$h", candidate.Hour);
            cmd.Parameters.AddWithValue("$m", candidate.Minute);
            cmd.Parameters.AddWithValue("$shift", candidate.Shift);
            cmd.Parameters.AddWithValue("$sku", candidate.Sku);
            cmd.Parameters.AddWithValue("$name", candidate.ProductName);
            cmd.Parameters.AddWithValue("$loc", candidate.Location);
            cmd.Parameters.AddWithValue("$qty", candidate.Quantity.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$base", candidate.BaseUnit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }

        foreach (var id in ids)
        {
            var existing = GetImages(id).Select(x => x.LocalPath).ToList();
            var existingHashes = HashImages(existing);
            if (existingHashes is null) continue;
            if (candidateHashes.SequenceEqual(existingHashes, StringComparer.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    private static List<string>? HashImages(IReadOnlyList<string> paths)
    {
        var hashes = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) return null;
            try { hashes.Add(ImageHashService.Sha256File(path)); }
            catch { return null; }
        }
        hashes.Sort(StringComparer.OrdinalIgnoreCase);
        return hashes;
    }

    public static bool DeleteDamageReport(string reportId, string deletedBy, bool remoteTombstoned)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return false;
        var imagePaths = new List<string>();
        var deleted = false;

        using (var cn = Open())
        using (var tx = cn.BeginTransaction())
        {
            using (var images = cn.CreateCommand())
            {
                images.Transaction = tx;
                images.CommandText = "SELECT local_path FROM damage_images WHERE report_id=$id";
                images.Parameters.AddWithValue("$id", reportId);
                using var r = images.ExecuteReader();
                while (r.Read()) imagePaths.Add(r.GetString(0));
            }

            using (var tombstone = cn.CreateCommand())
            {
                tombstone.Transaction = tx;
                tombstone.CommandText = """
INSERT INTO deleted_reports(report_id,deleted_at,deleted_by,remote_tombstoned)
VALUES($id,$at,$by,$remote)
ON CONFLICT(report_id) DO UPDATE SET
 deleted_at=excluded.deleted_at,
 deleted_by=excluded.deleted_by,
 remote_tombstoned=excluded.remote_tombstoned
""";
                tombstone.Parameters.AddWithValue("$id", reportId);
                tombstone.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                tombstone.Parameters.AddWithValue("$by", deletedBy);
                tombstone.Parameters.AddWithValue("$remote", remoteTombstoned ? 1 : 0);
                tombstone.ExecuteNonQuery();
            }

            using (var del = cn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM damage_reports WHERE report_id=$id";
                del.Parameters.AddWithValue("$id", reportId);
                deleted = del.ExecuteNonQuery() == 1;
            }
            tx.Commit();
        }

        if (!deleted) return false;

        var root = Path.GetFullPath(AppPaths.PendingImages).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in imagePaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(full)) File.Delete(full);
                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                    Directory.Delete(parent, false);
            }
            catch
            {
                // DB deletion remains authoritative. Orphan local files can be cleaned later.
            }
        }
        return true;
    }

    public static void SetReportStatus(string reportId, string status, string? error = null)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = status == "SYNCED"
            ? "UPDATE damage_reports SET sync_status=$s,synced_at=$t,last_error=NULL WHERE report_id=$id"
            : "UPDATE damage_reports SET sync_status=$s,last_error=$e WHERE report_id=$id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$id", reportId);
        if (status == "SYNCED") cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        else cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
