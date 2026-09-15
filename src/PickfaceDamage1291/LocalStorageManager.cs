using System.Security.Cryptography;

namespace PickfaceDamage1291;

internal sealed record LocalStorageMoveResult(bool Success, string NewRoot, string Message, string? Warning = null);

internal static class LocalStorageManager
{
    public static string BuildTargetRoot(string selectedFolder)
    {
        var selected = Path.GetFullPath(selectedFolder.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(selected), "PickfaceDamage1291", StringComparison.OrdinalIgnoreCase)
            ? selected
            : Path.Combine(selected, "PickfaceDamage1291");
    }

    public static LocalStorageMoveResult MoveTo(string targetRoot)
    {
        var sourceRoot = Path.GetFullPath(AppPaths.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        targetRoot = Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
            return new LocalStorageMoveResult(false, targetRoot, "Thư mục mới đang trùng với nơi lưu hiện tại.");

        if (IsInside(targetRoot, sourceRoot) || IsInside(sourceRoot, targetRoot))
            return new LocalStorageMoveResult(false, targetRoot, "Không thể chọn thư mục nguồn và thư mục đích lồng vào nhau.");

        if (new Uri(targetRoot + Path.DirectorySeparatorChar).IsUnc)
            return new LocalStorageMoveResult(false, targetRoot, "Chỉ hỗ trợ chuyển dữ liệu sang ổ đĩa local trên máy này.");

        if (BackgroundSyncCoordinator.IsBusy || BackgroundSyncCoordinator.QueueCount > 0)
            return new LocalStorageMoveResult(false, targetRoot, "Đang có tiến trình đồng bộ nền. Hãy chờ đồng bộ xong rồi đổi nơi lưu trữ.");

        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
            return new LocalStorageMoveResult(false, targetRoot, "Thư mục đích đã có dữ liệu. Hãy chọn thư mục trống để tránh ghi đè.");

        var tempRoot = targetRoot + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(tempRoot);
            CopyTree(sourceRoot, tempRoot);
            VerifyTree(sourceRoot, tempRoot);

            if (Directory.Exists(targetRoot)) Directory.Delete(targetRoot, true);
            Directory.Move(tempRoot, targetRoot);

            // Switch the next application start only after the complete copy was verified.
            AppPaths.SaveConfiguredRoot(targetRoot);

            string? warning = null;
            try
            {
                Directory.Delete(sourceRoot, true);
            }
            catch (Exception ex)
            {
                warning = "Dữ liệu mới đã được kiểm tra và kích hoạt, nhưng không thể xóa bản cũ. Có thể xóa thủ công sau khi xác nhận ứng dụng hoạt động bình thường. " + ex.Message;
            }

            return new LocalStorageMoveResult(true, targetRoot, "Đã chuyển toàn bộ dữ liệu local sang vị trí mới.", warning);
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            return new LocalStorageMoveResult(
                false,
                targetRoot,
                "Không thể tự chuyển dữ liệu. Dữ liệu tại vị trí cũ vẫn được giữ nguyên. Hãy kiểm tra quyền ghi/dung lượng ổ đĩa hoặc tự sao chép thủ công nếu cần.\n\n" + ex.Message);
        }
    }

    private static void CopyTree(string sourceRoot, string targetRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
    }

    private static void VerifyTree(string sourceRoot, string targetRoot)
    {
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var targetFiles = Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(targetRoot, path))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!sourceFiles.SequenceEqual(targetFiles, StringComparer.OrdinalIgnoreCase))
            throw new IOException("Danh sách file sau khi sao chép không khớp dữ liệu nguồn.");

        foreach (var relative in sourceFiles)
        {
            var source = Path.Combine(sourceRoot, relative);
            var target = Path.Combine(targetRoot, relative);
            var sourceInfo = new FileInfo(source);
            var targetInfo = new FileInfo(target);
            if (sourceInfo.Length != targetInfo.Length)
                throw new IOException($"Kích thước file không khớp: {relative}");
            if (!string.Equals(Hash(source), Hash(target), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Kiểm tra toàn vẹn file không đạt: {relative}");
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsInside(string candidate, string parent)
    {
        var parentWithSlash = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidateWithSlash = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidateWithSlash.StartsWith(parentWithSlash, StringComparison.OrdinalIgnoreCase);
    }
}
