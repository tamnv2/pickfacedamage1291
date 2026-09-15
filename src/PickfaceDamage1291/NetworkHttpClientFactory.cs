using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PickfaceDamage1291;

internal static class NetworkHttpClientFactory
{
    private static long _lastNetworkChangeUtcTicks;

    public static event Action? NetworkChanged;

    static NetworkHttpClientFactory()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => MarkNetworkChanged("address");
        NetworkChange.NetworkAvailabilityChanged += (_, e) => MarkNetworkChanged(e.IsAvailable ? "available" : "unavailable");
    }

    public static HttpClient Create(TimeSpan timeout, string userAgent)
    {
        var proxy = WebRequest.DefaultWebProxy;
        if (proxy is not null)
            proxy.Credentials = CredentialCache.DefaultCredentials;

        // SocketsHttpHandler keeps HttpClient reuse fast, while short connection lifetimes
        // prevent a laptop from being pinned to stale DNS/proxy/TCP state after Wi-Fi/LAN changes.
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = proxy,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    public static bool NetworkChangedRecently(TimeSpan window)
    {
        var ticks = Interlocked.Read(ref _lastNetworkChangeUtcTicks);
        if (ticks <= 0) return false;
        var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    public static async Task<T> RetryAsync<T>(Func<Task<T>> action, int attempts = 3, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < attempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < attempts)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }
        throw last ?? new HttpRequestException("Kết nối mạng không thành công.");
    }

    public static string Friendly(Exception ex, string target)
    {
        var socket = FindInner<SocketException>(ex);
        if (socket is not null && socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData)
            return $"Không phân giải được địa chỉ {target} từ ứng dụng. Trình duyệt có thể dùng DNS/proxy khác với ứng dụng Windows. Ứng dụng đã thử proxy hệ thống nhưng kết nối vẫn chưa thành công.";

        if (ex is TaskCanceledException)
            return $"Kết nối tới {target} quá thời gian chờ.";

        if (ex is HttpRequestException)
            return $"Không kết nối được tới {target}: {ex.Message}";

        return ex.Message;
    }

    private static void MarkNetworkChanged(string state)
    {
        Interlocked.Exchange(ref _lastNetworkChangeUtcTicks, DateTime.UtcNow.Ticks);
        try
        {
            AppLog.Info("NETWORK_CHANGED", "Windows báo thay đổi kết nối mạng.", new Dictionary<string, object?>
            {
                ["state"] = state
            });
        }
        catch
        {
            // Network notifications can arrive before logging is initialized.
        }

        try { NetworkChanged?.Invoke(); } catch { }
    }

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is T typed) return typed;
        return null;
    }
}
