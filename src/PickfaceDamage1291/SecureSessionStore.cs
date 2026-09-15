using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal static class SecureSessionStore
{
    private static string SessionFile => Path.Combine(AppPaths.Data, "firebase_session.bin");
    private static string DeviceFile => Path.Combine(AppPaths.Data, "device_id.txt");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PickfaceDamage1291.FirebaseSession.v1");

    public static string GetOrCreateDeviceId()
    {
        AppPaths.EnsureCreated();
        try
        {
            if (File.Exists(DeviceFile))
            {
                var existing = File.ReadAllText(DeviceFile).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }
        }
        catch
        {
            // Fall through and recreate a local device id.
        }

        var value = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        File.WriteAllText(DeviceFile, value, Encoding.UTF8);
        return value;
    }

    public static void Save(FirebaseSession session)
    {
        if (!LoginPreferencesStore.ShouldPersistSession)
        {
            Clear();
            return;
        }

        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SessionFile, protectedBytes);
        CryptographicOperations.ZeroMemory(plain);
    }

    public static FirebaseSession? Load()
    {
        try
        {
            if (!File.Exists(SessionFile)) return null;
            var protectedBytes = File.ReadAllBytes(SessionFile);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<FirebaseSession>(plain, JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SessionFile)) File.Delete(SessionFile);
        }
        catch
        {
            // A failed local cleanup must not crash the application.
        }
    }
}
