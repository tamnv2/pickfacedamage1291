using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PickfaceDamage1291;

internal sealed record PreparedUploadImage(
    byte[] Bytes,
    string MimeType,
    string FileName,
    long OriginalBytes,
    bool Optimized,
    int OriginalWidth,
    int OriginalHeight,
    int UploadWidth,
    int UploadHeight);

/// <summary>
/// Optimizes only the payload sent to Google Drive. The local source file is never modified.
/// Images that are already light enough are kept byte-for-byte to avoid unnecessary quality loss.
/// </summary>
internal static class ImageUploadOptimizer
{
    public const int MaxLongEdge = 2560;
    public const long JpegQuality = 90L;
    private const long PreserveOriginalMaxBytes = 1024L * 1024L;

    public static async Task<PreparedUploadImage> PrepareAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy ảnh để tối ưu trước khi upload.", path);

        var originalBytes = await File.ReadAllBytesAsync(path, ct);
        var originalMime = GetMimeType(path);
        var originalName = Path.GetFileName(path);

        try
        {
            ct.ThrowIfCancellationRequested();
            using var source = Image.FromFile(path);

            var originalWidth = source.Width;
            var originalHeight = source.Height;
            var maxEdge = Math.Max(originalWidth, originalHeight);
            var alreadyLight = originalBytes.LongLength <= PreserveOriginalMaxBytes && maxEdge <= MaxLongEdge;

            // Keep small images exactly as selected by the user. This avoids generation loss
            // for already-efficient JPEG/PNG files and keeps duplicate/hash behaviour unchanged.
            if (alreadyLight)
                return Original(originalBytes, originalMime, originalName, originalWidth, originalHeight);

            NormalizeExifOrientation(source);

            var orientedWidth = source.Width;
            var orientedHeight = source.Height;
            var scale = Math.Min(1d, (double)MaxLongEdge / Math.Max(orientedWidth, orientedHeight));
            var targetWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));

            using var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight));
            }

            using var output = new MemoryStream();
            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                .First(codec => string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
            using (var encoderParameters = new EncoderParameters(1))
            {
                encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                bitmap.Save(output, jpegCodec, encoderParameters);
            }

            var optimizedBytes = output.ToArray();

            // Never replace the upload payload with a larger file. If compression provides no
            // storage/network benefit, preserve the exact original bytes instead.
            if (optimizedBytes.LongLength >= originalBytes.LongLength)
                return Original(originalBytes, originalMime, originalName, originalWidth, originalHeight);

            return new PreparedUploadImage(
                optimizedBytes,
                "image/jpeg",
                Path.GetFileNameWithoutExtension(originalName) + ".jpg",
                originalBytes.LongLength,
                true,
                originalWidth,
                originalHeight,
                targetWidth,
                targetHeight);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Optimization is an efficiency feature, never a reason to lose a damage report.
            // Unsupported HEIC/WEBP codecs or malformed metadata fall back to the original file.
            AppLog.Exception("IMAGE_UPLOAD_OPTIMIZE_FALLBACK", ex, new Dictionary<string, object?>
            {
                ["file_name"] = originalName,
                ["original_bytes"] = originalBytes.LongLength,
                ["mime_type"] = originalMime
            });
            return Original(originalBytes, originalMime, originalName, 0, 0);
        }
    }

    private static PreparedUploadImage Original(
        byte[] bytes,
        string mimeType,
        string fileName,
        int width,
        int height)
        => new(
            bytes,
            mimeType,
            fileName,
            bytes.LongLength,
            false,
            width,
            height,
            width,
            height);

    private static void NormalizeExifOrientation(Image image)
    {
        const int orientationPropertyId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationPropertyId)) return;

        try
        {
            var orientation = image.GetPropertyItem(orientationPropertyId).Value.FirstOrDefault();
            var rotate = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.RotateNoneFlipY,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (rotate != RotateFlipType.RotateNoneFlipNone)
                image.RotateFlip(rotate);
        }
        catch
        {
            // Invalid or incomplete EXIF must not block upload.
        }
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        _ => "application/octet-stream"
    };
}
