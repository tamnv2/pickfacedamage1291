using System.Diagnostics;

namespace PickfaceDamage1291;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var startup = Stopwatch.StartNew();
        ApplicationConfiguration.Initialize();
        try
        {
            AppPaths.EnsureCreated();
            AppLog.Initialize();
            AttachUnhandledLogging();
            AppLog.Info("APP_START", "Ứng dụng khởi động.");
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                "Không mở được nơi lưu dữ liệu đã cấu hình. Ứng dụng dừng để tránh tạo dữ liệu mới sai vị trí.\n\n" + ex.Message,
                "Không mở được dữ liệu local",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        AppPaths.BackupDatabase();
        Database.Initialize();
        SyncCacheStore.Initialize();

        if (!FirebaseClient.IsConfigured)
        {
            AppLog.Error("FIREBASE_CONFIG_MISSING", "Firebase client config chưa sẵn sàng.");
            MessageBox.Show(
                "Firebase client config chưa sẵn sàng trong build này.",
                "Thiếu cấu hình",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        RuntimeConfigService.InitializeAsync().GetAwaiter().GetResult();
        AppLog.Info("STARTUP_LOCAL_READY", "Khởi tạo local và tuyến mạng cơ bản đã sẵn sàng.", new Dictionary<string, object?>
        {
            ["elapsed_ms"] = startup.ElapsedMilliseconds
        });

        while (true)
        {
            if (AppSession.Current is null)
            {
                var resumed = AutoLoginService.TryResumeAsync().GetAwaiter().GetResult();
                if (!resumed)
                {
                    using var login = new LoginFormV130();
                    if (login.ShowDialog() != DialogResult.OK || AppSession.Current is null)
                    {
                        AppLog.Info("APP_EXIT_LOGIN", "Ứng dụng đóng tại màn hình đăng nhập.");
                        return;
                    }
                }
            }

            if (AppSession.Current is { } active)
            {
                AppLog.BindSession(active.Profile.Username);
                AppLog.Info("SESSION_ACTIVE", "Phiên ứng dụng đã sẵn sàng.", new Dictionary<string, object?>
                {
                    ["username"] = active.Profile.Username,
                    ["role"] = active.Profile.Role,
                    ["offline"] = active.OfflineMode,
                    ["startup_elapsed_ms"] = startup.ElapsedMilliseconds
                });
            }

            var main = new MainForm();
            SessionUiBinder.Bind(main);
            V130Runtime.Apply(main);
            V131Runtime.Apply(main);
            V132Runtime.Apply(main);
            UiRuntimeFixes.Attach(main);
            V133Runtime.Apply(main);
            V140Runtime.Apply(main);
            V142Runtime.Apply(main);
            V142RuntimeCorrections.Apply(main);
            V146Runtime.Apply(main);
            V147Runtime.Apply(main);
            V148Runtime.Apply(main);
            V1410Runtime.Apply(main);
            V1414Runtime.Apply(main);
            V1416Runtime.Apply(main);
            V1419Runtime.Apply(main);
            V1420Runtime.Apply(main);
            V1427Runtime.Apply(main);
            V1428Runtime.Apply(main);
            Application.Run(main);

            var storageMoveTarget = V132Runtime.ConsumeStorageMoveTarget();
            var logoutRequested = V130Runtime.ConsumeLogoutRequested();
            CleanupSession(main, storageMoveTarget is not null ? "STORAGE_MOVE" : logoutRequested ? "USER_LOGOUT" : "APP_CLOSED");

            if (storageMoveTarget is not null)
            {
                if (!LoginPreferencesStore.ShouldPersistSession) SecureSessionStore.Clear();
                HandleStorageMoveAndRestart(storageMoveTarget);
                return;
            }

            if (logoutRequested)
            {
                SecureSessionStore.Clear();
                continue;
            }

            if (!LoginPreferencesStore.ShouldPersistSession)
                SecureSessionStore.Clear();
            AppLog.Info("APP_EXIT", "Ứng dụng đóng bình thường.");
            return;
        }
    }

    private static void AttachUnhandledLogging()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.CaptureCrash("UI_UNHANDLED_EXCEPTION", e.Exception, waitForUpload: false);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppLog.CaptureCrash("DOMAIN_UNHANDLED_EXCEPTION", ex, waitForUpload: e.IsTerminating);
            else
                AppLog.CaptureCrash("DOMAIN_UNHANDLED_EXCEPTION", null, Convert.ToString(e.ExceptionObject) ?? "Unknown unhandled exception", e.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Exception("TASK_UNOBSERVED_EXCEPTION", e.Exception);
            e.SetObserved();
        };
    }

    private static void HandleStorageMoveAndRestart(string targetRoot)
    {
        var oldRoot = AppPaths.Root;
        AppLog.Info("STORAGE_MOVE_START", "Bắt đầu chuyển nơi lưu dữ liệu local.");
        var result = LocalStorageManager.MoveTo(targetRoot);
        if (!result.Success)
        {
            AppLog.Warning("STORAGE_MOVE_FAILED", result.Message);
            var open = System.Windows.Forms.MessageBox.Show(
                result.Message + "\n\nMở thư mục dữ liệu hiện tại để tự xử lý thủ công?",
                "Không thể chuyển nơi lưu trữ",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (open == DialogResult.Yes && Directory.Exists(oldRoot))
                Process.Start(new ProcessStartInfo("explorer.exe", oldRoot) { UseShellExecute = true });
        }
        else if (!string.IsNullOrWhiteSpace(result.Warning))
        {
            AppLog.Warning("STORAGE_MOVE_WARNING", result.Warning);
            var open = System.Windows.Forms.MessageBox.Show(
                result.Warning + "\n\nMở thư mục dữ liệu cũ để kiểm tra/xóa thủ công?",
                "Đã chuyển dữ liệu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (open == DialogResult.Yes && Directory.Exists(oldRoot))
                Process.Start(new ProcessStartInfo("explorer.exe", oldRoot) { UseShellExecute = true });
        }

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    private static void CleanupSession(MainForm main, string reason)
    {
        var cleanup = Stopwatch.StartNew();
        var session = AppSession.Current;
        var manager = AppSession.OperatorManager;
        try
        {
            if (manager is not null && session?.OfflineMode != true)
            {
                manager.ReleaseAsync(normalLogout: false).GetAwaiter().GetResult();
                if (session is not null)
                {
                    try
                    {
                        DurableAuditQueue.Enqueue(session, "OPERATOR_RELEASED", new { session = manager.SessionId }, manager.SessionId);
                    }
                    catch { }
                }
            }

            if (session is not null)
            {
                try
                {
                    DurableAuditQueue.Enqueue(session, "LOGOUT", new { reason });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception("SESSION_CLEANUP_WARNING", ex, new Dictionary<string, object?> { ["reason"] = reason });
        }
        finally
        {
            AppLog.Info("SESSION_CLOSED", "Đã đóng phiên ứng dụng.", new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["elapsed_ms"] = cleanup.ElapsedMilliseconds,
                ["remote_logout_audit_wait"] = false
            });
            if (manager is not null)
            {
                try { manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            }
            main.Dispose();
            AppSession.OperatorManager = null;
            AppSession.Current = null;
        }
    }
}
