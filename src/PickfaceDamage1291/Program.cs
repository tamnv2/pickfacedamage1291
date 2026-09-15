using System.Diagnostics;

namespace PickfaceDamage1291;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            AppPaths.EnsureCreated();
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

        if (!FirebaseClient.IsConfigured)
        {
            MessageBox.Show(
                "Firebase client config chưa sẵn sàng trong build này.",
                "Thiếu cấu hình",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        RuntimeConfigService.InitializeAsync().GetAwaiter().GetResult();

        while (true)
        {
            if (AppSession.Current is null)
            {
                var resumed = AutoLoginService.TryResumeAsync().GetAwaiter().GetResult();
                if (!resumed)
                {
                    using var login = new LoginFormV130();
                    if (login.ShowDialog() != DialogResult.OK || AppSession.Current is null)
                        return;
                }
            }

            var main = new MainForm();
            SessionUiBinder.Bind(main);
            V130Runtime.Apply(main);
            V131Runtime.Apply(main);
            V132Runtime.Apply(main);
            UiRuntimeFixes.Attach(main);
            V133Runtime.Apply(main);
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
            return;
        }
    }

    private static void HandleStorageMoveAndRestart(string targetRoot)
    {
        var oldRoot = AppPaths.Root;
        var result = LocalStorageManager.MoveTo(targetRoot);
        if (!result.Success)
        {
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
        var session = AppSession.Current;
        var manager = AppSession.OperatorManager;
        try
        {
            if (manager is not null && session?.OfflineMode != true)
                manager.ReleaseAsync(normalLogout: true).GetAwaiter().GetResult();

            if (session is not null)
                FirebaseClient.AppendAuditAsync(session, "LOGOUT", new { reason }).GetAwaiter().GetResult();
        }
        catch
        {
            // Closing the app must not be blocked by a transient network failure.
        }
        finally
        {
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
