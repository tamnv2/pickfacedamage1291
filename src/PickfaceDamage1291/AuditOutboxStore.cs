using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class AuditOutboxStore
{
    private static string ConnectionString => $"Data Source={AppPaths.DatabaseFile};Cache=Shared";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static SqliteConnection Open()
    {
        AppPaths.EnsureCreated();
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
CREATE TABLE IF NOT EXISTS audit_outbox (
    event_id TEXT PRIMARY KEY,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_audit_outbox_created ON audit_outbox(created_at);
""";
        cmd.ExecuteNonQuery();
        return cn;
    }

    public static void Enqueue(FirebaseAuditEntry entry)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO audit_outbox(event_id,payload_json,created_at) VALUES($id,$json,$created)";
        cmd.Parameters.AddWithValue("$id", entry.EventId);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry, JsonOptions));
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public static List<FirebaseAuditEntry> List(int limit = 200)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM audit_outbox ORDER BY created_at LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var reader = cmd.ExecuteReader();
        var result = new List<FirebaseAuditEntry>();
        while (reader.Read())
        {
            try
            {
                var item = JsonSerializer.Deserialize<FirebaseAuditEntry>(reader.GetString(0), JsonOptions);
                if (item is not null) result.Add(item);
            }
            catch
            {
                // Keep the corrupt row for manual diagnosis; do not crash business flow.
            }
        }
        return result;
    }

    public static void Delete(string eventId)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_outbox WHERE event_id=$id";
        cmd.Parameters.AddWithValue("$id", eventId);
        cmd.ExecuteNonQuery();
    }

    public static int Count()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_outbox";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
