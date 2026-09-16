using System.Net;

namespace PickfaceDamage1291;

internal static class OfficeNetworkRouteV1413
{
    private static readonly Uri GatewayUri = new("https://script.google.com/");
    private static readonly object Gate = new();
    private static int _initialized;
    private static CancellationTokenSource? _networkChangeDebounce;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        NetworkHttpClientFactory.NetworkChanged += OnNetworkChanged;
        ApplyCurrentNetworkPreference("startup");
    }

    public static bool IsExplicitSystemProxyActive()
    {
#pragma warning disable SYSLIB0014
        try
        {
            var proxy = WebRequest.DefaultWebProxy;
            if (proxy is null) return false;
            if (proxy.IsBypassed(GatewayUri)) return false;
            var proxyUri = proxy.GetProxy(GatewayUri);
            if (proxyUri is null) return false;
            return !SameEndpoint(proxyUri, GatewayUri);
        }
        catch
        {
            return false;
        }
#pragma warning restore SYSLIB0014
    }

    public static string GetProxyRouteLabel()
    {
#pragma warning disable SYSLIB0014
        try
        {
            var proxy = WebRequest.DefaultWebProxy;
            if (proxy is null || proxy.IsBypassed(GatewayUri)) return "direct";
            var proxyUri = proxy.GetProxy(GatewayUri);
            if (proxyUri is null || SameEndpoint(proxyUri, GatewayUri)) return "direct";
            return string.IsNullOrWhiteSpace(proxyUri.Host) ? "system_proxy" : $"system_proxy:{proxyUri.Host}:{proxyUri.Port}";
        }
        catch
        {
            return "unknown";
        }
#pragma warning restore SYSLIB0014
    }

    public static void ApplyCurrentNetworkPreference(string reason)
    {
        if (!IsExplicitSystemProxyActive())
        {
            AppLog.Info("OFFICE_ROUTE_DETECTED", "Không phát hiện proxy hệ thống bắt buộc; giữ chế độ direct-first và tự fallback.",
                new Dictionary<string, object?>
                {
                    ["mode"] = "direct_first",
                    ["reason"] = reason
                });
            return;
        }

        NetworkHttpClientFactory.PreferRtdbRelayForCurrentNetwork("windows_system_proxy_detected");
        AppLog.Info("OFFICE_ROUTE_DETECTED", "Phát hiện proxy hệ thống bắt buộc; Firebase RTDB sẽ đi qua Google Gateway thay vì thử RTDB trực tiếp.",
            new Dictionary<string, object?>
            {
                ["mode"] = "system_proxy_google_gateway",
                ["proxy_route"] = GetProxyRouteLabel(),
                ["reason"] = reason
            });
    }

    private static void OnNetworkChanged()
    {
        lock (Gate)
        {
            _networkChangeDebounce?.Cancel();
            _networkChangeDebounce?.Dispose();
            _networkChangeDebounce = new CancellationTokenSource();
            var token = _networkChangeDebounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(900, token);
                    if (!token.IsCancellationRequested)
                        ApplyCurrentNetworkPreference("windows_network_changed");
                }
                catch (OperationCanceledException)
                {
                    // A newer network change replaced this probe.
                }
                catch (Exception ex)
                {
                    AppLog.Warning("OFFICE_ROUTE_REEVALUATE_FAILED", ex.Message);
                }
            }, token);
        }
    }

    private static bool SameEndpoint(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;
}
