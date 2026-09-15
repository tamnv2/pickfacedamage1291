namespace PickfaceDamage1291;

internal static class AutoLoginService
{
    public static async Task<bool> TryResumeAsync(CancellationToken ct = default)
    {
        var preferences = LoginPreferencesStore.Load();
        if (!preferences.KeepSignedIn) return false;

        var cached = SecureSessionStore.Load();
        if (cached is null) return false;

        try
        {
            cached.OfflineMode = false;
            await FirebaseClient.EnsureFreshAsync(cached, ct);
            var profile = await FirebaseClient.GetProfileAsync(cached.Uid, cached.IdToken, ct);
            if (profile?.Active != true) throw new InvalidOperationException("Tài khoản không còn hoạt động.");
            cached.Profile = profile;
            cached.Email = profile.Email;
            return await SessionBootstrap.PrepareOnlineAsync(cached, null, ct);
        }
        catch
        {
            if (!preferences.AllowOfflineLogin) return false;
            try { return SessionBootstrap.PrepareOffline(cached); }
            catch { return false; }
        }
    }
}
