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
    private static readonly HttpClient GatewayHttp = CreateGatewayHttpClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    public static string CurrentExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được file EXE đang chạy.");

    public static string CurrentDirectory =>
        Path.GetDirectoryName(CurrentExecutablePath) ?? throw new InvalidOperationException("Không xác định được thư mục đang chạy ứng dụng.");

    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        Exception? directError = null;
        try
        {
            return await GetLatestFromGitHubAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            directError = ex;
            AppLog.Warning("UPDATE_GITHUB_METADATA_FALLBACK", "Không đọc được GitHub Release trực tiếp; chuyển sang Google gateway.", new Dictionary<string, object?>
            {
                ["error"] = ex.GetType().Name
            });
        }

        try
        {
            return await GetLatestFromGoogleMirrorAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception fallbackError)
        {
            throw new InvalidOperationException(
                "Không kiểm tra được bản cập nhật qua GitHub trực tiếp và Google gateway dự phòng.",
                directError is null ? fallbackError : new AggregateException(directError, fallbackError));
        }
    }

    private static async Task<ReleaseInfo?> GetLatestFromGitHubAsync(CancellationToken cancellationToken)
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

    private static async Task<ReleaseInfo?> GetLatestFromGoogleMirrorAsync(CancellationToken cancellationToken)
    {
        using var doc = await PostGatewayAsync("release_mirror_manifest", new { }, cancellationToken);
        var root = doc.RootElement;
        EnsureGatewayOk(root);

        var tag = root.TryGetProperty("tag", out var tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
        if (!TryParseVersion(tag, out var version)) return null;
        var assetName = root.TryGetProperty("asset_name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
        var assetUrl = root.TryGetProperty("asset_url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
        var digest = root.TryGetProperty("digest", out var digestNode) ? digestNode.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var htmlNode) ? htmlNode.GetString() ?? CloudConfig.GitHubReleasesPage : CloudConfig.GitHubReleasesPage;
        var notes = root.TryGetProperty("notes", out var notesNode) ? notesNode.GetString() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(assetName))
            throw new InvalidDataException("Google gateway không trả tên gói Windows x64.");
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Google gateway không trả SHA-256 hợp lệ cho gói cập nhật.");

        AppLog.Info("UPDATE_METADATA_GOOGLE_FALLBACK", "Đã đọc metadata cập nhật qua Google gateway.", new Dictionary<string, object?>
        {
            ["tag"] = tag,
            ["asset"] = assetName
        });
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
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Chưa chọn nơi lưu bản cập nhật.", nameof(destination));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temp = destination + ".part";
        Exception? directError = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(release.AssetUrl))
            {
                try
                {
                    progress?.Report("Đang tải bản cập nhật từ GitHub... 5%");
                    await DownloadDirectAsync(release.AssetUrl, temp, progress, cancellationToken);
                    VerifyDigestIfAvailable(temp, release.Digest);
                    File.Move(temp, destination, true);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    directError = ex;
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    AppLog.Warning("UPDATE_GITHUB_DOWNLOAD_FALLBACK", "Không tải được release trực tiếp từ GitHub; chuyển sang Google Drive mirror.", new Dictionary<string, object?>
                    {
                        ["tag"] = release.Tag,
                        ["error"] = ex.GetType().Name
                    });
                }
            }

            progress?.Report("GitHub không truy cập được. Đang tải qua Google Drive dự phòng... 5%");
            try
            {
                await DownloadFromGoogleMirrorAsync(release, temp, progress, cancellationToken);
                VerifyDigestIfAvailable(temp, release.Digest);
                File.Move(temp, destination, true);
                AppLog.Info("UPDATE_GOOGLE_MIRROR_DOWNLOAD_OK", "Đã tải gói cập nhật qua Google Drive mirror.", new Dictionary<string, object?>
                {
                    ["tag"] = release.Tag,
                    ["asset"] = release.AssetName
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackError)
            {
                throw new InvalidOperationException(
                    "Không tải được bản cập nhật qua GitHub trực tiếp và Google Drive dự phòng.",
                    directError is null ? fallbackError : new AggregateException(directError, fallbackError));
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static async Task DownloadDirectAsync(
        string assetUrl,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);

        var buffer = new byte[128 * 1024];
        long written = 0;
        var lastPercent = -1;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;

            if (total is > 0)
            {
                var downloadPercent = Math.Clamp((int)Math.Round(written * 100d / total.Value), 0, 100);
                if (downloadPercent != lastPercent)
                {
                    var overallPercent = 5 + (int)Math.Round(downloadPercent * 0.75d);
                    progress?.Report($"Đang tải bản cập nhật từ GitHub... {overallPercent}%");
                    lastPercent = downloadPercent;
                }
            }
        }
        await output.FlushAsync(cancellationToken);
        progress?.Report("Đã tải xong gói cập nhật. 80%");
    }

    private static async Task DownloadFromGoogleMirrorAsync(
        ReleaseInfo release,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await using var output = File.Create(destination);
        long totalSize = -1;
        long written = 0;
        int chunkCount = -1;
        string? mirrorDigest = null;

        for (var index = 0; index < 512; index++)
        {
            using var doc = await PostGatewayAsync(
                "release_mirror_chunk",
                new { tag = release.Tag, asset_name = release.AssetName, chunk_index = index },
                cancellationToken);
            var root = doc.RootElement;
            EnsureGatewayOk(root);

            var returnedTag = root.TryGetProperty("tag", out var tagNode) ? tagNode.GetString() ?? string.Empty : string.Empty;
            var returnedAsset = root.TryGetProperty("asset_name", out var assetNode) ? assetNode.GetString() ?? string.Empty : string.Empty;
            if (!string.Equals(returnedTag, release.Tag, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(returnedAsset, release.AssetName, StringComparison.Ordinal))
                throw new InvalidDataException("Google Drive mirror trả sai release hoặc sai tên asset.");

            var currentTotal = root.GetProperty("total_size").GetInt64();
            var currentCount = root.GetProperty("chunk_count").GetInt32();
            if (currentTotal <= 0 || currentCount <= 0 || currentCount > 512)
                throw new InvalidDataException("Thông tin kích thước/chunk của Google Drive mirror không hợp lệ.");
            if (totalSize < 0) totalSize = currentTotal;
            if (chunkCount < 0) chunkCount = currentCount;
            if (totalSize != currentTotal || chunkCount != currentCount)
                throw new InvalidDataException("Metadata mirror thay đổi trong lúc tải. Đã dừng cập nhật.");

            var encoded = root.TryGetProperty("data_base64", out var dataNode) ? dataNode.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("Google Drive mirror trả chunk rỗng.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException ex) { throw new InvalidDataException("Chunk mirror không phải Base64 hợp lệ.", ex); }

            var expectedChunkHash = root.TryGetProperty("chunk_sha256", out var hashNode) ? hashNode.GetString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedChunkHash))
            {
                var actualChunkHash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!actualChunkHash.Equals(expectedChunkHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256 chunk {index + 1}/{chunkCount} không khớp. Đã dừng cập nhật.");
            }

            mirrorDigest ??= root.TryGetProperty("digest", out var digestNode) ? digestNode.GetString() : null;
            await output.WriteAsync(bytes, cancellationToken);
            written += bytes.LongLength;
            var downloadPercent = totalSize > 0 ? Math.Clamp((int)Math.Round(written * 100d / totalSize), 0, 100) : 0;
            var overallPercent = 5 + (int)Math.Round(downloadPercent * 0.75d);
            progress?.Report($"Đang tải qua Google Drive dự phòng... {overallPercent}% ({index + 1}/{chunkCount})");

            if (index + 1 >= chunkCount) break;
        }

        await output.FlushAsync(cancellationToken);
        if (totalSize <= 0 || chunkCount <= 0 || written != totalSize)
            throw new InvalidDataException($"Gói mirror tải chưa đủ dữ liệu ({written}/{totalSize}).");
        if (!string.IsNullOrWhiteSpace(release.Digest) && !string.IsNullOrWhiteSpace(mirrorDigest) &&
            !release.Digest.Equals(mirrorDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 metadata giữa release và Google Drive mirror không khớp.");
    }

    private static async Task<JsonDocument> PostGatewayAsync(string action, object payload, CancellationToken cancellationToken)
    {
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Build chưa có Google gateway dự phòng cho cập nhật.");

        var body = JsonSerializer.Serialize(new { action, payload });
        using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var response = await GatewayHttp.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google gateway trả HTTP {(int)response.StatusCode}.");
        return JsonDocument.Parse(responseText);
    }

    private static void EnsureGatewayOk(JsonElement root)
    {
        if (root.TryGetProperty("ok", out var okNode) && okNode.ValueKind == JsonValueKind.True) return;
        var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() ?? string.Empty : string.Empty;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google gateway không xử lý được yêu cầu cập nhật." : error);
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

        progress?.Report("Đang kiểm tra gói cập nhật... 86%");
        var extract = Path.Combine(work, "extract");
        Directory.CreateDirectory(extract);
        progress?.Report("Đang giải nén gói cập nhật... 90%");
        ExtractZipSafely(zipPath, extract);
        var newExe = Directory.EnumerateFiles(extract, "PickfaceDamage1291.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidDataException("Gói cập nhật không có PickfaceDamage1291.exe.");

        var targetExe = CurrentExecutablePath;
        var stagedExe = targetExe + ".update";
        progress?.Report("Đang chuẩn bị file cập nhật... 95%");
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
        progress?.Report("Sẵn sàng cài đặt bản cập nhật. 100%");
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
        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(5), "PickfaceDamage1291-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static HttpClient CreateGatewayHttpClient()
    {
        var client = NetworkHttpClientFactory.Create(TimeSpan.FromMinutes(6), "PickfaceDamage1291-Updater-GoogleFallback");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
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
            throw new InvalidDataException("SHA-256 của gói cập nhật không khớp release. Đã dừng cập nhật.");
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
