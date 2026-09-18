namespace PickfaceDamage1291;

internal sealed record CloudSyncSummary(
    int ReportsPulled,
    int ReportsPushed,
    int ReportsDeleted,
    int ReportConflicts,
    int ProductsPulled,
    int ProductsPushed,
    int DuplicatesRemoved);

internal sealed class DuplicateReportException(string existingReportId)
    : InvalidOperationException($"Phiếu trùng hoàn toàn với dữ liệu trung tâm. ID đã có: {existingReportId}")
{
    public string ExistingReportId { get; } = existingReportId;
}

internal sealed class SyncConflictException(string message) : InvalidOperationException(message);

internal static class CloudSyncService
{
    private const string ReportSeqKey = "report_change_seq";
    private const string ProductSeqKey = "product_change_seq";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static long _lastReportPullStamp;

    private static bool IsReportPullFresh(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero) return false;
        var stamp = Volatile.Read(ref _lastReportPullStamp);
        return stamp > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(stamp) <= maxAge;
    }

    public static async Task<CloudSyncSummary> SyncNowAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!GoogleService.IsConnected())
            throw new InvalidOperationException("Cần kết nối online để đồng bộ dữ liệu giữa các máy.");

        await Gate.WaitAsync(ct);
        try
        {
            SyncCacheStore.Initialize();
            AppLog.Info("CLOUD_SYNC_START", "Bắt đầu đồng bộ hai chiều Google Sheet.");
            var reportsPulled = 0;
            var reportsPushed = 0;
            var reportsDeleted = 0;
            var conflicts = 0;
            var productsPulled = 0;
            var productsPushed = 0;
            var duplicates = 0;

            progress?.Report("Đang chuẩn bị đồng bộ hai chiều... 5%");
            if (SyncCacheStore.ProductsDirty && AppSession.Current?.Profile.IsAdmin == true)
            {
                progress?.Report("Đang đẩy danh mục SKU thay đổi từ máy này... 10%");
                productsPushed += await PushLocalProductCatalogCoreAsync(null, ct);
            }

            progress?.Report("Đang nhận thay đổi phiếu từ Google... 20%");
            var pull1 = await PullReportsAsync(null, ct);
            reportsPulled += pull1.Applied;
            reportsDeleted += pull1.Deleted;
            conflicts += pull1.Conflicts;

            progress?.Report("Đang đồng bộ danh mục SKU... 35%");
            var products = await PullProductsAsync(null, ct);
            productsPulled += products.Pulled;
            if (products.ServerCount == 0 && Database.GetProductCount() > 0 && AppSession.Current?.Profile.IsAdmin == true)
            {
                progress?.Report("Đang khởi tạo danh mục SKU dùng chung... 42%");
                productsPushed += await PushLocalProductCatalogCoreAsync(null, ct);
                var confirmProducts = await PullProductsAsync(null, ct);
                productsPulled += confirmProducts.Pulled;
            }

            var pending = Database.GetPendingReports().ToList();
            if (pending.Count > 0)
            {
                var pendingIndex = 0;
                progress?.Report($"Đang đẩy {pending.Count:N0} phiếu local lên Google... 45%");
                foreach (var report in pending)
                {
                    ct.ThrowIfCancellationRequested();
                    pendingIndex++;
                    try
                    {
                        var currentIndex = pendingIndex;
                        var itemProgress = new Progress<int>(value =>
                        {
                            var completedBefore = currentIndex - 1;
                            var fractional = completedBefore + Math.Clamp(value, 0, 100) / 100d;
                            var percent = 45 + (int)Math.Round(35d * fractional / pending.Count);
                            progress?.Report($"Đang đẩy phiếu {currentIndex:N0}/{pending.Count:N0} lên Google... {Math.Clamp(percent, 45, 80)}%");
                        });
                        await GoogleService.SyncReportAsync(report, itemProgress);
                        reportsPushed++;
                    }
                    catch (DuplicateReportException ex)
                    {
                        SyncCacheStore.RemoveLocalDuplicate(report.ReportId, ex.ExistingReportId);
                        duplicates++;
                    }
                    catch (SyncConflictException)
                    {
                        conflicts++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Exception("REPORT_PUSH_FAILED", ex, new Dictionary<string, object?>
                        {
                            ["report_id"] = report.ReportId,
                            ["sku"] = report.Sku,
                            ["version"] = report.Version
                        });
                    }
                }
            }

            progress?.Report("Đang xác nhận thay đổi mới nhất từ Google... 85%");
            var pull2 = await PullReportsAsync(null, ct);
            reportsPulled += pull2.Applied;
            reportsDeleted += pull2.Deleted;
            conflicts += pull2.Conflicts;

            try
            {
                if (AppSession.Current is { } session)
                {
                    progress?.Report("Đang đồng bộ lịch sử thao tác lên Google... 95%");
                    await FirebaseClient.FlushAuditOutboxAsync(session, ct);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception("AUDIT_FLUSH_FAILED", ex);
            }

            var summary = new CloudSyncSummary(reportsPulled, reportsPushed, reportsDeleted, conflicts, productsPulled, productsPushed, duplicates);
            progress?.Report($"Đồng bộ hoàn tất 100%: nhận {reportsPulled:N0}, gửi {reportsPushed:N0}, xoá {reportsDeleted:N0}, SKU nhận {productsPulled:N0}, xung đột {conflicts:N0}.");
            AppLog.Info("CLOUD_SYNC_DONE", "Hoàn tất đồng bộ hai chiều.", new Dictionary<string, object?>
            {
                ["reports_pulled"] = reportsPulled,
                ["reports_pushed"] = reportsPushed,
                ["reports_deleted"] = reportsDeleted,
                ["conflicts"] = conflicts,
                ["products_pulled"] = productsPulled,
                ["products_pushed"] = productsPushed,
                ["duplicates_removed"] = duplicates
            });
            return summary;
        }
        catch (Exception ex)
        {
            AppLog.Exception("CLOUD_SYNC_FAILED", ex);
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<(int ReportsPulled, int ReportsDeleted, int ProductsPulled)> PullSharedDataAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!GoogleService.IsConnected())
            throw new InvalidOperationException("Cần kết nối online để nhận dữ liệu dùng chung.");

        await Gate.WaitAsync(ct);
        try
        {
            SyncCacheStore.Initialize();
            progress?.Report("Đang nhận thay đổi phiếu từ Google... 10%");
            var reports = await PullReportsAsync(null, ct);
            progress?.Report("Đang nhận danh mục SKU dùng chung... 55%");
            var products = await PullProductsAsync(null, ct);
            progress?.Report("Đã nhận dữ liệu dùng chung. 100%");
            return (reports.Applied, reports.Deleted, products.Pulled);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<(int ReportsPulled, int ReportsDeleted, bool SkippedFresh)> PullReportsOnlyAsync(
        IProgress<string>? progress = null,
        TimeSpan? skipIfFreshFor = null,
        CancellationToken ct = default)
    {
        if (!GoogleService.IsConnected())
            throw new InvalidOperationException("Cần kết nối online để nhận dữ liệu phiếu dùng chung.");

        await Gate.WaitAsync(ct);
        try
        {
            SyncCacheStore.Initialize();
            if (skipIfFreshFor is { } maxAge && IsReportPullFresh(maxAge))
            {
                progress?.Report("Dữ liệu phiếu vừa được kiểm tra, không cần nhận lại.");
                return (0, 0, true);
            }

            progress?.Report("Đang kiểm tra dữ liệu phiếu mới từ Google... 10%");
            var reports = await PullReportsAsync(null, ct);
            progress?.Report("Đã kiểm tra dữ liệu phiếu mới. 100%");
            return (reports.Applied, reports.Deleted, false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<int> PushLocalProductCatalogAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (AppSession.Current?.Profile.IsAdmin != true)
            throw new InvalidOperationException("Chỉ ADMIN được cập nhật danh mục SKU dùng chung.");
        if (!GoogleService.IsConnected())
        {
            SyncCacheStore.MarkProductsDirty();
            throw new InvalidOperationException("Danh mục đã lưu local và sẽ đẩy lên Google khi online.");
        }

        await Gate.WaitAsync(ct);
        try
        {
            return await PushLocalProductCatalogCoreAsync(progress, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<int> PushLocalProductCatalogCoreAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (AppSession.Current?.Profile.IsAdmin != true) return 0;
        var products = Database.GetProducts(string.Empty, int.MaxValue);
        if (products.Count == 0)
        {
            SyncCacheStore.ClearProductsDirty();
            return 0;
        }

        var changed = 0;
        // Gateway accepts at most 1,000 products/request. Using the full supported page
        // reduces full-sheet read/write passes on Apps Script without changing data semantics.
        const int batchSize = 1000;
        for (var offset = 0; offset < products.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = products.Skip(offset).Take(batchSize).ToList();
            progress?.Report($"Đang gửi danh mục SKU {Math.Min(offset + batch.Count, products.Count):N0}/{products.Count:N0}...");
            var result = await GoogleGatewayV140.PushProductsAsync(batch, ct);
            changed += result.Changed;
        }
        SyncCacheStore.ClearProductsDirty();
        AppLog.Info("PRODUCT_CATALOG_PUSH", "Đã đồng bộ danh mục SKU lên Google Sheet.", new Dictionary<string, object?>
        {
            ["local_count"] = products.Count,
            ["changed"] = changed
        });
        return changed;
    }

    private static async Task<(int Applied, int Deleted, int Conflicts)> PullReportsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var cursor = SyncCacheStore.GetLong(ReportSeqKey);
        var applied = 0;
        var deleted = 0;
        var conflicts = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await GoogleGatewayV140.PullReportChangesAsync(cursor, 1000, ct);
            if (page.Changes.Count == 0) break;

            foreach (var change in page.Changes)
            {
                var result = SyncCacheStore.ApplyRemoteReport(change);
                if (result == RemoteApplyResult.Applied) applied++;
                else if (result == RemoteApplyResult.Deleted) deleted++;
                else if (result == RemoteApplyResult.Conflict) conflicts++;
                cursor = Math.Max(cursor, change.ChangeSeq);
            }
            SyncCacheStore.SetLong(ReportSeqKey, cursor);
            progress?.Report($"Đã nhận tới thay đổi phiếu #{cursor:N0}.");
            if (!page.HasMore) break;
        }
        Volatile.Write(ref _lastReportPullStamp, System.Diagnostics.Stopwatch.GetTimestamp());
        return (applied, deleted, conflicts);
    }

    private static async Task<(int Pulled, int ServerCount)> PullProductsAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var cursor = SyncCacheStore.GetLong(ProductSeqKey);
        var pulled = 0;
        var serverCount = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await GoogleGatewayV140.PullProductChangesAsync(cursor, 1000, ct);
            serverCount = page.ServerCount;
            if (page.Changes.Count == 0) break;
            foreach (var product in page.Changes)
            {
                SyncCacheStore.ApplyRemoteProduct(product);
                cursor = Math.Max(cursor, product.ChangeSeq);
                pulled++;
            }
            SyncCacheStore.SetLong(ProductSeqKey, cursor);
            progress?.Report($"Đã nhận tới thay đổi SKU #{cursor:N0}.");
            if (!page.HasMore) break;
        }
        return (pulled, serverCount);
    }
}
