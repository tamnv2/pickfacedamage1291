using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed class LoginPreferences
{
    public bool AllowOfflineLogin { get; set; } = true;
    public bool KeepSignedIn { get; set; }
}

internal static class LoginPreferencesStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.Data, "login_preferences.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LoginPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new LoginPreferences();
            return JsonSerializer.Deserialize<LoginPreferences>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new LoginPreferences();
        }
        catch
        {
            return new LoginPreferences();
        }
    }

    public static void Save(LoginPreferences value)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(value, JsonOptions));
    }

    public static bool ShouldPersistSession
    {
        get
        {
            var p = Load();
            return p.AllowOfflineLogin || p.KeepSignedIn;
        }
    }
}
