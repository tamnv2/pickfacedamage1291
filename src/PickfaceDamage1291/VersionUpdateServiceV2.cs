using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string HtmlUrl,
    string Notes,
    string AssetName,
    string AssetUrl,
    string? Digest);

internal static class VersionUpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    public static string CurrentExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được file EXE đang chạy.");

    public static string CurrentDirectory =>
        Path.GetDirectoryName(CurrentExecutablePath) ?? throw new InvalidOperationException("Không xác định được thư mục đang chạy ứng dụng.");

    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(CloudConfig.GitHubLatestReleaseApi, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!TryParseVersion(tag, out var version)) return null;
        var htmlUrl = root.TryGetProperty("html_url", out var html)
            ? html.GetString() ?? CloudConfig.GitHubReleasesPage
            : CloudConfig.GitHubReleasesPage;
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;

        string assetName = string.Empty;
        string assetUrl = string.Empty;
        string? digest = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) continue;
                assetName = name;
                assetUrl = asset.TryGetProperty("browser_download_url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
                digest = asset.TryGetProperty("digest", out var digestNode) ? digestNode.GetString() : null;
                break;
            }
        }

        return new ReleaseInfo(version, tag, htmlUrl, notes, assetName, assetUrl, digest);
    }

    public static bool IsNewer(ReleaseInfo release) => release.Version > Normalize(CurrentVersion);

    public static bool CanWriteCurrentDirectory(out string reason)
    {
        reason = string.Empty;
        var directory = CurrentDirectory;
        var probe = Path.Combine(directory, $".pickface-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            reason = ex.Message;
            return false;
        }
    }

    public static async Task DownloadPackageAsync(
        ReleaseInfo release,
        string destination,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.AssetUrl))
            throw new InvalidOperationException("Release mới chưa có gói Windows x64 để tải.");
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Chưa chọn nơi lưu bản cập nhật.", nameof(destination));

        progress?.Report("Đang tải bản cập nhật từ GitHub...");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temp = destination + ".part";
        try
        {
            using var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temp))
                await input.CopyToAsync(output, cancellationToken);
            VerifyDigestIfAvailable(temp, release.Digest);
            File.Move(temp, destination, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static async Task<string> PrepareAutoUpdateAsync(
        ReleaseInfo release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanWriteCurrentDirectory(out var writeError))
            throw new UnauthorizedAccessException("Thư mục chứa ứng dụng không cho phép ghi đè bằng quyền hiện tại. " + writeError);

        var work = Path.Combine(Path.GetTempPath(), "PickfaceDamage1291", "update", release.Tag.Replace(':', '-'));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var zipPath = Path.Combine(work, string.IsNullOrWhiteSpace(release.AssetName) ? "update.zip" : release.AssetName);
        await DownloadPackageAsync(release, zipPath, progress, cancellationToken);

        progress?.Report("Đang kiểm tra gói cập nhật...");
        var extract = Path.Combine(work, "extract");
        Directory.CreateDirectory(extract);
        ExtractZipSafely(zipPath, extract);
        var newExe = Directory.EnumerateFiles(extract, "PickfaceDamage1291.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidDataException("Gói cập nhật không có PickfaceDamage1291.exe.");

        var targetExe = CurrentExecutablePath;
        var stagedExe = targetExe + ".update";
        File.Copy(newExe, stagedExe, true);

        var script = Path.Combine(work, "apply-update.cmd");
        var backupExe = targetExe + ".bak";
        var pid = Environment.ProcessId;
        var scriptText = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal")
            .AppendLine($"set PID={pid}")
            .AppendLine(":wait")
            .AppendLine("tasklist /FI \"PID eq %PID%\" 2>NUL | find \"%PID%\" >NUL")
            .AppendLine("if not errorlevel 1 (")
            .AppendLine("  ping 127.0.0.1 -n 2 >NUL")
            .AppendLine("  goto wait")
            .AppendLine(")")
            .AppendLine($"copy /Y \"{EscapeCmd(targetExe)}\" \"{EscapeCmd(backupExe)}\" >NUL")
            .AppendLine($"move /Y \"{EscapeCmd(stagedExe)}\" \"{EscapeCmd(targetExe)}\" >NUL")
            .AppendLine("if errorlevel 1 (")
            .AppendLine($"  if exist \"{EscapeCmd(backupExe)}\" copy /Y \"{EscapeCmd(backupExe)}\" \"{EscapeCmd(targetExe)}\" >NUL")
            .AppendLine($"  start \"\" \"{EscapeCmd(targetExe)}\"")
            .AppendLine("  exit /b 1")
            .AppendLine(")")
            .AppendLine($"start \"\" \"{EscapeCmd(targetExe)}\"")
            .AppendLine($"if exist \"{EscapeCmd(backupExe)}\" del /Q \"{EscapeCmd(backupExe)}\" >NUL 2>NUL")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(script, scriptText, Encoding.ASCII);
        return script;
    }

    // Compatibility for the old MainForm code path. The v1.2.2 visible updater UI uses PrepareAutoUpdateAsync directly.
    public static Task<string> DownloadAndPrepareAsync(
        ReleaseInfo release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        PrepareAutoUpdateAsync(release, progress, cancellationToken);

    public static void LaunchUpdaterAndExit(string scriptPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{scriptPath}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        Application.Exit();
    }

    public static void OpenReleasesPage() =>
        Process.Start(new ProcessStartInfo(CloudConfig.GitHubReleasesPage) { UseShellExecute = true });

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceDamage1291-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var dash = value.IndexOf('-');
        if (dash >= 0) value = value[..dash];
        if (!Version.TryParse(value, out var parsed))
        {
            version = new Version(0, 0, 0);
            return false;
        }
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) =>
        new(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static void VerifyDigestIfAvailable(string path, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return;
        var expected = digest[7..].Trim();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 của gói cập nhật không khớp GitHub Release. Đã dừng cập nhật.");
    }

    private static void ExtractZipSafely(string zipPath, string destination)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!fullPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Gói cập nhật chứa đường dẫn không hợp lệ.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, true);
        }
    }

    private static string EscapeCmd(string value) => value.Replace("%", "%%");
}
