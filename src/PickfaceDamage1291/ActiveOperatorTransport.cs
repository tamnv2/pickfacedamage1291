namespace PickfaceDamage1291;

/// <summary>
/// Keeps direct Firebase RTDB as the fast primary path, but temporarily switches the small
/// active_operator coordination traffic to the Google Apps Script bridge after a direct failure.
/// This is specifically for corporate networks that allow Google services but block/intercept
/// the regional *.firebasedatabase.app endpoint.
/// </summary>
internal static class ActiveOperatorTransport
{
    private static long _bridgePreferredUntilUtcTicks;
    private static readonly TimeSpan BridgePreference = TimeSpan.FromMinutes(10);

    public static bool IsBridgePreferred => DateTime.UtcNow.Ticks < Interlocked.Read(ref _bridgePreferredUntilUtcTicks);

    public static async Task<ActiveOperatorSnapshot> GetSnapshotAsync(FirebaseSession session, CancellationToken ct = default)
    {
        if (IsBridgePreferred)
        {
            try { return await OfficeNetworkFirebaseBridge.GetSnapshotAsync(session, ct); }
            catch (Exception bridgeEx) when (!ct.IsCancellationRequested)
            {
                AppLog.Warning("OFFICE_FIREBASE_BRIDGE_READ_FAILED", NetworkHttpClientFactory.Friendly(bridgeEx, "Google Gateway active_operator"));
                ClearBridgePreference();
            }
        }

        try
        {
            return await FirebaseClient.GetActiveOperatorSnapshotAsync(session, ct);
        }
        catch (Exception directEx) when (!ct.IsCancellationRequested)
        {
            AppLog.Warning("DIRECT_FIREBASE_OPERATOR_READ_FAILED", NetworkHttpClientFactory.Friendly(directEx, "Firebase active_operator"));
            try
            {
                var result = await OfficeNetworkFirebaseBridge.GetSnapshotAsync(session, ct);
                PreferBridge();
                return result;
            }
            catch (Exception bridgeEx)
            {
                throw new AggregateException("Không đọc được active_operator qua Firebase trực tiếp hoặc Google Gateway dự phòng.", directEx, bridgeEx);
            }
        }
    }

    public static async Task<long> GetServerNowMsAsync(FirebaseSession session, CancellationToken ct = default)
    {
        if (IsBridgePreferred)
        {
            try { return await OfficeNetworkFirebaseBridge.GetServerNowMsAsync(session, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                AppLog.Warning("OFFICE_FIREBASE_BRIDGE_TIME_FAILED", NetworkHttpClientFactory.Friendly(ex, "Google Gateway server time"));
            }
        }
        return await FirebaseClient.GetServerNowMsAsync(session, ct);
    }

    public static async Task<bool> PutAsync(FirebaseSession session, ActiveOperatorRecord value, string etag, CancellationToken ct = default)
    {
        if (IsBridgePreferred)
        {
            try { return await OfficeNetworkFirebaseBridge.PutAsync(session, value, etag, ct); }
            catch (Exception bridgeEx) when (!ct.IsCancellationRequested)
            {
                AppLog.Warning("OFFICE_FIREBASE_BRIDGE_WRITE_FAILED", NetworkHttpClientFactory.Friendly(bridgeEx, "Google Gateway active_operator PUT"));
                ClearBridgePreference();
            }
        }

        try
        {
            return await FirebaseClient.PutActiveOperatorAsync(session, value, etag, ct);
        }
        catch (Exception directEx) when (!ct.IsCancellationRequested)
        {
            AppLog.Warning("DIRECT_FIREBASE_OPERATOR_WRITE_FAILED", NetworkHttpClientFactory.Friendly(directEx, "Firebase active_operator PUT"));
            try
            {
                var result = await OfficeNetworkFirebaseBridge.PutAsync(session, value, etag, ct);
                PreferBridge();
                return result;
            }
            catch (Exception bridgeEx)
            {
                throw new AggregateException("Không ghi được active_operator qua Firebase trực tiếp hoặc Google Gateway dự phòng.", directEx, bridgeEx);
            }
        }
    }

    public static async Task<bool> DeleteAsync(FirebaseSession session, string etag, CancellationToken ct = default)
    {
        if (IsBridgePreferred)
        {
            try { return await OfficeNetworkFirebaseBridge.DeleteAsync(session, etag, ct); }
            catch (Exception bridgeEx) when (!ct.IsCancellationRequested)
            {
                AppLog.Warning("OFFICE_FIREBASE_BRIDGE_DELETE_FAILED", NetworkHttpClientFactory.Friendly(bridgeEx, "Google Gateway active_operator DELETE"));
                ClearBridgePreference();
            }
        }

        try
        {
            return await FirebaseClient.DeleteActiveOperatorAsync(session, etag, ct);
        }
        catch (Exception directEx) when (!ct.IsCancellationRequested)
        {
            AppLog.Warning("DIRECT_FIREBASE_OPERATOR_DELETE_FAILED", NetworkHttpClientFactory.Friendly(directEx, "Firebase active_operator DELETE"));
            try
            {
                var result = await OfficeNetworkFirebaseBridge.DeleteAsync(session, etag, ct);
                PreferBridge();
                return result;
            }
            catch (Exception bridgeEx)
            {
                throw new AggregateException("Không xoá được active_operator qua Firebase trực tiếp hoặc Google Gateway dự phòng.", directEx, bridgeEx);
            }
        }
    }

    private static void PreferBridge()
    {
        Interlocked.Exchange(ref _bridgePreferredUntilUtcTicks, DateTime.UtcNow.Add(BridgePreference).Ticks);
        AppLog.Info("OFFICE_FIREBASE_BRIDGE_SELECTED", "Tạm ưu tiên Google Gateway cho active_operator trong 10 phút sau lỗi Firebase trực tiếp.");
    }

    private static void ClearBridgePreference() => Interlocked.Exchange(ref _bridgePreferredUntilUtcTicks, 0);
}
