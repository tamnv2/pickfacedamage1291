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
    public const string DamageSpreadsheetId = "1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4";

    // Desktop OAuth Client ID is public metadata and may be embedded after OWNER supplies it.
    // Client secret is intentionally not used by the desktop app.
    public const string GoogleOAuthClientId = "";

    // Firebase public client configuration. Fill only after Firebase is added to the
    // approved Google Cloud project above. Do not put service-account credentials here.
    public const string FirebaseApiKey = "";
    public const string FirebaseDatabaseUrl = "";

    public const string GitHubRepository = "tamnv2/pickfacedamage1291";
    public const string GitHubLatestReleaseApi = "https://api.github.com/repos/tamnv2/pickfacedamage1291/releases/latest";
    public const string GitHubReleasesPage = "https://github.com/tamnv2/pickfacedamage1291/releases";
}
