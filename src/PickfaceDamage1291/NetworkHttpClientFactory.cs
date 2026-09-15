using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace PickfaceDamage1291;

internal static class NetworkHttpClientFactory
{
    public static HttpClient Create(TimeSpan timeout, string userAgent)
    {
        var proxy = WebRequest.DefaultWebProxy;
        if (proxy is not null)
            proxy.Credentials = CredentialCache.DefaultCredentials;

        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
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

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is T typed) return typed;
        return null;
    }
}
