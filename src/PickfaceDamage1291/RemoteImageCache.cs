namespace PickfaceDamage1291;

internal static class RemoteImageCache
{
    public static async Task<DamageImage> EnsureLocalAsync(DamageImage image, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(image.LocalPath) && File.Exists(image.LocalPath)) return image;
        if (string.IsNullOrWhiteSpace(image.DriveFileId)) return image;

        var downloaded = await GoogleGatewayV140.GetImageAsync(image.DriveFileId, ct);
        var folder = AppPaths.GetDraftImageFolder(image.ReportId);
        var extension = ExtensionFor(downloaded.MimeType, downloaded.Name);
        var target = Path.Combine(folder, $"remote_{image.Sequence:00}_{image.DriveFileId[..Math.Min(12, image.DriveFileId.Length)]}{extension}");
        await File.WriteAllBytesAsync(target, downloaded.Bytes, ct);
        SyncCacheStore.SetImageLocalPath(image.ReportId, image.Sequence, target);
        try
        {
            var hash = ImageHashService.Sha256File(target);
            SyncCacheStore.SetImageHash(image.ReportId, image.Sequence, hash);
        }
        catch { }
        return image with { LocalPath = target };
    }

    private static string ExtensionFor(string mimeType, string name)
    {
        var ext = Path.GetExtension(name);
        if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 8) return ext;
        return mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            _ => ".jpg"
        };
    }
}
