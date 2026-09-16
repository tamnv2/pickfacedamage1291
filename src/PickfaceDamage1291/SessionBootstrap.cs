namespace PickfaceDamage1291;

internal static class SessionBootstrap
{
    public static async Task<bool> PrepareOnlineAsync(FirebaseSession session, IWin32Window? owner = null, CancellationToken ct = default)
    {
        session.OfflineMode = false;
        var profile = await FirebaseClient.GetProfileAsync(session.Uid, session.IdToken, ct)
                      ?? throw new InvalidOperationException("Tài khoản chưa được cấp hồ sơ sử dụng ứng dụng. Liên hệ ADMIN.");
        if (!profile.Active) throw new InvalidOperationException("Tài khoản đã bị khóa hoặc ngừng hoạt động.");
        session.Profile = profile;
        session.Email = profile.Email;

        AppLog.Info("CONNECTIVITY_ONLINE_READY", "Đã xác thực online và chọn đường kết nối cho mạng hiện tại.", new Dictionary<string, object?>
        {
            ["firebase_rtdb_route"] = NetworkHttpClientFactory.IsRtdbRelayPreferred ? "google_relay" : "direct_firebase",
            ["github_runtime_config_reachable"] = RuntimeConfigService.GitHubRuntimeConfigReachable,
            ["google_gateway"] = RuntimeConfigService.IsGoogleGatewayConfigured
        });

        OperatorLeaseManager? manager = null;
        if (!profile.IsAdmin && profile.HasPermission("damage_entry"))
        {
            manager = new OperatorLeaseManager(session);
            var result = await manager.AcquireAsync((existing, online) =>
            {
                var message = online
                    ? $"Tài khoản {existing.Username} đang ONLINE trên thiết bị khác.\n\nNếu tiếp tục, phiên đó sẽ bị ngắt ngay và quyền nhập chuyển sang tài khoản {profile.Username}.\n\nTiếp tục?"
                    : $"Phiên {existing.Username} đã hết lease/không còn hoạt động.\n\nNhận quyền nhập cho tài khoản {profile.Username}?";
                return MessageBox.Show(owner, message, "Chuyển quyền nhập", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            }, ct);
            if (!result.Success)
            {
                await manager.DisposeAsync();
                MessageBox.Show(owner, result.Message, "Chưa thể vào vùng nhập liệu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
        }

        AppSession.Current = session;
        AppSession.OperatorManager = manager;
        SecureSessionStore.Save(session);

        // Login audit is committed to the durable local outbox only. It is sent by the normal
        // cloud-sync path after the main window is usable, avoiding both login delay and a race
        // where two concurrent transports could append the same event at once.
        try
        {
            DurableAuditQueue.Enqueue(session, "LOGIN_SUCCESS", new { role = profile.Role }, manager?.SessionId);
        }
        catch (Exception ex)
        {
            AppLog.Exception("LOGIN_AUDIT_QUEUE_FAILED", ex);
            // Audit transport/storage must never invalidate an otherwise valid login.
        }
        return true;
    }

    public static bool PrepareOffline(FirebaseSession cached, IWin32Window? owner = null)
    {
        var preferences = LoginPreferencesStore.Load();
        if (!preferences.AllowOfflineLogin)
        {
            MessageBox.Show(owner, "Tùy chọn Cho phép đăng nhập khi offline đang tắt.", "Không thể đăng nhập offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        cached.OfflineMode = true;
        if (cached.Profile.IsAdmin || !cached.Profile.HasPermission("damage_entry"))
        {
            AppSession.Current = cached;
            AppSession.OperatorManager = null;
            try { DurableAuditQueue.Enqueue(cached, "LOGIN_OFFLINE", new { role = cached.Profile.Role }); } catch { }
            return true;
        }

        var manager = new OperatorLeaseManager(cached, reuseCachedLease: true);
        if (!manager.IsAcquired)
        {
            manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            MessageBox.Show(owner, "Lease offline không còn hiệu lực. Cần kết nối Internet để đăng nhập lại.", "Không thể đăng nhập offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        AppSession.Current = cached;
        AppSession.OperatorManager = manager;
        manager.StartOfflineMonitoring();
        SecureSessionStore.Save(cached);
        try { DurableAuditQueue.Enqueue(cached, "LOGIN_OFFLINE", new { role = cached.Profile.Role }, manager.SessionId); } catch { }
        return true;
    }
}
