using System.Text.Json;

namespace PickfaceDamage1291;

internal static class AppPaths
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string DefaultRoot = Path.Combine(LocalAppData, "PickfaceDamage1291");
    public static readonly string StorageLocatorFile = Path.Combine(LocalAppData, "PickfaceDamage1291.storage.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string? _root;
    private static bool _customRoot;

    public static string Root => _root ??= ResolveRoot();
    public static bool IsCustomRoot { get { _ = Root; return _customRoot; } }
    public static string Data => Path.Combine(Root, "data");
    public static string Images => Path.Combine(Root, "images");
    public static string PendingImages => Path.Combine(Images, "pending");
    public static string Backup => Path.Combine(Root, "backup");
    public static string Logs => Path.Combine(Root, "logs");
    public static string DatabaseFile => Path.Combine(Data, "pickface.db");
    public static string SettingsFile => Path.Combine(Data, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Images);
        Directory.CreateDirectory(PendingImages);
        Directory.CreateDirectory(Backup);
        Directory.CreateDirectory(Logs);
    }

    public static string GetDraftImageFolder(string reportId)
    {
        var path = Path.Combine(PendingImages, reportId);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void BackupDatabase()
    {
        if (!File.Exists(DatabaseFile)) return;

        try
        {
            var target = Path.Combine(Backup, $"pickface_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(DatabaseFile, target, overwrite: false);
            foreach (var old in new DirectoryInfo(Backup)
                         .GetFiles("pickface_*.db")
                         .OrderByDescending(x => x.CreationTimeUtc)
                         .Skip(10))
            {
                old.Delete();
            }
        }
        catch
        {
            // Backup failure must never prevent the application from starting.
        }
    }

    public static void SaveConfiguredRoot(string root)
    {
        var full = Path.GetFullPath(root.Trim());
        var directory = Path.GetDirectoryName(StorageLocatorFile)!;
        Directory.CreateDirectory(directory);
        var temp = StorageLocatorFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new StorageLocator { Root = full }, JsonOptions));
        File.Move(temp, StorageLocatorFile, true);
    }

    private static string ResolveRoot()
    {
        try
        {
            if (File.Exists(StorageLocatorFile))
            {
                var locator = JsonSerializer.Deserialize<StorageLocator>(File.ReadAllText(StorageLocatorFile), JsonOptions);
                if (!string.IsNullOrWhiteSpace(locator?.Root))
                {
                    var configured = Path.GetFullPath(locator.Root.Trim());
                    if (!Directory.Exists(configured))
                        throw new DirectoryNotFoundException($"Không tìm thấy nơi lưu dữ liệu đã cấu hình: {configured}");
                    _customRoot = !string.Equals(configured.TrimEnd(Path.DirectorySeparatorChar), DefaultRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
                    return configured;
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            throw;
        }
        catch
        {
            // A malformed locator falls back to the original default location.
        }

        _customRoot = false;
        return DefaultRoot;
    }

    private sealed class StorageLocator
    {
        public string Root { get; set; } = string.Empty;
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static GoogleSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new GoogleSettings();
            return JsonSerializer.Deserialize<GoogleSettings>(File.ReadAllText(AppPaths.SettingsFile))
                   ?? new GoogleSettings();
        }
        catch
        {
            return new GoogleSettings();
        }
    }

    public static void Save(GoogleSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
