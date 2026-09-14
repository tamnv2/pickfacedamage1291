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
        Application.Run(new MainForm());
    }
}
