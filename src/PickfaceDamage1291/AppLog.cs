using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickfaceDamage1291;

internal static partial class AppLog
{
    // 2 MB keeps each upload comfortably below the 8 MB gateway limit while avoiding
    // very small files that would cause unnecessary Drive traffic.
    private const long MaxFileBytes = 2L * 1024 * 1024;
    private static readonly TimeSpan MaxOpenFileAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LocalRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan AutoSweepInterval = TimeSpan.FromMinutes(5);

    private static readonly object Gate = new();
    private static readonly SemaphoreSlim UploadGate = new(1, 1);
    private static readonly DateTime ProcessStartedAt = DateTime.Now;

    private static bool _initialized;
    private static string? _currentWritablePath;
    private static DateTime _currentFileCreatedAt;
    private static int _partIndex = 1;
    private static string _boundUsername = "prelogin";
    private static System.Threading.Timer? _autoTimer;
    private static bool _autoStarted;
    private static Dictionary<string, string>? _uploadedSignatures;

    private static string UploadStatePath => Path.Combine(AppPaths.Data, "log_upload_state.json");

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*bearer\\s+)[^\\s,;]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex("(?i)([?&](?:auth|key|token|id_token|refresh_token|access_token)=)[^&\\s]+")]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex("(?i)(\\\"?(?:password|passwd|pwd|secret|credential|id_token|refresh_token|access_token|authorization|cookie)\\\"?\\s*[:=]\\s*\\\"?)[^\\\",;\\s}]+")]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex("(?i)eyJ[a-zA-Z0-9_-]{10,}\\.[a-zA-Z0-9_-]{10,}\\.[a-zA-Z0-9_-]{10,}")]
    private static partial Regex JwtRegex();

    public static void Initialize()
    {
        int expiredRemoved;
        int legacyRecovered;
        lock (Gate)
        {
            if (_initialized) return;
            AppPaths.EnsureCreated();
            Directory.CreateDirectory(AppPaths.Logs);
            legacyRecovered = RecoverLegacySendFilesLocked();
            expiredRemoved = CleanupExpiredLogsLocked();
            LoadUploadStateLocked();
            _initialized = true;
        }

        Info("APP_LOG_READY", "Hệ thống log local realtime đã sẵn sàng.", new Dictionary<string, object?>
        {
            ["process_started_at"] = ProcessStartedAt,
            ["log_file"] = Path.GetFileName(GetCurrentWritablePathForInfo()),
            ["expired_logs_removed"] = expiredRemoved,
            ["legacy_send_files_recovered"] = legacyRecovered,
            ["local_retention_days"] = (int)LocalRetention.TotalDays,
            ["rotation_mb"] = MaxFileBytes / 1024 / 1024
        });
    }

    public static void BindSession(string? username)
    {
        Initialize();
        string? sealedPath = null;
        lock (Gate)
        {
            var next = SafeFilePart(username ?? string.Empty);
            if (string.IsNullOrWhiteSpace(next)) next = "unknown-user";
            if (!string.Equals(_boundUsername, next, StringComparison.OrdinalIgnoreCase))
            {
                sealedPath = SealCurrentLocked(crash: false);
                _boundUsername = next;
            }
            CleanupExpiredLogsLocked();
        }

        StartAutoUpload();
        if (!string.IsNullOrWhiteSpace(sealedPath)) TriggerAutoUpload();
        TriggerAutoUpload();
    }

    public static void TriggerAutoUpload()
    {
        Initialize();
        if (!CanUploadNow()) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await SealAgedFileIfNeededAsync();
                await UploadPendingAsync(null, CancellationToken.None);
            }
            catch
            {
                // Auto upload is best-effort. Local files remain for a later retry/manual send.
            }
        });
    }

    public static void CaptureCrash(string eventName, Exception? exception, string? message = null, bool waitForUpload = false)
    {
        try
        {
            if (exception is not null)
                Write("ERROR", eventName, message ?? exception.Message, null, exception);
            else
                Write("ERROR", eventName, message ?? "Ứng dụng kết thúc bất thường.", null, null);

            string? crashPath;
            lock (Gate)
                crashPath = SealCurrentLocked(crash: true);

            if (string.IsNullOrWhiteSpace(crashPath)) return;

            if (waitForUpload && CanUploadNow())
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    UploadSpecificAsync(crashPath, cts.Token).GetAwaiter().GetResult();
                    return;
                }
                catch
                {
                    // Keep the crash file locally. Next online session/manual send will retry.
                }
            }

            TriggerAutoUpload();
        }
        catch
        {
            // Crash diagnostics must never throw back into the failing process.
        }
    }

    public static void Info(string eventName, string message, IReadOnlyDictionary<string, object?>? details = null)
        => Write("INFO", eventName, message, details, null);

    public static void Warning(string eventName, string message, IReadOnlyDictionary<string, object?>? details = null)
        => Write("WARN", eventName, message, details, null);

    public static void Error(string eventName, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? details = null)
        => Write("ERROR", eventName, message, details, exception);

    public static void Exception(string eventName, Exception exception, IReadOnlyDictionary<string, object?>? details = null)
        => Write("ERROR", eventName, exception.Message, details, exception);

    public static (int FileCount, long Bytes) GetStats()
    {
        Initialize();
        lock (Gate)
        {
            CleanupExpiredLogsLocked();
            var files = EnumerateLogFilesLocked().ToList();
            return (files.Count, files.Sum(x => x.Length));
        }
    }

    public static async Task<int> UploadAllAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Initialize();
        if (!CanUploadNow())
            throw new InvalidOperationException("Cần đăng nhập online và kết nối Google để gửi logs.");

        // Manual send seals the current file first, so the uploaded copy is immutable while
        // gateway diagnostics continue in a newly-created file.
        lock (Gate)
            SealCurrentLocked(crash: false);

        var sent = await UploadPendingAsync(progress, ct);
        Info("LOG_UPLOAD_COMPLETE", "Đã gửi các file log mới/thay đổi lên Drive và vẫn giữ bản local 7 ngày.",
            new Dictionary<string, object?> { ["sent_files"] = sent });
        progress?.Report(sent == 0
            ? "Không có file log mới/thay đổi cần gửi."
            : $"Đã gửi {sent:N0} file log. Bản local được giữ 7 ngày.");
        return sent;
    }

    private static void StartAutoUpload()
    {
        lock (Gate)
        {
            if (_autoStarted) return;
            _autoStarted = true;
            _autoTimer = new System.Threading.Timer(
                _ => TriggerAutoUpload(),
                null,
                TimeSpan.FromMinutes(1),
                AutoSweepInterval);
        }
    }

    private static async Task SealAgedFileIfNeededAsync()
    {
        string? sealedPath = null;
        lock (Gate)
        {
            CleanupExpiredLogsLocked();
            if (!string.IsNullOrWhiteSpace(_currentWritablePath) &&
                File.Exists(_currentWritablePath) &&
                _currentFileCreatedAt != default &&
                DateTime.Now - _currentFileCreatedAt >= MaxOpenFileAge)
            {
                sealedPath = SealCurrentLocked(crash: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(sealedPath))
            await UploadPendingAsync(null, CancellationToken.None);
    }

    private static async Task<int> UploadPendingAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (!CanUploadNow()) return 0;

        await UploadGate.WaitAsync(ct);
        try
        {
            List<FileInfo> files;
            string? current;
            lock (Gate)
            {
                CleanupExpiredLogsLocked();
                current = _currentWritablePath;
                files = EnumerateLogFilesLocked()
                    .Where(x => !string.Equals(x.FullName, current, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.CreationTimeUtc)
                    .ToList();
            }

            var sent = 0;
            var candidates = new List<FileInfo>();
            lock (Gate)
            {
                LoadUploadStateLocked();
                foreach (var file in files)
                {
                    var signature = FileSignature(file);
                    if (_uploadedSignatures!.TryGetValue(file.Name, out var old) &&
                        string.Equals(old, signature, StringComparison.Ordinal))
                        continue;
                    candidates.Add(file);
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = candidates[i];
                progress?.Report($"Đang gửi log {i + 1}/{candidates.Count}: {file.Name}");
                if (await UploadSpecificCoreAsync(file.FullName, ct)) sent++;
            }

            return sent;
        }
        finally
        {
            UploadGate.Release();
        }
    }

    private static async Task<bool> UploadSpecificAsync(string path, CancellationToken ct)
    {
        await UploadGate.WaitAsync(ct);
        try
        {
            return await UploadSpecificCoreAsync(path, ct);
        }
        finally
        {
            UploadGate.Release();
        }
    }

    private static async Task<bool> UploadSpecificCoreAsync(string path, CancellationToken ct)
    {
        if (!CanUploadNow() || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

        byte[] bytes;
        FileInfo snapshot;
        lock (Gate)
        {
            snapshot = new FileInfo(path);
            if (snapshot.Length <= 0) return false;
            bytes = File.ReadAllBytes(path);
        }

        await GoogleGatewayV140.UploadLogAsync(snapshot.Name, bytes, ct);

        lock (Gate)
        {
            LoadUploadStateLocked();
            if (File.Exists(path))
            {
                var after = new FileInfo(path);
                _uploadedSignatures![after.Name] = FileSignature(after);
                SaveUploadStateLocked();
            }
        }
        return true;
    }

    private static bool CanUploadNow()
    {
        try
        {
            return AppSession.Current is { OfflineMode: false } && GoogleService.IsConnected();
        }
        catch
        {
            return false;
        }
    }

    private static void Write(
        string level,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? details,
        Exception? exception)
    {
        string? sealedPath = null;
        try
        {
            if (!_initialized)
            {
                AppPaths.EnsureCreated();
                Directory.CreateDirectory(AppPaths.Logs);
                _initialized = true;
            }

            var safeDetails = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (details is not null)
            {
                foreach (var pair in details)
                {
                    safeDetails[pair.Key] = IsSensitiveKey(pair.Key)
                        ? "<redacted>"
                        : SanitizeValue(pair.Value);
                }
            }

            var username = string.Empty;
            try { username = AppSession.Current?.Profile.Username ?? string.Empty; } catch { }

            var entry = new Dictionary<string, object?>
            {
                ["time"] = DateTimeOffset.Now.ToString("O"),
                ["level"] = level,
                ["event"] = SanitizeText(eventName),
                ["message"] = SanitizeText(message),
                ["username"] = SanitizeText(username, 200),
                ["device_id"] = SafeDeviceId(),
                ["machine_name"] = SanitizeText(Environment.MachineName, 200),
                ["version"] = VersionUpdateService.CurrentVersionText,
                ["details"] = safeDetails.Count == 0 ? null : safeDetails
            };

            if (exception is not null)
            {
                entry["exception_type"] = exception.GetType().FullName ?? exception.GetType().Name;
                entry["exception_message"] = SanitizeText(exception.Message);
                entry["stack"] = SanitizeText(exception.StackTrace ?? string.Empty, 12000);
            }

            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (Gate)
            {
                var path = WritablePathLocked(out var agedPath);
                if (!string.IsNullOrWhiteSpace(agedPath)) sealedPath = agedPath;
                File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));

                if (new FileInfo(path).Length >= MaxFileBytes)
                    sealedPath = SealCurrentLocked(crash: false);
            }
        }
        catch
        {
            // Diagnostics must never block the business flow.
        }

        if (!string.IsNullOrWhiteSpace(sealedPath)) TriggerAutoUpload();
    }

    private static IEnumerable<FileInfo> EnumerateLogFilesLocked()
    {
        if (!Directory.Exists(AppPaths.Logs)) yield break;
        foreach (var path in Directory.EnumerateFiles(AppPaths.Logs, "*.log", SearchOption.TopDirectoryOnly))
            yield return new FileInfo(path);
    }

    private static string WritablePathLocked(out string? sealedPath)
    {
        sealedPath = null;
        if (!string.IsNullOrWhiteSpace(_currentWritablePath) &&
            File.Exists(_currentWritablePath) &&
            _currentFileCreatedAt != default &&
            DateTime.Now - _currentFileCreatedAt >= MaxOpenFileAge)
        {
            sealedPath = SealCurrentLocked(crash: false);
        }

        if (string.IsNullOrWhiteSpace(_currentWritablePath))
        {
            _currentFileCreatedAt = DateTime.Now;
            _currentWritablePath = BuildProcessLogPath(_partIndex, _currentFileCreatedAt);
        }

        return _currentWritablePath;
    }

    private static string? SealCurrentLocked(bool crash)
    {
        if (string.IsNullOrWhiteSpace(_currentWritablePath) || !File.Exists(_currentWritablePath))
        {
            _currentWritablePath = null;
            _currentFileCreatedAt = default;
            return null;
        }

        var path = _currentWritablePath;
        _currentWritablePath = null;
        _currentFileCreatedAt = default;
        _partIndex++;

        if (!crash) return path;

        var directory = Path.GetDirectoryName(path) ?? AppPaths.Logs;
        var crashName = "crash_" + Path.GetFileName(path);
        var crashPath = UniqueLocalPath(directory, crashName);
        try
        {
            File.Move(path, crashPath);
            return crashPath;
        }
        catch
        {
            return path;
        }
    }

    private static string BuildProcessLogPath(int partIndex, DateTime createdAt)
    {
        var user = SafeFilePart(_boundUsername);
        var machine = SafeFilePart(Environment.MachineName);
        var version = SafeFilePart(VersionUpdateService.CurrentVersionText);
        var suffix = partIndex <= 1 ? string.Empty : $"_part{partIndex:00}";
        var name = $"pickface_{user}_{machine}_{version}_{createdAt:yyyyMMdd_HHmmss_fff}_p{Environment.ProcessId}{suffix}.log";
        return UniqueLocalPath(AppPaths.Logs, name);
    }

    private static string UniqueLocalPath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}_v{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    private static string GetCurrentWritablePathForInfo()
    {
        lock (Gate)
            return WritablePathLocked(out _);
    }

    private static int CleanupExpiredLogsLocked()
    {
        if (!Directory.Exists(AppPaths.Logs)) return 0;
        var cutoff = DateTime.UtcNow - LocalRetention;
        var removed = 0;

        foreach (var path in Directory.EnumerateFiles(AppPaths.Logs, "*", SearchOption.TopDirectoryOnly).ToList())
        {
            if (!path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".send", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var info = new FileInfo(path);
                var ageBasis = info.LastWriteTimeUtc > info.CreationTimeUtc ? info.LastWriteTimeUtc : info.CreationTimeUtc;
                if (ageBasis >= cutoff) continue;
                if (string.Equals(path, _currentWritablePath, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(path);
                removed++;
            }
            catch
            {
                // Retention cleanup is best-effort.
            }
        }

        if (_uploadedSignatures is not null)
        {
            var existing = Directory.EnumerateFiles(AppPaths.Logs, "*.log", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _uploadedSignatures.Keys.Where(x => !existing.Contains(x)).ToList())
                _uploadedSignatures.Remove(key);
            SaveUploadStateLocked();
        }

        return removed;
    }

    private static int RecoverLegacySendFilesLocked()
    {
        if (!Directory.Exists(AppPaths.Logs)) return 0;
        var recovered = 0;
        foreach (var path in Directory.EnumerateFiles(AppPaths.Logs, "*.send", SearchOption.TopDirectoryOnly).ToList())
        {
            try
            {
                var basePath = path[..^5];
                if (!basePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) basePath += ".log";
                var target = UniqueLocalPath(Path.GetDirectoryName(basePath) ?? AppPaths.Logs, Path.GetFileName(basePath));
                File.Move(path, target);
                recovered++;
            }
            catch
            {
                // Keep legacy file untouched if it cannot be recovered safely.
            }
        }
        return recovered;
    }

    private static void LoadUploadStateLocked()
    {
        if (_uploadedSignatures is not null) return;
        try
        {
            _uploadedSignatures = File.Exists(UploadStatePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(UploadStatePath))
                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _uploadedSignatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (_uploadedSignatures.Comparer != StringComparer.OrdinalIgnoreCase)
            _uploadedSignatures = new Dictionary<string, string>(_uploadedSignatures, StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveUploadStateLocked()
    {
        if (_uploadedSignatures is null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var temp = UploadStatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_uploadedSignatures), new System.Text.UTF8Encoding(false));
            File.Move(temp, UploadStatePath, true);
        }
        catch
        {
            // Upload state failure only causes a later harmless re-upload; never delete logs.
        }
    }

    private static string FileSignature(FileInfo file)
        => $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty).Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray();
        var safe = new string(chars).Trim().Trim('.', '-');
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static object? SanitizeValue(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            string s => SanitizeText(s),
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            Guid g => g.ToString("D"),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            _ => SanitizeText(Convert.ToString(value) ?? string.Empty)
        };
    }

    private static string SanitizeText(string value, int maxLength = 4000)
    {
        var text = value ?? string.Empty;
        text = BearerRegex().Replace(text, "$1<redacted>");
        text = QuerySecretRegex().Replace(text, "$1<redacted>");
        text = KeyValueSecretRegex().Replace(text, "$1<redacted>");
        text = JwtRegex().Replace(text, "<redacted-jwt>");
        if (text.Length > maxLength) text = text[..maxLength] + "...";
        return text;
    }

    private static bool IsSensitiveKey(string key)
    {
        var k = key.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return k.Contains("password") || k.Contains("passwd") || k.Contains("secret") ||
               k.Contains("credential") || k.Contains("token") || k.Contains("authorization") ||
               k.Contains("cookie") || k.Contains("api_key") || k.Contains("apikey");
    }

    private static string SafeDeviceId()
    {
        try
        {
            var id = SecureSessionStore.GetOrCreateDeviceId();
            return id.Length <= 16 ? id : id[..16];
        }
        catch
        {
            return "unknown";
        }
    }
}
