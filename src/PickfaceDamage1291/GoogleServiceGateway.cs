using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

/// <summary>
/// Google access is performed by the approved server-side Apps Script gateway.
/// The Windows EXE contains no Google OAuth client secret, refresh token or service-account key.
/// </summary>
internal static class GoogleService
{
    private static readonly HttpClient Http = CreateClient();

    public static bool IsClientConfigured() => RuntimeConfigService.IsGoogleGatewayConfigured;

    public static bool IsConnected() =>
        RuntimeConfigService.IsGoogleGatewayConfigured &&
        AppSession.Current is { OfflineMode: false };

    public static bool NeedsReconnect() => false;

    public static Task AuthorizeAsync() => VerifyBindingAsync(writeHeaders: true);

    public static Task ProvisionAsync() => VerifyBindingAsync(writeHeaders: true);

    public static async Task VerifyBindingAsync(bool writeHeaders = false)
    {
        await GoogleGatewayV140.VerifyCapabilitiesAsync();
        var payload = new
        {
            root_folder_id = CloudConfig.DriveRootFolderId,
            image_folder_id = CloudConfig.DriveImageFolderId,
            spreadsheet_id = CloudConfig.DamageSpreadsheetId,
            write_headers = writeHeaders
        };
        _ = await CallGatewayAsync("verify", payload);
    }

    /// <summary>
    /// Queue pending/error reports and return immediately. One coordinator serializes all Google writes,
    /// so automatic startup sync, manual sync and newly entered reports never compete with each other.
    /// </summary>
    public static Task SyncAllPendingAsync(IProgress<string>? progress = null)
    {
        EnsureGatewayReady();
        var added = BackgroundSyncCoordinator.EnqueuePending();
        progress?.Report(added > 0
            ? $"Đã đưa {added:N0} phiếu vào hàng đợi đồng bộ nền."
            : BackgroundSyncCoordinator.QueueCount > 0
                ? "Các phiếu đang nằm trong hàng đợi đồng bộ nền."
                : "Không có phiếu chờ đồng bộ.");
        return Task.CompletedTask;
    }

    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    public static async Task SyncReportAsync(DamageReport report, IProgress<int>? progress = null)
    {
        EnsureGatewayReady();
        await MutationGate.WaitAsync();
        try
        {
            try
            {
                progress?.Report(5);
                Database.SetReportStatus(report.ReportId, "SYNCING");
                var localImages = Database.GetImages(report.ReportId);
                var imagePayload = new List<object>();

                var imageIndex = 0;
                foreach (var image in localImages.OrderBy(x => x.Sequence))
                {
                    string? base64 = null;
                    string? mimeType = null;
                    string? originalName = null;
                    if (string.IsNullOrWhiteSpace(image.DriveFileId))
                    {
                        if (!File.Exists(image.LocalPath))
                            throw new FileNotFoundException("Không tìm thấy ảnh trên máy để đồng bộ.", image.LocalPath);
                        var prepared = await ImageUploadOptimizer.PrepareAsync(image.LocalPath);
                        base64 = Convert.ToBase64String(prepared.Bytes);
                        mimeType = prepared.MimeType;
                        originalName = prepared.FileName;

                        if (prepared.Optimized)
                        {
                            AppLog.Info("IMAGE_UPLOAD_OPTIMIZED", "Đã tối ưu ảnh trước khi upload Google Drive, giữ nguyên file local.", new Dictionary<string, object?>
                            {
                                ["report_id"] = report.ReportId,
                                ["sequence"] = image.Sequence,
                                ["original_bytes"] = prepared.OriginalBytes,
                                ["upload_bytes"] = prepared.Bytes.LongLength,
                                ["original_size"] = $"{prepared.OriginalWidth}x{prepared.OriginalHeight}",
                                ["upload_size"] = $"{prepared.UploadWidth}x{prepared.UploadHeight}",
                                ["quality"] = ImageUploadOptimizer.JpegQuality,
                                ["max_long_edge"] = ImageUploadOptimizer.MaxLongEdge
                            });
                        }
                    }

                    imagePayload.Add(new
                    {
                        sequence = image.Sequence,
                        drive_file_id = image.DriveFileId ?? string.Empty,
                        drive_link = image.DriveLink ?? string.Empty,
                        original_name = originalName ?? string.Empty,
                        mime_type = mimeType ?? string.Empty,
                        data_base64 = base64 ?? string.Empty
                    });

                    imageIndex++;
                    var imageProgress = localImages.Count == 0 ? 30 : 10 + (int)Math.Round(25d * imageIndex / localImages.Count);
                    progress?.Report(imageProgress);
                }

                if (localImages.Count == 0) progress?.Report(35);

                var payload = new
                {
                    report = new
                    {
                        report_id = report.ReportId,
                        occurred_date = report.OccurredDate.ToString("yyyy-MM-dd"),
                        hour = report.Hour,
                        minute = report.Minute,
                        shift = report.Shift,
                        sku = report.Sku,
                        product_name = report.ProductName,
                        location = report.Location,
                        quantity = decimal.Truncate(report.Quantity),
                        base_unit = report.BaseUnit,
                        created_at = report.CreatedAt.ToString("O"),
                        created_by = report.CreatedBy,
                        version = report.Version,
                        updated_at = report.UpdatedAt?.ToString("O") ?? string.Empty,
                        updated_by = report.UpdatedBy ?? string.Empty
                    },
                    images = imagePayload
                };

                progress?.Report(45);
                var root = await CallGatewayAsync("sync_report", payload);
                if (root.TryGetProperty("duplicate", out var duplicateNode) && duplicateNode.ValueKind == JsonValueKind.True)
                {
                    var existingId = root.TryGetProperty("duplicate_report_id", out var duplicateIdNode) ? duplicateIdNode.GetString() ?? string.Empty : string.Empty;
                    throw new DuplicateReportException(existingId);
                }
                if (root.TryGetProperty("conflict", out var conflictNode) && conflictNode.ValueKind == JsonValueKind.True)
                {
                    var remoteVersion = root.TryGetProperty("remote_version", out var remoteVersionNode) && remoteVersionNode.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;
                    var message = $"Phiếu đã thay đổi trên máy khác. Local v{report.Version}, Google v{remoteVersion}. Hệ thống không ghi đè tự động.";
                    SyncCacheStore.MarkConflict(report.ReportId, message);
                    throw new SyncConflictException(message);
                }
                progress?.Report(88);
                ValidateGatewayResult(report, localImages, root);

                if (root.TryGetProperty("images", out var imagesNode) && imagesNode.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in imagesNode.EnumerateArray())
                    {
                        var sequence = item.TryGetProperty("sequence", out var seqNode) ? seqNode.GetInt32() : 0;
                        var fileId = item.TryGetProperty("file_id", out var fileNode) ? fileNode.GetString() ?? string.Empty : string.Empty;
                        var link = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                        if (sequence > 0 && !string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(link))
                            Database.UpdateImageDrive(report.ReportId, sequence, fileId, link);
                    }
                }

                progress?.Report(96);
                var changeSeq = root.TryGetProperty("change_seq", out var changeSeqNode) && changeSeqNode.TryGetInt64(out var seqValue) ? seqValue : 0;
                var fingerprint = root.TryGetProperty("fingerprint", out var fingerprintNode) ? fingerprintNode.GetString() ?? string.Empty : string.Empty;
                if (changeSeq > 0) SyncCacheStore.MarkSyncedMetadata(report.ReportId, changeSeq, fingerprint);
                else Database.SetReportStatus(report.ReportId, "SYNCED");
                progress?.Report(100);
            }
            catch (DuplicateReportException)
            {
                throw;
            }
            catch (SyncConflictException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Database.SetReportStatus(report.ReportId, "ERROR", ex.Message);
                throw;
            }
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public static async Task MarkReportDeletedAsync(DamageReport report, string deletedBy)
    {
        EnsureGatewayReady();
        await MutationGate.WaitAsync();
        try
        {
            var now = DateTime.Now;
            var payload = new
            {
                report = new
                {
                    report_id = report.ReportId,
                    occurred_date = string.Empty,
                    hour = 0,
                    minute = 0,
                    shift = string.Empty,
                    sku = "__DELETED__",
                    product_name = "ĐÃ XÓA",
                    location = string.Empty,
                    quantity = 0,
                    base_unit = string.Empty,
                    created_at = report.CreatedAt.ToString("O"),
                    created_by = report.CreatedBy,
                    version = Math.Max(1, report.Version) + 1,
                    updated_at = now.ToString("O"),
                    updated_by = deletedBy
                },
                images = Array.Empty<object>()
            };

            var root = await CallGatewayAsync("sync_report", payload);
            if (root.TryGetProperty("conflict", out var conflictNode) && conflictNode.ValueKind == JsonValueKind.True)
            {
                var remoteVersion = root.TryGetProperty("remote_version", out var versionNode) && versionNode.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;
                throw new SyncConflictException($"Không thể xoá vì phiếu trên Google đã lên phiên bản {remoteVersion}. Hãy đồng bộ lại trước.");
            }
            var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
            var row = root.TryGetProperty("row", out var rowNode) && rowNode.TryGetInt32(out var rowValue) ? rowValue : 0;
            if (!string.Equals(returnedId, report.ReportId, StringComparison.Ordinal) || row < 2)
                throw new InvalidOperationException("Google chưa xác nhận ghi dấu xoá cho phiếu.");
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private static void ValidateGatewayResult(DamageReport report, IReadOnlyList<DamageImage> localImages, JsonElement root)
    {
        var returnedId = root.TryGetProperty("report_id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty;
        if (!string.Equals(returnedId, report.ReportId, StringComparison.Ordinal))
            throw new InvalidOperationException("Google xác nhận sai mã phiếu. Phiếu chưa được đánh dấu đồng bộ.");

        var row = root.TryGetProperty("row", out var rowNode) && rowNode.TryGetInt32(out var rowValue) ? rowValue : 0;
        if (row < 2)
            throw new InvalidOperationException("Google chưa xác nhận được dòng dữ liệu hợp lệ trên bảng tính.");

        var returnedImages = new Dictionary<int, (string FileId, string Url)>();
        if (root.TryGetProperty("images", out var imagesNode) && imagesNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in imagesNode.EnumerateArray())
            {
                var sequence = item.TryGetProperty("sequence", out var seqNode) && seqNode.TryGetInt32(out var seq) ? seq : 0;
                var fileId = item.TryGetProperty("file_id", out var fileNode) ? fileNode.GetString() ?? string.Empty : string.Empty;
                var url = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                if (sequence > 0) returnedImages[sequence] = (fileId, url);
            }
        }

        if (returnedImages.Count != localImages.Count)
            throw new InvalidOperationException($"Google chỉ xác nhận {returnedImages.Count}/{localImages.Count} ảnh. Phiếu sẽ chờ đồng bộ lại.");

        foreach (var image in localImages)
        {
            if (!returnedImages.TryGetValue(image.Sequence, out var saved) ||
                string.IsNullOrWhiteSpace(saved.FileId) || string.IsNullOrWhiteSpace(saved.Url))
                throw new InvalidOperationException($"Google chưa xác nhận ảnh {image.Sequence}. Phiếu sẽ chờ đồng bộ lại.");
        }
    }

    private static async Task<JsonElement> CallGatewayAsync(string action, object payload, CancellationToken ct = default)
    {
        EnsureGatewayReady();
        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        await FirebaseClient.EnsureFreshAsync(session, ct);

        var requestBody = JsonSerializer.Serialize(new
        {
            action,
            id_token = session.IdToken,
            payload
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Kết nối Google không thành công ({(int)response.StatusCode}). {TrimError(text)}");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch { throw new InvalidOperationException("Google trả dữ liệu không hợp lệ."); }
        using (doc)
        {
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okNode) && okNode.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google từ chối yêu cầu." : error);
            }
            return root.Clone();
        }
    }

    private static void EnsureGatewayReady()
    {
        if (AppSession.Current is null)
            throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        if (AppSession.Current.OfflineMode)
            throw new InvalidOperationException("Đang làm việc offline. Dữ liệu sẽ giữ trên máy và đồng bộ khi có mạng.");
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Kết nối Google chưa sẵn sàng. Dữ liệu vẫn được giữ trên máy và sẽ đồng bộ khi kết nối lại.");
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _ => "application/octet-stream"
    };

    private static string TrimError(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 600 ? text : text[..600] + "...";
    }

    private static HttpClient CreateClient()
    {
        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(4), "PickfaceDamage1291-GoogleGateway");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
