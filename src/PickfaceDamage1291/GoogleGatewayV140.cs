using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed record RemoteImageChange(int Sequence, string FileId, string Url, string Sha256);

internal sealed record RemoteReportChange(
    long ChangeSeq,
    bool Deleted,
    string Fingerprint,
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
    string CreatedBy,
    int Version,
    DateTime? UpdatedAt,
    string UpdatedBy,
    DateTime? DeletedAt,
    string DeletedBy,
    IReadOnlyList<RemoteImageChange> Images);

internal sealed record ReportChangePage(IReadOnlyList<RemoteReportChange> Changes, long LatestSeq, bool HasMore);

internal sealed record RemoteProductChange(
    long ChangeSeq,
    string Sku,
    string ProductName,
    string BaseUnit,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string SourceFile);

internal sealed record ProductChangePage(IReadOnlyList<RemoteProductChange> Changes, long LatestSeq, bool HasMore, int ServerCount);

internal static class GoogleGatewayV140
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task VerifyCapabilitiesAsync(CancellationToken ct = default)
    {
        JsonElement root;
        try
        {
            root = await CallAsync("gateway_info", new { }, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Action không được hỗ trợ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google Gateway đang chạy deployment cũ hoặc ứng dụng đang trỏ tới URL /exec cũ. Đây không phải lỗi quyền ADMIN. Hãy cập nhật đúng deployment Apps Script hiện hữu.", ex);
        }

        var version = Str(root, "version");
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("capabilities", out var node) && node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) capabilities.Add(item.GetString()!);

        var required = new[] { "pull_changes", "push_products", "pull_products", "append_audit", "list_audit", "delete_audit_range", "upload_log", "sync_report", "get_image" };
        var missing = required.Where(x => !capabilities.Contains(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Google Gateway {(string.IsNullOrWhiteSpace(version) ? "?" : version)} thiếu chức năng: {string.Join(", ", missing)}. Hãy deploy Code.gs v1.4.1 vào đúng deployment /exec đang dùng.");
    }

    public static async Task<ReportChangePage> PullReportChangesAsync(long afterSeq, int limit = 500, CancellationToken ct = default)
    {
        var root = await CallAsync("pull_changes", new { after_seq = Math.Max(0, afterSeq), limit = Math.Clamp(limit, 1, 1000) }, ct);
        var changes = new List<RemoteReportChange>();
        if (root.TryGetProperty("changes", out var node) && node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                var report = item.TryGetProperty("report", out var reportNode) ? reportNode : item;
                var images = new List<RemoteImageChange>();
                if (item.TryGetProperty("images", out var imageNode) && imageNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var image in imageNode.EnumerateArray())
                    {
                        images.Add(new RemoteImageChange(
                            Int(image, "sequence"),
                            Str(image, "file_id"),
                            Str(image, "url"),
                            Str(image, "sha256")));
                    }
                }

                var date = ParseDate(Str(report, "occurred_date"));
                var createdAt = ParseDateTime(Str(report, "created_at")) ?? DateTime.Now;
                changes.Add(new RemoteReportChange(
                    Long(item, "change_seq"),
                    Bool(item, "deleted"),
                    Str(item, "fingerprint"),
                    Str(report, "report_id"),
                    date,
                    Int(report, "hour"),
                    Int(report, "minute"),
                    Str(report, "shift"),
                    Str(report, "sku"),
                    Str(report, "product_name"),
                    Str(report, "location"),
                    Decimal(report, "quantity"),
                    Str(report, "base_unit"),
                    createdAt,
                    Str(report, "created_by"),
                    Math.Max(1, Int(report, "version")),
                    ParseDateTime(Str(report, "updated_at")),
                    Str(report, "updated_by"),
                    ParseDateTime(Str(item, "deleted_at")),
                    Str(item, "deleted_by"),
                    images));
            }
        }

        return new ReportChangePage(
            changes.OrderBy(x => x.ChangeSeq).ToList(),
            Long(root, "latest_seq"),
            Bool(root, "has_more"));
    }

    public static async Task<ProductChangePage> PullProductChangesAsync(long afterSeq, int limit = 500, CancellationToken ct = default)
    {
        var root = await CallAsync("pull_products", new { after_seq = Math.Max(0, afterSeq), limit = Math.Clamp(limit, 1, 1000) }, ct);
        var changes = new List<RemoteProductChange>();
        if (root.TryGetProperty("changes", out var node) && node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                changes.Add(new RemoteProductChange(
                    Long(item, "change_seq"),
                    Str(item, "sku"),
                    Str(item, "product_name"),
                    Str(item, "base_unit"),
                    ParseDateTime(Str(item, "first_seen_at")) ?? DateTime.UtcNow,
                    ParseDateTime(Str(item, "last_seen_at")) ?? DateTime.UtcNow,
                    Str(item, "source_file")));
            }
        }

        return new ProductChangePage(
            changes.OrderBy(x => x.ChangeSeq).ToList(),
            Long(root, "latest_seq"),
            Bool(root, "has_more"),
            Int(root, "server_count"));
    }

    public static async Task<(int Changed, long LatestSeq)> PushProductsAsync(IReadOnlyList<ProductRecord> products, CancellationToken ct = default)
    {
        var payload = new
        {
            products = products.Select(x => new
            {
                sku = x.Sku,
                product_name = x.ProductName,
                base_unit = x.BaseUnit,
                first_seen_at = x.FirstSeenAt.ToUniversalTime().ToString("O"),
                last_seen_at = x.LastSeenAt.ToUniversalTime().ToString("O"),
                source_file = x.SourceFile
            }).ToArray()
        };
        var root = await CallAsync("push_products", payload, ct);
        return (Int(root, "changed"), Long(root, "latest_seq"));
    }

    public static async Task AppendAuditAsync(FirebaseAuditEntry entry, CancellationToken ct = default)
    {
        var root = await CallAsync("append_audit", new
        {
            event_id = entry.EventId,
            device_id = entry.DeviceId,
            session_id = entry.SessionId,
            action = entry.Action,
            client_time = entry.ClientTime,
            details = entry.Details
        }, ct);
        if (!Bool(root, "stored") && !Bool(root, "duplicate"))
            throw new InvalidOperationException("Google Sheet chưa xác nhận lưu lịch sử.");
    }

    public static async Task<AuditPage> ListAuditPageAsync(long? beforeServerTimeExclusive = null, int pageSize = 100, CancellationToken ct = default)
    {
        var root = await CallAsync("list_audit", new
        {
            before_server_time_exclusive = beforeServerTimeExclusive,
            limit = Math.Clamp(pageSize, 1, 100)
        }, ct);

        var entries = new List<FirebaseAuditEntry>();
        if (root.TryGetProperty("entries", out var node) && node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                object? details = null;
                if (item.TryGetProperty("details", out var detailsNode)) details = detailsNode.Clone();
                entries.Add(new FirebaseAuditEntry
                {
                    EventId = Str(item, "event_id"),
                    Uid = Str(item, "uid"),
                    Username = Str(item, "username"),
                    DeviceId = Str(item, "device_id"),
                    SessionId = Str(item, "session_id"),
                    Action = Str(item, "action"),
                    ClientTime = Str(item, "client_time"),
                    ServerTime = Long(item, "server_time"),
                    Details = details
                });
            }
        }

        var next = root.TryGetProperty("next_before_server_time", out var nextNode) && nextNode.ValueKind == JsonValueKind.Number
            ? nextNode.GetInt64()
            : (long?)null;
        return new AuditPage(entries, Bool(root, "has_more"), next);
    }

    public static async Task<int> DeleteAuditRangeAsync(long fromInclusive, long toInclusive, CancellationToken ct = default)
    {
        var root = await CallAsync("delete_audit_range", new { from_server_time = fromInclusive, to_server_time = toInclusive }, ct);
        return Int(root, "deleted_count");
    }

    public static async Task<(byte[] Bytes, string MimeType, string Name)> GetImageAsync(string fileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("Thiếu mã ảnh Drive.", nameof(fileId));
        var root = await CallAsync("get_image", new { file_id = fileId }, ct);
        var data = Str(root, "data_base64");
        if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("Google không trả dữ liệu ảnh.");
        return (Convert.FromBase64String(data), Str(root, "mime_type"), Str(root, "name"));
    }

    public static async Task UploadLogAsync(string fileName, byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length == 0) throw new InvalidOperationException("File log rỗng.");
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("File log vượt giới hạn 8 MB. Hãy gửi các file log nhỏ hơn.");
        var root = await CallAsync("upload_log", new
        {
            file_name = fileName,
            device_id = SecureSessionStore.GetOrCreateDeviceId(),
            data_base64 = Convert.ToBase64String(bytes)
        }, ct);
        if (string.IsNullOrWhiteSpace(Str(root, "file_id")))
            throw new InvalidOperationException("Drive chưa xác nhận file log đã được lưu.");
    }

    private static async Task<JsonElement> CallAsync(string action, object payload, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info("GATEWAY_CALL_START", "Bắt đầu gọi Google gateway.", new Dictionary<string, object?>
        {
            ["action"] = action
        });

        try
        {
            var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
            if (session.OfflineMode) throw new InvalidOperationException("Đang làm việc offline.");
            if (!RuntimeConfigService.IsGoogleGatewayConfigured)
                throw new InvalidOperationException("Kết nối Google dùng chung chưa sẵn sàng.");

            await FirebaseClient.EnsureFreshAsync(session, ct);
            var body = JsonSerializer.Serialize(new { action, id_token = session.IdToken, payload });
            using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warning("GATEWAY_HTTP_ERROR", "Google gateway trả HTTP không thành công.", new Dictionary<string, object?>
                {
                    ["action"] = action,
                    ["http_status"] = (int)response.StatusCode,
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
                });
                throw new InvalidOperationException($"Gateway Google lỗi HTTP {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okNode) && okNode.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
                AppLog.Warning("GATEWAY_REJECTED", error ?? "Google từ chối yêu cầu.", new Dictionary<string, object?>
                {
                    ["action"] = action,
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
                });
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google từ chối yêu cầu." : error);
            }

            AppLog.Info("GATEWAY_CALL_OK", "Google gateway xử lý thành công.", new Dictionary<string, object?>
            {
                ["action"] = action,
                ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
            });
            return root.Clone();
        }
        catch (Exception ex)
        {
            AppLog.Exception("GATEWAY_CALL_FAILED", ex, new Dictionary<string, object?>
            {
                ["action"] = action,
                ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
            });
            throw;
        }
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n) || n.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return string.Empty;
        return n.ValueKind == JsonValueKind.String ? n.GetString() ?? string.Empty : n.ToString();
    }

    private static int Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n)) return 0;
        if (n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var value)) return value;
        return int.TryParse(n.ToString(), out value) ? value : 0;
    }

    private static long Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n)) return 0;
        if (n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var value)) return value;
        return long.TryParse(n.ToString(), out value) ? value : 0;
    }

    private static decimal Decimal(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n)) return 0;
        if (n.ValueKind == JsonValueKind.Number && n.TryGetDecimal(out var value)) return value;
        return decimal.TryParse(n.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0;
    }

    private static bool Bool(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var n)) return false;
        if (n.ValueKind == JsonValueKind.True) return true;
        if (n.ValueKind == JsonValueKind.False) return false;
        var s = n.ToString();
        return s == "1" || bool.TryParse(s, out var value) && value;
    }

    private static DateTime ParseDate(string text)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var iso))
            return iso.Date;
        if (DateTime.TryParseExact(text, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var display))
            return display.Date;
        return DateTime.Today;
    }

    private static DateTime? ParseDateTime(string text)
        => DateTimeOffset.TryParse(text, out var dto) ? dto.LocalDateTime : null;

    private static HttpClient CreateClient()
    {
        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(4), "PickfaceDamage1291-SyncV141");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
