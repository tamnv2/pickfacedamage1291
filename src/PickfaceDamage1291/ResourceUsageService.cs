using System.Diagnostics;

namespace PickfaceDamage1291;

internal sealed record ResourceUsageSnapshot(
    long TotalBytes,
    long DatabaseBytes,
    long DataAuxBytes,
    long ImageBytes,
    long LogBytes,
    long BackupBytes,
    long OtherBytes,
    long RamWorkingSetBytes);

internal static class ResourceUsageService
{
    public static ResourceUsageSnapshot GetSnapshot()
    {
        AppPaths.EnsureCreated();

        var database = FileLength(AppPaths.DatabaseFile);
        var dataTotal = DirectorySize(AppPaths.Data);
        var dataAux = Math.Max(0, dataTotal - database);
        var images = DirectorySize(AppPaths.Images);
        var logs = DirectorySize(AppPaths.Logs);
        var backup = DirectorySize(AppPaths.Backup);
        var total = DirectorySize(AppPaths.Root);
        var known = dataTotal + images + logs + backup;
        var other = Math.Max(0, total - known);

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        return new ResourceUsageSnapshot(
            total,
            database,
            dataAux,
            images,
            logs,
            backup,
            other,
            Math.Max(0, process.WorkingSet64));
    }

    public static string FormatBytes(long value)
    {
        if (value < 1024) return $"{value:N0} B";
        var units = new[] { "KB", "MB", "GB", "TB" };
        var size = value / 1024d;
        var index = 0;
        while (size >= 1024d && index < units.Length - 1)
        {
            size /= 1024d;
            index++;
        }
        return $"{size:N2} {units[index]}";
    }

    private static long FileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }

                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        var info = new DirectoryInfo(directory);
                        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(directory);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return total;
    }
}
