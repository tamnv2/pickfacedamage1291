namespace PickfaceDamage1291;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppPaths.EnsureCreated();
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

        // Runtime metadata is public configuration and is needed before username login.
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
            UiRuntimeFixes.Attach(main);
            Application.Run(main);

            var logoutRequested = V130Runtime.ConsumeLogoutRequested();
            CleanupSession(main, logoutRequested ? "USER_LOGOUT" : "APP_CLOSED");

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
