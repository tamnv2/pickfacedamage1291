using System.Collections.Concurrent;

namespace PickfaceDamage1291;

internal sealed record BackgroundSyncState(
    string? ReportId,
    string? Sku,
    int Percent,
    string Message,
    int QueueCount,
    bool IsBusy,
    bool Completed,
    bool Failed);

internal static class BackgroundSyncCoordinator
{
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly HashSet<string> Tracked = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim Signal = new(0);
    private static Task? _worker;
    private static int _busy;

    public static event Action<BackgroundSyncState>? StateChanged;

    public static bool IsBusy => Volatile.Read(ref _busy) == 1;

    public static int QueueCount
    {
        get
        {
            lock (Gate) return Tracked.Count;
        }
    }

    public static bool Enqueue(string reportId)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return false;
        lock (Gate)
        {
            if (!Tracked.Add(reportId)) return false;
            Queue.Enqueue(reportId);
            EnsureWorkerLocked();
        }
        Signal.Release();
        Publish(new BackgroundSyncState(reportId, null, 0, "Đã xếp vào hàng đợi đồng bộ.", QueueCount, IsBusy, false, false));
        return true;
    }

    public static int EnqueuePending()
    {
        var count = 0;
        foreach (var report in Database.GetPendingReports().OrderBy(x => x.CreatedAt))
            if (Enqueue(report.ReportId)) count++;
        return count;
    }

    public static int EnqueueAllLocal()
    {
        var count = 0;
        foreach (var report in Database.GetReports(int.MaxValue).OrderBy(x => x.CreatedAt))
            if (Enqueue(report.ReportId)) count++;
        return count;
    }

    private static void EnsureWorkerLocked()
    {
        if (_worker is { IsCompleted: false }) return;
        _worker = Task.Run(WorkerLoopAsync);
    }

    private static async Task WorkerLoopAsync()
    {
        while (true)
        {
            await Signal.WaitAsync();
            if (!Queue.TryDequeue(out var reportId)) continue;

            var report = Database.GetReportById(reportId);
            if (report is null)
            {
                RemoveTracked(reportId);
                continue;
            }

            if (!GoogleService.IsConnected())
            {
                Database.SetReportStatus(reportId, "OFFLINE_PENDING");
                Publish(new BackgroundSyncState(reportId, report.Sku, 0, "Chờ kết nối để đồng bộ.", Math.Max(0, QueueCount - 1), false, false, false));
                RemoveTracked(reportId);
                continue;
            }

            Interlocked.Exchange(ref _busy, 1);
            try
            {
                var progress = new InlineProgress<int>(value =>
                    Publish(new BackgroundSyncState(
                        reportId,
                        report.Sku,
                        Math.Clamp(value, 0, 100),
                        $"Đang đồng bộ SKU {report.Sku}...",
                        QueueCount,
                        true,
                        false,
                        false)));

                await GoogleService.SyncReportAsync(report, progress);
                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"Đã đồng bộ SKU {report.Sku}.", Math.Max(0, QueueCount - 1), true, true, false));
                NotificationCenter.Show(null, $"Đã đồng bộ SKU {report.Sku} lên Google.", "Đồng bộ hoàn tất", MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Publish(new BackgroundSyncState(reportId, report.Sku, 100, $"Đồng bộ SKU {report.Sku} chưa thành công.", Math.Max(0, QueueCount - 1), true, true, true));
                NotificationCenter.Show(null, $"Phiếu SKU {report.Sku} vẫn được giữ trên máy. {ex.Message}", "Chưa đồng bộ được", MessageBoxIcon.Warning);
            }
            finally
            {
                RemoveTracked(reportId);
                Interlocked.Exchange(ref _busy, 0);
                if (QueueCount == 0)
                    Publish(new BackgroundSyncState(null, null, 0, "Không có phiếu đang đồng bộ.", 0, false, true, false));
            }
        }
    }

    private static void RemoveTracked(string reportId)
    {
        lock (Gate) Tracked.Remove(reportId);
    }

    private static void Publish(BackgroundSyncState state)
    {
        try { StateChanged?.Invoke(state); } catch { }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
