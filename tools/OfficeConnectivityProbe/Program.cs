using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PickfaceOfficeProbe;

internal static class Program
{
    private const string GatewayUrl = "https://script.google.com/macros/s/AKfycbwfB-NdKPRj-GN4T_MRS89aaJ8ihRsPfT9qd2wUhBntOAOtBF1ycuaNJRdYN5JCFf12/exec";
    private const string RtdbUrl = "https://pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app/.info/serverTimeOffset.json";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
    private static StreamWriter? Log;

    private sealed record Target(string Name, string Url, string Host, bool GatewayPost = false);
    private sealed class TargetResult
    {
        public required string Name { get; init; }
        public bool Dns { get; set; }
        public bool Tcp443 { get; set; }
        public bool Tls { get; set; }
        public bool HttpSystem { get; set; }
        public bool HttpDirect { get; set; }
        public bool Curl { get; set; }
        public bool PowerShell { get; set; }
        public string? SystemStatus { get; set; }
        public string? DirectStatus { get; set; }
    }

    [STAThread]
    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "PICKFACE DAMAGE 1291 - Office Connectivity Probe";

        var logDir = ResolveLogDirectory();
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"office_probe_{DateTime.Now:yyyyMMdd_HHmmss}_{Sanitize(Environment.MachineName)}.log");
        await using var writer = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        Log = writer;

        Write("PICKFACE DAMAGE 1291 - OFFICE CONNECTIVITY PROBE v1.0.0");
        Write($"StartedLocal={DateTimeOffset.Now:O}");
        Write($"Machine={Environment.MachineName}");
        Write($"OS={Environment.OSVersion}");
        Write($"Framework={Environment.Version}");
        Write($"64BitProcess={Environment.Is64BitProcess}");
        Write($"LogPath={logPath}");
        Write("NOTE=Tool chi thuc hien DNS/TCP/TLS/HTTP read-only va gateway_info. Khong ghi/xoa du lieu nghiep vu.");

        await CaptureEnvironmentAsync();

        var targets = new[]
        {
            new Target("GitHub", "https://github.com/", "github.com"),
            new Target("GitHub API", "https://api.github.com/repos/tamnv2/pickfacedamage1291", "api.github.com"),
            new Target("GitHub RAW config", "https://raw.githubusercontent.com/tamnv2/pickfacedamage1291/main/runtime-config.json", "raw.githubusercontent.com"),
            new Target("Google Apps Script", "https://script.google.com/", "script.google.com"),
            new Target("Project Google Gateway", GatewayUrl, "script.google.com", true),
            new Target("Google API", "https://www.googleapis.com/", "www.googleapis.com"),
            new Target("Google Drive", "https://drive.google.com/", "drive.google.com"),
            new Target("Google Docs", "https://docs.google.com/", "docs.google.com"),
            new Target("Firebase RTDB project", RtdbUrl, "pickface-damage-1291-default-rtdb.asia-southeast1.firebasedatabase.app"),
            new Target("Firebase Secure Token", "https://securetoken.googleapis.com/v1/token", "securetoken.googleapis.com"),
            new Target("Firebase Identity Toolkit", "https://identitytoolkit.googleapis.com/v1/accounts:lookup", "identitytoolkit.googleapis.com")
        };

        var results = new List<TargetResult>();
        foreach (var target in targets)
            results.Add(await RunTargetAsync(target));

        await CaptureCommandAsync("NETSH_WINHTTP_PROXY", "netsh.exe", "winhttp show proxy", 10);
        await CaptureCommandAsync("IPCONFIG_ALL", "ipconfig.exe", "/all", 15);
        await CaptureCommandAsync("ROUTE_PRINT", "route.exe", "print", 15);

        WriteSummary(results);
        Write($"FinishedLocal={DateTimeOffset.Now:O}");
        Write("END_OF_PROBE");

        Console.WriteLine();
        Console.WriteLine("HOAN TAT. Gui file log nay cho ChatGPT de phan tich:");
        Console.WriteLine(logPath);
        Console.WriteLine();
        Console.WriteLine("Nhan Enter de dong...");
        Console.ReadLine();
        return 0;
    }

    private static async Task CaptureEnvironmentAsync()
    {
        WriteSection("NETWORK_ENVIRONMENT");
        Write($"NetworkAvailable={NetworkInterface.GetIsNetworkAvailable()}");
        foreach (var variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" })
        {
            var raw = Environment.GetEnvironmentVariable(variable);
            Write($"ENV_{variable}={RedactProxy(raw)}");
        }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var props = nic.GetIPProperties();
                var addresses = props.UnicastAddresses
                    .Where(x => x.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(x => x.Address.ToString());
                var dns = props.DnsAddresses.Select(x => x.ToString());
                var gateways = props.GatewayAddresses.Select(x => x.Address.ToString());
                Write($"NIC name={nic.Name}; type={nic.NetworkInterfaceType}; status={nic.OperationalStatus}; speed={nic.Speed}; ip=[{string.Join(',', addresses)}]; dns=[{string.Join(',', dns)}]; gateway=[{string.Join(',', gateways)}]");
            }
            catch (Exception ex)
            {
                Write($"NIC_ERROR name={nic.Name}; {Describe(ex)}");
            }
        }

#pragma warning disable SYSLIB0014
        try
        {
            var proxy = WebRequest.DefaultWebProxy;
            foreach (var url in new[] { GatewayUrl, RtdbUrl, "https://github.com/" })
            {
                var uri = new Uri(url);
                var proxyUri = proxy?.GetProxy(uri);
                var bypass = proxy?.IsBypassed(uri);
                Write($"SYSTEM_PROXY target={uri.Host}; bypass={bypass}; proxy={RedactProxy(proxyUri?.ToString())}");
            }
        }
        catch (Exception ex)
        {
            Write($"SYSTEM_PROXY_ERROR={Describe(ex)}");
        }
#pragma warning restore SYSLIB0014

        await CaptureCommandAsync("NETSH_INTERFACE", "netsh.exe", "interface show interface", 10);
    }

    private static async Task<TargetResult> RunTargetAsync(Target target)
    {
        WriteSection($"TARGET {target.Name}");
        Write($"URL={target.Url}");
        var result = new TargetResult { Name = target.Name };

        result.Dns = await TestDnsAsync(target.Host);
        result.Tcp443 = await TestTcpAsync(target.Host, 443);
        result.Tls = await TestTlsAsync(target.Host, 443);

        var system = await TestHttpAsync(target, useProxy: true);
        result.HttpSystem = system.Reachable;
        result.SystemStatus = system.Status;

        var direct = await TestHttpAsync(target, useProxy: false);
        result.HttpDirect = direct.Reachable;
        result.DirectStatus = direct.Status;

        result.Curl = await TestCurlAsync(target);
        result.PowerShell = await TestPowerShellAsync(target);

        Write($"TARGET_RESULT name={target.Name}; dns={result.Dns}; tcp443={result.Tcp443}; tls={result.Tls}; http_system={result.HttpSystem}; http_direct={result.HttpDirect}; curl={result.Curl}; powershell={result.PowerShell}; system_status={result.SystemStatus}; direct_status={result.DirectStatus}");
        return result;
    }

    private static async Task<bool> TestDnsAsync(string host)
    {
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            var sw = Stopwatch.StartNew();
            var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
            sw.Stop();
            Write($"DNS PASS host={host}; ms={sw.ElapsedMilliseconds}; addresses=[{string.Join(',', addresses.Select(x => x.ToString()))}]");
            return addresses.Length > 0;
        }
        catch (Exception ex)
        {
            Write($"DNS FAIL host={host}; {Describe(ex)}");
            return false;
        }
    }

    private static async Task<bool> TestTcpAsync(string host, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            using var tcp = new TcpClient();
            var sw = Stopwatch.StartNew();
            await tcp.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            Write($"TCP PASS host={host}; port={port}; ms={sw.ElapsedMilliseconds}; remote={tcp.Client.RemoteEndPoint}");
            return true;
        }
        catch (Exception ex)
        {
            Write($"TCP FAIL host={host}; port={port}; {Describe(ex)}");
            return false;
        }
    }

    private static async Task<bool> TestTlsAsync(string host, int port)
    {
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);
            using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, errors) =>
            {
                Write($"TLS_CERT_POLICY host={host}; errors={errors}");
                return true;
            });
            var sw = Stopwatch.StartNew();
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cts.Token);
            sw.Stop();
            var cert = ssl.RemoteCertificate is null ? null : new X509Certificate2(ssl.RemoteCertificate);
            Write($"TLS PASS host={host}; ms={sw.ElapsedMilliseconds}; protocol={ssl.SslProtocol}; cipher={ssl.NegotiatedCipherSuite}; cert_subject={cert?.Subject}; cert_issuer={cert?.Issuer}; cert_until={cert?.NotAfter:O}");
            return true;
        }
        catch (Exception ex)
        {
            Write($"TLS FAIL host={host}; {Describe(ex)}");
            return false;
        }
    }

    private static async Task<(bool Reachable, string Status)> TestHttpAsync(Target target, bool useProxy)
    {
        var mode = useProxy ? "SYSTEM_PROXY" : "DIRECT_NO_PROXY";
        try
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = useProxy,
                Proxy = null,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = Timeout,
                PooledConnectionLifetime = TimeSpan.FromSeconds(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(2),
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PickfaceOfficeProbe/1.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var request = new HttpRequestMessage(target.GatewayPost ? HttpMethod.Post : HttpMethod.Get, target.Url);
            if (target.GatewayPost)
                request.Content = new StringContent("{\"action\":\"gateway_info\",\"payload\":{}}", Encoding.UTF8, "application/json");

            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            var location = response.Headers.Location?.ToString() ?? "";
            string body = "";
            if (target.GatewayPost)
            {
                body = await response.Content.ReadAsStringAsync();
                if (body.Length > 700) body = body[..700] + "...";
                body = body.Replace('\r', ' ').Replace('\n', ' ');
            }
            Write($"HTTP {mode} PASS name={target.Name}; status={(int)response.StatusCode} {response.StatusCode}; ms={sw.ElapsedMilliseconds}; final={response.RequestMessage?.RequestUri}; type={contentType}; location={location}; body={body}");
            return (true, $"{(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Write($"HTTP {mode} FAIL name={target.Name}; {Describe(ex)}");
            return (false, "EXCEPTION");
        }
    }

    private static async Task<bool> TestCurlAsync(Target target)
    {
        var args = target.GatewayPost
            ? $"-sS -L --max-time 18 -o NUL -w \"HTTP=%{{http_code}} REMOTE=%{{remote_ip}} TLS=%{{ssl_version}} TIME=%{{time_total}}\" -H \"Content-Type: application/json\" --data \"{{\\\"action\\\":\\\"gateway_info\\\",\\\"payload\\\":{{}}}}\" \"{target.Url}\""
            : $"-sS -L --max-time 18 -o NUL -w \"HTTP=%{{http_code}} REMOTE=%{{remote_ip}} TLS=%{{ssl_version}} TIME=%{{time_total}}\" \"{target.Url}\"";
        var run = await RunProcessAsync("curl.exe", args, 25);
        Write($"CURL name={target.Name}; exit={run.ExitCode}; stdout={OneLine(run.StdOut)}; stderr={OneLine(run.StdErr)}");
        return run.ExitCode == 0;
    }

    private static async Task<bool> TestPowerShellAsync(Target target)
    {
        var method = target.GatewayPost ? "Post" : "Get";
        var body = target.GatewayPost ? "-Body '{\"action\":\"gateway_info\",\"payload\":{}}' -ContentType 'application/json'" : "";
        var script = "$ProgressPreference='SilentlyContinue'; try { $r=Invoke-WebRequest -UseBasicParsing -Method " + method + " " + body + " -Uri '" + target.Url.Replace("'", "''") + "' -TimeoutSec 15; Write-Output ('HTTP=' + [int]$r.StatusCode + ' LEN=' + $r.RawContentLength); exit 0 } catch { if ($_.Exception.Response) { Write-Output ('HTTP=' + [int]$_.Exception.Response.StatusCode.value__ + ' REACHED=1'); exit 0 }; Write-Error $_.Exception.ToString(); exit 2 }";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var run = await RunProcessAsync("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", 25);
        Write($"POWERSHELL_IWR name={target.Name}; exit={run.ExitCode}; stdout={OneLine(run.StdOut)}; stderr={OneLine(run.StdErr)}");
        return run.ExitCode == 0;
    }

    private static async Task CaptureCommandAsync(string label, string file, string args, int timeoutSeconds)
    {
        var run = await RunProcessAsync(file, args, timeoutSeconds);
        WriteSection(label);
        Write($"EXIT={run.ExitCode}");
        foreach (var line in run.StdOut.Replace("\r", "").Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) Write(line);
        if (!string.IsNullOrWhiteSpace(run.StdErr)) Write("STDERR=" + OneLine(run.StdErr));
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string file, string args, int timeoutSeconds)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return (-2, await stdoutTask, (await stderrTask) + " TIMEOUT");
            }
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return (-1, "", Describe(ex));
        }
    }

    private static void WriteSummary(IReadOnlyList<TargetResult> results)
    {
        WriteSection("FINAL_SUMMARY");
        var gateway = results.First(x => x.Name == "Project Google Gateway");
        var rtdb = results.First(x => x.Name == "Firebase RTDB project");
        var secure = results.First(x => x.Name == "Firebase Secure Token");
        var identity = results.First(x => x.Name == "Firebase Identity Toolkit");
        var github = results.First(x => x.Name == "GitHub");
        var raw = results.First(x => x.Name == "GitHub RAW config");

        var gatewayReach = AnyHttp(gateway);
        var rtdbReach = AnyHttp(rtdb);
        var firebaseAuthReach = AnyHttp(secure) && AnyHttp(identity);
        var githubReach = AnyHttp(github) || AnyHttp(raw);

        string classification;
        if (gatewayReach && !githubReach && (!rtdbReach || !firebaseAuthReach))
            classification = "OFFICE_GOOGLE_ONLY_OR_PARTIAL_FIREBASE_BLOCK";
        else if (gatewayReach && !githubReach)
            classification = "OFFICE_GITHUB_BLOCKED_BUT_GOOGLE_OK";
        else if (gatewayReach && rtdbReach && firebaseAuthReach && githubReach)
            classification = "OPEN_NETWORK_ALL_PRIMARY_PATHS_REACHABLE";
        else if (!gatewayReach && !githubReach && !rtdbReach)
            classification = "NETWORK_OR_PROXY_BLOCKING_MULTIPLE_SERVICES";
        else
            classification = "MIXED_CONNECTIVITY_REVIEW_PER_TARGET";

        Write($"CLASSIFICATION={classification}");
        Write($"GoogleGatewayReachable={gatewayReach}");
        Write($"FirebaseRtdbReachable={rtdbReach}");
        Write($"FirebaseAuthEndpointsReachable={firebaseAuthReach}");
        Write($"GitHubReachable={githubReach}");
        Write("ROUTE_CANDIDATE_1=Direct Firebase + Google gateway for business sync when all Firebase endpoints are reachable.");
        Write("ROUTE_CANDIDATE_2=Google gateway relay for Firebase RTDB/auth when Gateway PASS but direct Firebase FAIL.");
        Write("ROUTE_CANDIDATE_3=System proxy route when SYSTEM_PROXY PASS and DIRECT_NO_PROXY FAIL.");
        Write("ROUTE_CANDIDATE_4=Direct route when DIRECT_NO_PROXY PASS and SYSTEM_PROXY FAIL.");
        Write("UPLOAD_THIS_LOG_TO_CHATGPT=YES");
    }

    private static bool AnyHttp(TargetResult r) => r.HttpSystem || r.HttpDirect || r.Curl || r.PowerShell;

    private static string ResolveLogDirectory()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var folder = !string.IsNullOrWhiteSpace(exe) ? Path.GetDirectoryName(exe) : null;
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var candidate = Path.Combine(folder, "OfficeProbeLogs");
                Directory.CreateDirectory(candidate);
                var test = Path.Combine(candidate, ".write_test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return candidate;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickfaceDamage1291", "OfficeProbeLogs");
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }

    private static string RedactProxy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
            if (string.IsNullOrEmpty(uri.UserInfo)) return uri.ToString();
            var builder = new UriBuilder(uri) { UserName = "REDACTED", Password = "REDACTED" };
            return builder.Uri.ToString();
        }
        catch { return "[unparseable-proxy-value]"; }
    }

    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? cur = ex; cur is not null && parts.Count < 5; cur = cur.InnerException)
            parts.Add($"{cur.GetType().Name}: {cur.Message.Replace('\r', ' ').Replace('\n', ' ')}");
        return string.Join(" -> ", parts);
    }

    private static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 1500 ? text[..1500] + "..." : text;
    }

    private static void WriteSection(string title)
    {
        Write("");
        Write($"===== {title} =====");
    }

    private static void Write(string text)
    {
        var line = $"[{DateTimeOffset.Now:O}] {text}";
        Console.WriteLine(line);
        Log?.WriteLine(line);
    }
}
