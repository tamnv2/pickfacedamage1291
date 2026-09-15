namespace PickfaceDamage1291;

/// <summary>
/// Public, non-secret identifiers only. Never place passwords, OAuth client secrets,
/// refresh tokens, service-account keys or other credentials in this file.
/// </summary>
internal static class CloudConfig
{
    public const string GoogleCloudProjectId = "pickface-damage-1291";

    public const string DriveRootFolderId = "16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC";
    public const string DriveImageFolderId = "1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf";
    public const string DriveLogsFolderId = "1Z19VgAAmCN1u7z_xSSztx9IVuq3kFSlK";
    public const string DamageSpreadsheetId = "1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4";

    // Desktop OAuth Client ID is public metadata. No client secret is used by the desktop app.
    public const string GoogleOAuthClientId = "78092201115-vh15d3ijur18v0qaga7327pv8m786ooi.apps.googleusercontent.com";

    // Firebase client configuration is public metadata. Security comes from Authentication
    // and Realtime Database Security Rules, not from hiding these values.
    public const string FirebaseApiKey = "AIzaSyD6aKmuZSbcl5HAqnb7fNv_RaD6v4gQABU";
    public const string FirebaseDatabaseUrl = "https://pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app";

    public const string GitHubRepository = "tamnv2/pickfacedamage1291";
    public const string GitHubLatestReleaseApi = "https://api.github.com/repos/tamnv2/pickfacedamage1291/releases/latest";
    public const string GitHubReleasesPage = "https://github.com/tamnv2/pickfacedamage1291/releases";
}
