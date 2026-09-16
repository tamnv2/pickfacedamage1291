using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickfaceDamage1291;

internal static partial class AppLog
{
    private const long MaxFileBytes = 4L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly DateTime ProcessStartedAt = DateTime.Now;
    private static bool _initialized;
    private static string? _currentWritablePath;
    private static int _partIndex = 1;

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
        lock (Gate)
        {
            if (_initialized) return;
            AppPaths.EnsureCreated();
            Directory.CreateDirectory(AppPaths.Logs);
            _initialized = true;
        }
        Info("APP_LOG_READY", "Hệ thống log local realtime đã sẵn sàng.", new Dictionary<string, object?>
        {
            ["process_started_at"] = ProcessStartedAt,
            ["log_file"] = Path.GetFileName(GetCurrentWritablePathForInfo())
        });
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
            var files = EnumerateLogFilesLocked().ToList();
            return (files.Count, files.Sum(x => x.Length));
        }
    }

    public static async Task<int> UploadAllAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Initialize();
        if (!GoogleService.IsConnected())
            throw new InvalidOperationException("Cần đăng nhập online và kết nối Google để gửi logs.");

        List<FileInfo> files;
        lock (Gate)
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.Logs, "*.log", SearchOption.TopDirectoryOnly).ToList())
            {
                try
                {
                    var sealedPath = file + ".send";
                    if (File.Exists(sealedPath))
                        sealedPath = file + "." + Guid.NewGuid().ToString("N") + ".send";
                    File.Move(file, sealedPath);
                    if (string.Equals(_currentWritablePath, file, StringComparison.OrdinalIgnoreCase))
                        _currentWritablePath = null;
                }
                catch
                {
                    // A file that cannot be sealed remains local and is never deleted.
                }
            }
            files = EnumerateLogFilesLocked().Where(x => x.Extension.Equals(".send", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.CreationTimeUtc).ToList();
        }

        if (files.Count == 0)
        {
            progress?.Report("Không có file log chờ gửi.");
            return 0;
        }

        var sent = 0;
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report($"Đang gửi log {i + 1}/{files.Count}: {DisplayUploadName(file.Name)}");
            byte[] bytes;
            lock (Gate)
                bytes = File.ReadAllBytes(file.FullName);

            await GoogleGatewayV140.UploadLogAsync(DisplayUploadName(file.Name), bytes, ct);

            lock (Gate)
            {
                if (File.Exists(file.FullName)) File.Delete(file.FullName);
            }
            sent++;
        }

        progress?.Report($"Đã gửi thành công {sent:N0} file log. File đã gửi đã được xoá khỏi máy.");
        return sent;
    }

    private static void Write(
        string level,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? details,
        Exception? exception)
    {
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
                var path = WritablePathLocked();
                File.AppendAllText(path, line, new System.Text.UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never block the business flow.
        }
    }

    private static IEnumerable<FileInfo> EnumerateLogFilesLocked()
    {
        if (!Directory.Exists(AppPaths.Logs)) yield break;
        foreach (var path in Directory.EnumerateFiles(AppPaths.Logs, "*", SearchOption.TopDirectoryOnly))
        {
            if (!path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".send", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new FileInfo(path);
        }
    }

    private static string WritablePathLocked()
    {
        if (string.IsNullOrWhiteSpace(_currentWritablePath))
            _currentWritablePath = BuildProcessLogPath(_partIndex);

        if (!File.Exists(_currentWritablePath) || new FileInfo(_currentWritablePath).Length < MaxFileBytes)
            return _currentWritablePath;

        _partIndex++;
        _currentWritablePath = BuildProcessLogPath(_partIndex);
        return _currentWritablePath;
    }

    private static string BuildProcessLogPath(int partIndex)
    {
        var device = SafeFilePart(SafeDeviceId());
        var prefix = $"pickface_{ProcessStartedAt:yyyyMMdd_HHmmss_fff}_{device}";
        var suffix = partIndex <= 1 ? string.Empty : $"_part{partIndex:00}";
        return Path.Combine(AppPaths.Logs, prefix + suffix + ".log");
    }

    private static string GetCurrentWritablePathForInfo()
    {
        lock (Gate)
            return WritablePathLocked();
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty).Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var safe = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safe) ? "unknown-device" : safe;
    }

    private static string DisplayUploadName(string sealedName)
    {
        var name = sealedName;
        if (name.EndsWith(".send", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        var marker = name.LastIndexOf(".log.", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) name = name[..(marker + 4)];
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ? name : name + ".log";
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
