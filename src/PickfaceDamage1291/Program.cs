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

        using (var login = new LoginForm())
        {
            if (login.ShowDialog() != DialogResult.OK || AppSession.Current is null)
                return;
        }

        if (AppSession.Current?.OfflineMode != true)
            RuntimeConfigService.InitializeAsync().GetAwaiter().GetResult();

        var main = new MainForm();
        SessionUiBinder.Bind(main);
        UiRuntimeFixes.Attach(main);
        Application.Run(main);

        var session = AppSession.Current;
        var manager = AppSession.OperatorManager;
        try
        {
            if (manager is not null && session?.OfflineMode != true)
                manager.ReleaseAsync(normalLogout: true).GetAwaiter().GetResult();
            else if (manager is null && session is not null && !session.OfflineMode)
                FirebaseClient.AppendAuditAsync(session, "LOGOUT", new { reason = "APP_CLOSED" }).GetAwaiter().GetResult();
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
