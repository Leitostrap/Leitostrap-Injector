using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ZstdSharp;

namespace LeitostrapV7.Core;

public class ProxyService
{
    private static ProxyService? _instance;
    public static ProxyService Instance => _instance ??= new ProxyService();

    private ProxyService() { }

    private const string HostsMarker = "# Leitostrap proxy entry";
    private static readonly string HostsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32", "drivers", "etc", "hosts");
    private static readonly string CertDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leitostrap Injector", "Proxy Certificates");

    private static readonly int[] CandidatePorts = { 443, 8443, 2053, 2083, 2087, 2096, 8843, 18443, 2443, 4443 };

    private static readonly string[] SettingsHosts =
    {
        "clientsettingscdn.roblox.com",
        "clientsettings.roblox.com"
    };

    private static readonly string[] SettingsPathSeqs =
    {
        "/v2/settings/application/",
        "/v2/settings-compressed/application/",
        "/settings/application/",
        "/settings-compressed/application/"
    };

    private static readonly string[] FlagPrefixes =
    {
        "FFlag", "DFFlag", "FInt", "DFInt", "FString", "DFString",
        "FLog", "DFLog", "SFFlag", "SDFFlag", "SFInt", "SDFInt",
        "SFString", "SDFString", "FFloat", "DFFloat"
    };

    private static readonly HashSet<string> ConditionalHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "if-none-match", "if-modified-since", "if-range"
    };

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection", "keep-alive", "proxy-connection", "proxy-authenticate",
        "proxy-authorization", "te", "trailer", "transfer-encoding", "upgrade"
    };

    private static readonly HashSet<string> SpoiledHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "etag", "content-md5", "x-signature-ed25519", "last-modified", "date"
    };

    private TcpListener? _listener;
    private Thread? _serverThread;
    private X509Certificate2? _caCert;
    private RSA? _caKey;
    private X509Certificate2? _defaultCert;
    private readonly ConcurrentDictionary<string, X509Certificate2> _hostCerts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPAddress[]> _upstreamIps =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _flagsLock = new();
    private readonly object _certGenLock = new();
    private Dictionary<string, string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private int _counter;
    private int _activePort;
    private readonly ConcurrentDictionary<string, byte[]> _dczDictCache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning { get; private set; }
    public bool IsReady => IsRunning && _defaultCert != null;
    public int ActivePort => _activePort;
    public int ActiveFlagsCount { get { lock (_flagsLock) return _flags.Count; } }

    private int _settingsRequests;
    private int _successfulInjections;

    public int SettingsRequests => _settingsRequests;
    public int SuccessfulInjections => _successfulInjections;

    public Dictionary<string, string> Flags
    {
        get { lock (_flagsLock) return new Dictionary<string, string>(_flags, StringComparer.OrdinalIgnoreCase); }
    }

    public Action<string, string>? Log { get; set; }

    private void OnLog(string msg)
    {
        try { Log?.Invoke("Proxy", msg); } catch { }
    }

    public bool Start(Dictionary<string, string> flags)
    {
        if (IsRunning)
        {
            UpdateFlags(flags);
            return true;
        }

        try
        {
            lock (_flagsLock)
            {
                var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (flags != null)
                {
                    foreach (var kv in flags)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                        clean[kv.Key.Trim()] = (kv.Value ?? "").Trim();
                    }
                }
                _flags = clean;
            }

            Directory.CreateDirectory(CertDir);

            if (!SetupCertificates())
            {
                OnLog("Certificate setup failed");
                return false;
            }

            InstallCaIntoCacertPem();

            ResolveUpstreamIPs();

            _activePort = 0;
            TcpListener? bound = null;
            foreach (int port in CandidatePorts)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.IPv6Any, port);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    listener.Start(512);
                    bound = listener;
                    _activePort = port;
                    OnLog($"Listening on 127.0.0.1:{port} (IPv4+IPv6 dual-stack)");
                    break;
                }
                catch (SocketException sex)
                {
                    OnLog($"Port {port} unavailable ({sex.SocketErrorCode}), trying next...");
                }
                catch (Exception ex)
                {
                    OnLog($"Port {port} failed: {ex.Message}");
                }
            }

            if (bound == null || _activePort == 0)
            {
                OnLog("All candidate ports are unavailable");
                return false;
            }

            if (!SetupHosts())
            {
                try { bound.Stop(); } catch { }
                OnLog("Failed to modify hosts file. Run as Administrator.");
                return false;
            }

            if (_activePort != 443)
            {
                if (AddPortProxy(_activePort))
                    OnLog($"Port 443 was busy - portproxy bridge installed (443 -> {_activePort})");
                else
                    OnLog("WARNING: 443 busy and portproxy failed - Roblox may not reach the proxy");
            }

            FlushDns();

            _listener = bound;
            IsRunning = true;
            _counter = 0;
            _settingsRequests = 0;
            _successfulInjections = 0;

            _serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "LeitostrapProxyLoop",
                Priority = ThreadPriority.AboveNormal
            };
            _serverThread.Start();

            OnLog($"Proxy ready on port {_activePort} with {ActiveFlagsCount} flags");

            try
            {
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                var existing = store.Certificates.Find(X509FindType.FindByThumbprint, _caCert!.Thumbprint, false);
                if (existing.Count == 0)
                {
                    store.Add(_caCert);
                    OnLog("Proxy CA installed into Windows certificate store");
                }
                else
                {
                    OnLog("Proxy CA already present in Windows store");
                }
            }
            catch (Exception ex)
            {
                OnLog($"CA store install skipped: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            OnLog($"Start failed: {ex.Message}");
            try { _listener?.Stop(); } catch { }
            _listener = null;
            IsRunning = false;
            try { RemoveHosts(); } catch { }
            return false;
        }
    }

    public bool UpdateFlags(Dictionary<string, string> flags)
    {
        try
        {
            lock (_flagsLock)
            {
                var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (flags != null)
                {
                    foreach (var kv in flags)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                        clean[kv.Key.Trim()] = (kv.Value ?? "").Trim();
                    }
                }
                _flags = clean;
            }
            OnLog($"Updated to {ActiveFlagsCount} flags");
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"UpdateFlags error: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
        try { _serverThread?.Join(1500); } catch { }
        _serverThread = null;

        try { RemovePortProxy(); } catch { }
        try { RemoveHosts(); } catch { }
        try { FlushDns(); } catch { }
        try { RemoveCaFromCacertPem(); } catch { }
        try { ClearClientAppSettingsFile(); } catch { }
        try { DeleteVersionFlagCaches(); } catch { }
        try { _hostCerts.Clear(); } catch { }
        try { _upstreamIps.Clear(); } catch { }
        try { _defaultCert?.Dispose(); } catch { }
        _defaultCert = null;
        try { _caCert?.Dispose(); } catch { }
        _caCert = null;
        try { _caKey?.Dispose(); } catch { }
        _caKey = null;
        _activePort = 0;

        OnLog("Stopped, hosts released, certs unloaded, cache cleaned");
    }

    public static void ForceCleanup()
    {
        try
        {
            var proxy = new ProxyService();
            proxy.RemoveHosts();
            FlushDns();
            proxy.RemoveCaFromCacertPem();
            DeleteVersionFlagCaches();
        }
        catch { }
    }

    private static void DeleteVersionFlagCaches()
    {
        try
        {
            string? path = FindLeitostrapFlagCachePath();
            if (path != null && File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                File.Delete(path);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string versionsDir = Path.Combine(appData, "Roblox", "Versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    string cachePath = Path.Combine(dir, "flag_cache.dat");
                    if (File.Exists(cachePath))
                    {
                        try
                        {
                            File.SetAttributes(cachePath, FileAttributes.Normal);
                            File.Delete(cachePath);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    #region Certificates

    private bool SetupCertificates()
    {
        try
        {
            lock (_certGenLock)
            {
                string caPfxPath = Path.Combine(CertDir, "ca.pfx");
                string caPemPath = Path.Combine(CertDir, "ca.crt");

                if (File.Exists(caPfxPath))
                {
                    try
                    {
                        var loaded = new X509Certificate2(caPfxPath);
                        var key = loaded.GetRSAPrivateKey();
                        if (key != null)
                        {
                            _caCert = loaded;
                            _caKey = key;
                            OnLog("Reusing persisted proxy CA");
                        }
                        else
                        {
                            OnLog("Persisted CA has no private key, regenerating");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLog($"Persisted CA load failed ({ex.Message}), regenerating");
                    }
                }

                if (_caCert == null || _caKey == null)
                {
                    _caKey?.Dispose();
                    _caKey = RSA.Create(2048);
                    var req = new CertificateRequest(
                        "CN=Leitostrap Proxy CA, O=Leitostrap, C=US",
                        _caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    req.CertificateExtensions.Add(
                        new X509BasicConstraintsExtension(true, false, 0, true));
                    req.CertificateExtensions.Add(
                        new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

                    var now = DateTimeOffset.UtcNow;
                    var selfSigned = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

                    byte[] pfx = selfSigned.Export(X509ContentType.Pfx);
                    File.WriteAllBytes(caPfxPath, pfx);
                    File.WriteAllText(caPemPath, selfSigned.ExportCertificatePem(), Encoding.UTF8);

                    _caCert = new X509Certificate2(caPfxPath);
                    _caKey = _caCert.GetRSAPrivateKey();
                    OnLog("Generated and persisted new proxy CA");
                }

                _defaultCert = LoadOrGenerateLeaf("proxy.default", "Leitostrap Proxy",
                    SettingsHosts.Concat(new[] { "localhost" }).ToArray(), true);

                foreach (var host in SettingsHosts)
                    _hostCerts[host] = LoadOrGenerateLeaf(host, host, new[] { host }, false);

                if (_caCert == null || _caKey == null || _defaultCert == null || _hostCerts.Count == 0)
                {
                    OnLog("Certificate chain incomplete");
                    return false;
                }
            }

            OnLog("Certificates ready");
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"Cert error: {ex.Message}");
            return false;
        }
    }

    private X509Certificate2 LoadOrGenerateLeaf(string filename, string cn, string[] sanHosts, bool includeLoopbackIp)
    {
        string pfxPath = Path.Combine(CertDir, $"{filename}.pfx");
        if (File.Exists(pfxPath))
        {
            try
            {
                var existing = new X509Certificate2(pfxPath);
                if (existing.HasPrivateKey &&
                    existing.NotAfter > DateTimeOffset.UtcNow.AddDays(7))
                {
                    return existing;
                }
                OnLog($"Leaf certificate {filename} expired or keyless, regenerating");
            }
            catch
            {
                OnLog($"Leaf certificate {filename} unreadable, regenerating");
            }
        }

        var ca = _caCert!;
        var caKey = _caKey!;

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication") }, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var h in sanHosts)
            sanBuilder.AddDnsName(h);
        if (includeLoopbackIp)
            sanBuilder.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var now = DateTimeOffset.UtcNow;
        var signed = req.Create(
            ca.SubjectName,
            X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1),
            now.AddDays(-1),
            now.AddYears(3),
            CreateSerialNumber());

        var leaf = signed.CopyWithPrivateKey(rsa);
        File.WriteAllBytes(pfxPath, leaf.Export(X509ContentType.Pfx));
        var persisted = new X509Certificate2(pfxPath);
        OnLog($"Generated leaf certificate for {cn}");
        return persisted;
    }

    private static byte[] CreateSerialNumber()
    {
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        if (serial[0] == 0) serial[0] = 0x01;
        return serial;
    }

    private X509Certificate2 SelectCertificate(string? sniHost)
    {
        if (!string.IsNullOrEmpty(sniHost))
        {
            string host = sniHost.Trim().ToLowerInvariant();
            if (_hostCerts.TryGetValue(host, out var exact))
                return exact;
        }
        if (_defaultCert != null) return _defaultCert;
        return _hostCerts.Values.FirstOrDefault() ?? _defaultCert!;
    }

    private void InstallCaIntoCacertPem()
    {
        try
        {
            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (!Directory.Exists(versionsDir)) return;

            string caPem = _caCert!.ExportCertificatePem();
            string marker = "# --- Leitostrap Proxy CA Start ---";
            string markerEnd = "# --- Leitostrap Proxy CA End ---";
            int patched = 0;

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                string cacertPath = Path.Combine(dir, "ssl", "cacert.pem");
                if (!File.Exists(cacertPath)) continue;
                try
                {
                    string content = File.ReadAllText(cacertPath, Encoding.UTF8);
                    string stripped = StripCaBlock(content, marker, markerEnd);
                    string updated = stripped.TrimEnd() + "\n" + marker + "\n" +
                                     caPem.Trim() + "\n" + markerEnd + "\n";
                    if (updated != content)
                        File.WriteAllText(cacertPath, updated, Encoding.UTF8);
                    patched++;
                }
                catch { }
            }
            OnLog($"Patched {patched} Roblox cacert.pem files with proxy CA");
        }
        catch (Exception ex)
        {
            OnLog($"cacert.pem patch error: {ex.Message}");
        }
    }

    private static string StripCaBlock(string content, string marker, string markerEnd)
    {
        while (true)
        {
            int start = content.IndexOf(marker, StringComparison.Ordinal);
            int end = content.IndexOf(markerEnd, StringComparison.Ordinal);
            if (start >= 0 && end >= 0 && end > start)
            {
                content = content[..start] + content[(end + markerEnd.Length)..];
                continue;
            }
            if (start >= 0)
            {
                content = content[..start] + content[(start + marker.Length)..];
                continue;
            }
            if (end >= 0)
            {
                content = content[..end] + content[(end + markerEnd.Length)..];
                continue;
            }
            break;
        }
        return content;
    }

    private void RemoveCaFromCacertPem()
    {
        try
        {
            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (!Directory.Exists(versionsDir)) return;

            string marker = "# --- Leitostrap Proxy CA Start ---";
            string markerEnd = "# --- Leitostrap Proxy CA End ---";

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                string cacertPath = Path.Combine(dir, "ssl", "cacert.pem");
                if (!File.Exists(cacertPath)) continue;
                try
                {
                    string content = File.ReadAllText(cacertPath, Encoding.UTF8);
                    if (!content.Contains(marker, StringComparison.Ordinal) &&
                        !content.Contains(markerEnd, StringComparison.Ordinal)) continue;
                    string cleaned = StripCaBlock(content, marker, markerEnd);
                    File.WriteAllText(cacertPath, cleaned, Encoding.UTF8);
                }
                catch { }
            }
        }
        catch { }
    }

    #endregion

    #region Self Test and Diagnostics

    public (bool ok, string msg) RunSelfTest()
    {
        try
        {
            if (!IsRunning || _listener == null)
                return (false, "Proxy is not running");

            OnLog("Self-test: checking firewall and TLS pipeline before launching Roblox...");

            TcpClient testTcp;
            try
            {
                testTcp = new TcpClient();
                testTcp.ReceiveTimeout = 10000;
                testTcp.SendTimeout = 10000;
                testTcp.Connect(IPAddress.Loopback, _activePort);
            }
            catch (Exception ex)
            {
                string msg = $"Self-test FAILED: cannot connect to proxy on 127.0.0.1:{_activePort} ({ex.Message}). " +
                    "A firewall or antivirus is likely blocking the connection. Allow LeitostrapV7.exe through your firewall.";
                OnLog(msg);
                return (false, msg);
            }

            try
            {
                using var ssl = new SslStream(testTcp.GetStream(), false, (s, c, ch, e) => true);
                var handshake = ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "clientsettingscdn.roblox.com",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                });
                if (!handshake.Wait(10000))
                {
                    string msg = "Self-test FAILED: TLS handshake with the proxy did not complete in 10 seconds.";
                    OnLog(msg);
                    return (false, msg);
                }

                byte[] presented = ssl.RemoteCertificate != null
                    ? ssl.RemoteCertificate.Export(X509ContentType.Cert)
                    : Array.Empty<byte>();
                bool ourCert = false;
                if (presented.Length > 0 && _caCert != null)
                {
                    using var presentedCert = new X509Certificate2(presented);
                    ourCert = string.Equals(presentedCert.Issuer, _caCert.Subject, StringComparison.OrdinalIgnoreCase);
                }
                if (!ourCert)
                    OnLog("Self-test warning: proxy did not present a certificate signed by our CA");

                string req =
                    "GET /v2/client-version/WindowsPlayer HTTP/1.1\r\n" +
                    "Host: clientsettingscdn.roblox.com\r\n" +
                    "Accept: application/json\r\n" +
                    "Accept-Encoding: identity\r\n" +
                    "Connection: close\r\n" +
                    "\r\n";
                byte[] reqBytes = Encoding.ASCII.GetBytes(req);
                ssl.Write(reqBytes, 0, reqBytes.Length);
                ssl.Flush();

                var resp = ReadHttpResponse(ssl, allowCloseDelimited: true);
                if (resp == null || resp.Value.StatusCode == 0)
                {
                    string msg = "Self-test FAILED: no HTTP response within 10 seconds. The proxy received the request but " +
                        "could not reach Roblox upstream in time (DNS or upstream connectivity). Check your internet connection " +
                        "or antivirus HTTPS inspection.";
                    OnLog(msg);
                    return (false, msg);
                }
                if (resp.Value.StatusCode >= 500)
                {
                    string msg = $"Self-test FAILED: proxy returned HTTP {resp.Value.StatusCode} - upstream unreachable. " +
                        "Roblox servers may be down, your connection blocked, or an antivirus is intercepting HTTPS.";
                    OnLog(msg);
                    return (false, msg);
                }

                OnLog($"Self-test passed: TLS handshake OK, proxy returned HTTP {resp.Value.StatusCode}, " +
                    $"certificate signed by our CA: {ourCert}");
                return (true, "Proxy self-test passed");
            }
            finally
            {
                try { testTcp.Close(); } catch { }
            }
        }
        catch (Exception ex)
        {
            OnLog($"Self-test error: {ex.Message}");
            return (false, $"Self-test error: {ex.Message}");
        }
    }

    public bool VerifyHostsActive()
    {
        try
        {
            foreach (var host in SettingsHosts)
            {
                var entry = Dns.GetHostEntry(host);
                bool looped = entry.AddressList.Any(ip =>
                    ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback));
                if (!looped)
                {
                    OnLog($"WARNING: {host} resolves to {entry.AddressList.FirstOrDefault()} instead of 127.0.0.1 - hosts file not active");
                    return false;
                }
            }
            OnLog("Hosts verification passed: settings hosts resolve to loopback");
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"Hosts verification failed: {ex.Message}");
            return false;
        }
    }

    public void DetectFirewall()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("netsh",
                "advfirewall show allprofiles state")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            bool anyOn = output.Contains("ON", StringComparison.OrdinalIgnoreCase);
            if (anyOn)
            {
                string exePath = Environment.ProcessPath ?? "LeitostrapV7.exe";
                OnLog($"Windows Firewall is active. If the self-test fails, allow \"{Path.GetFileName(exePath)}\" " +
                    "on private and public networks, or temporarily disable third-party antivirus HTTPS scanning.");
            }
        }
        catch { }
    }

    #endregion

    #region Port Proxy Bridge (443 busy fallback)

    private bool AddPortProxy(int targetPort)
    {
        try
        {
            RunNetsh($"interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=443 connectaddress=127.0.0.1 connectport={targetPort}");
            RunNetsh($"interface portproxy add v4tov6 listenaddress=127.0.0.1 listenport=443 connectaddress=::1 connectport={targetPort}");
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"portproxy add failed: {ex.Message}");
            return false;
        }
    }

    private void RemovePortProxy()
    {
        try
        {
            RunNetsh("interface portproxy delete v4tov4 listenaddress=127.0.0.1 listenport=443");
            RunNetsh("interface portproxy delete v4tov6 listenaddress=127.0.0.1 listenport=443");
        }
        catch { }
    }

    private static void RunNetsh(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("netsh", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p != null)
        {
            try { p.WaitForExit(8000); } catch { }
        }
    }

    #endregion

    #region Hosts Management

    private bool SetupHosts()
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                string content = ReadHosts();
                var lines = content
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(l => !l.Contains(HostsMarker, StringComparison.Ordinal))
                    .ToList();
                foreach (var host in SettingsHosts)
                {
                    lines.RemoveAll(l =>
                        !l.TrimStart().StartsWith("#", StringComparison.Ordinal) &&
                        l.Contains(host, StringComparison.OrdinalIgnoreCase) &&
                        (l.Contains("127.0.0.1", StringComparison.Ordinal) ||
                         l.Contains("::1", StringComparison.Ordinal)));
                    lines.Add($"127.0.0.1 {host} {HostsMarker}");
                    lines.Add($"::1 {host} {HostsMarker}");
                }
                if (WriteHosts(string.Join("\n", lines)))
                {
                    OnLog("Hosts entries installed");
                    return true;
                }
                OnLog($"Hosts write attempt {attempt} failed");
            }
            catch (Exception ex)
            {
                OnLog($"Hosts setup error: {ex.Message}");
            }
            Thread.Sleep(300);
        }
        return false;
    }

    private void RemoveHosts()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                string content = ReadHosts();
                if (!content.Contains(HostsMarker, StringComparison.Ordinal))
                {
                    if (attempt == 1) OnLog("Hosts file already clean");
                    return;
                }
                var lines = content
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(l => !l.Contains(HostsMarker, StringComparison.Ordinal))
                    .ToList();
                if (WriteHosts(string.Join("\n", lines)))
                {
                    if (!ReadHosts().Contains(HostsMarker, StringComparison.Ordinal))
                    {
                        OnLog("Hosts file cleaned successfully");
                        return;
                    }
                }
                OnLog($"Hosts cleanup attempt {attempt} failed, retrying...");
            }
            catch (Exception ex)
            {
                OnLog($"Hosts cleanup error: {ex.Message}");
            }
            Thread.Sleep(400);
        }
        OnLog("WARNING: Hosts cleanup may have failed after 5 attempts");
    }

    private static string ReadHosts()
    {
        try { return File.ReadAllText(HostsFile, Encoding.UTF8); }
        catch { return ""; }
    }

    private static bool WriteHosts(string content)
    {
        try
        {
            File.WriteAllText(HostsFile, content, Encoding.UTF8);
            string verify = File.ReadAllText(HostsFile, Encoding.UTF8);
            return Normalize(verify) == Normalize(content);
        }
        catch
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "_leitostrap_hosts.txt");
                File.WriteAllText(tmp, content, Encoding.UTF8);
                var psi = new System.Diagnostics.ProcessStartInfo("cmd",
                    $"/c copy /Y \"{tmp}\" \"{HostsFile}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
                string verify = File.ReadAllText(HostsFile, Encoding.UTF8);
                return Normalize(verify) == Normalize(content);
            }
            catch { return false; }
        }
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n").Trim();
    }

    private void ResolveUpstreamIPs()
    {
        _upstreamIps.Clear();
        foreach (var host in SettingsHosts)
        {
            var ips = ResolveHostRobust(host);
            if (ips.Length > 0)
            {
                _upstreamIps[host] = ips;
                OnLog($"Resolved {host} to {string.Join(", ", ips.Select(i => i.ToString()))}");
            }
            else
            {
                _upstreamIps[host] = Array.Empty<IPAddress>();
                OnLog($"WARNING: all DNS methods failed for {host} - proxy may not reach upstream");
            }
        }
    }

    private static IPAddress[] ResolveHostRobust(string host)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var ips = Dns.GetHostAddresses(host)
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .ToArray();
                if (ips.Length > 0) return ips;
            }
            catch { }
            Thread.Sleep(700 * attempt);
        }

        foreach (var provider in new[]
        {
            "https://dns.google/resolve?name={0}&type=A",
            "https://cloudflare-dns.com/dns-query?name={0}&type=A"
        })
        {
            try
            {
                using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                }) { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.Add("accept", "application/dns-json");

                string url = string.Format(provider, host);
                string json = http.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Answer", out var answers)) continue;

                var resolved = new List<IPAddress>();
                foreach (var a in answers.EnumerateArray())
                {
                    if (a.TryGetProperty("data", out var data) &&
                        IPAddress.TryParse(data.GetString(), out var ip) &&
                        ip.AddressFamily == AddressFamily.InterNetwork)
                        resolved.Add(ip);
                }
                if (resolved.Count > 0)
                {
                    OnLogStatic($"Resolved {host} via DNS-over-HTTPS: {string.Join(", ", resolved)}");
                    return resolved.ToArray();
                }
            }
            catch { }
        }

        return Array.Empty<IPAddress>();
    }

    private static void OnLogStatic(string msg)
    {
        try { Instance.OnLog(msg); } catch { }
    }

    private static void FlushDns()
    {
        try { DnsFlushResolverCache(); } catch { }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(5000);
        }
        catch { }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
                "-NoProfile -WindowStyle Hidden -Command Clear-DnsClientCache")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(5000);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dnsapi.dll")]
    private static extern void DnsFlushResolverCache();

    #endregion

    #region Server Loop

    private void ServerLoop()
    {
        while (IsRunning)
        {
            TcpClient? client = null;
            try
            {
                if (_listener == null) break;
                client = _listener.AcceptTcpClient();
            }
            catch
            {
                if (IsRunning) Thread.Sleep(10);
                continue;
            }

            int id = Interlocked.Increment(ref _counter);
            var thread = new Thread(() => HandleClient(client, id))
            {
                IsBackground = true,
                Name = $"LeitostrapProxyConn{id}"
            };
            try { thread.Start(); }
            catch
            {
                try { client.Close(); } catch { }
            }
        }
    }

    private void HandleClient(TcpClient client, int id)
    {
        SslStream? clientSsl = null;
        TcpClient? upstreamTcp = null;
        SslStream? upstreamSsl = null;
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;

            clientSsl = new SslStream(client.GetStream(), false);
            var sslOptions = new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ServerCertificateSelectionCallback = (sender, hostName) =>
                    SelectCertificate(hostName)
            };
            clientSsl.AuthenticateAsServer(sslOptions);

            while (IsRunning)
            {
                var req = ReadHttpRequest(clientSsl);
                if (req == null) break;

                string host = req.Value.Host;
                string path = req.Value.Path;
                bool keepAlive = req.Value.KeepAlive;
                bool isSettings = IsSettingsHost(host) && IsSettingsPath(path);

                OnLog($">> {req.Value.Method} {host}{path} settings={isSettings} keepAlive={keepAlive}");

                if (isSettings)
                    Interlocked.Increment(ref _settingsRequests);

                if (!isSettings)
                {
                    bool upstreamAlive = TunnelPassthrough(clientSsl, req.Value,
                        ref upstreamTcp, ref upstreamSsl, host);
                    if (!keepAlive || !upstreamAlive) break;
                    continue;
                }

                try
                {
                    upstreamSsl?.Dispose();
                    upstreamTcp?.Dispose();
                }
                catch { }
                upstreamSsl = null;
                upstreamTcp = null;

                upstreamSsl = ConnectUpstream(host, out upstreamTcp);
                if (upstreamSsl == null)
                {
                    SendErrorResponse(clientSsl, 502, "Bad Gateway");
                    break;
                }

                byte[] upstreamRequest = BuildSettingsUpstreamRequest(req.Value);
                try
                {
                    upstreamSsl.Write(upstreamRequest, 0, upstreamRequest.Length);
                    upstreamSsl.Flush();
                }
                catch (Exception ex)
                {
                    OnLog($"Upstream write failed: {ex.Message}");
                    SendErrorResponse(clientSsl, 502, "Bad Gateway");
                    break;
                }

                var resp = ReadHttpResponse(upstreamSsl, allowCloseDelimited: false);
                if (resp == null)
                {
                    OnLog($"Null response from upstream for {host}{path}");
                    SendErrorResponse(clientSsl, 502, "Bad Gateway");
                    break;
                }

                OnLog($"<< {resp.Value.StatusCode} {host}{path} body={resp.Value.Body.Length}");
                var (outBody, outEncoding, injected) = ProcessSettingsBody(req.Value.Path, resp.Value);
                SendResponse(clientSsl, resp.Value, outBody, outEncoding, keepAlive);

                if (!keepAlive) break;
            }
        }
        catch (Exception ex)
        {
            OnLog($"Connection {id} error: {ex.Message}");
        }
        finally
        {
            try { upstreamSsl?.Dispose(); } catch { }
            try { upstreamTcp?.Close(); } catch { }
            try { clientSsl?.Dispose(); } catch { }
            try { client.Close(); } catch { }
        }
    }

    private static bool IsSettingsHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        string normalized = host.Trim().ToLowerInvariant();
        foreach (var h in SettingsHosts)
            if (normalized == h) return true;
        return false;
    }

    private static bool IsSettingsPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string normalized = path.StartsWith('/') ? path : "/" + path;
        if (normalized.Contains("PCClientBootstrapper", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var seq in SettingsPathSeqs)
            if (normalized.Contains(seq, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool TunnelPassthrough(SslStream clientSsl, HttpRequest req,
        ref TcpClient? upstreamTcp, ref SslStream? upstreamSsl, string host)
    {
        try
        {
            if (upstreamSsl == null)
            {
                upstreamSsl = ConnectUpstream(host, out upstreamTcp);
                if (upstreamSsl == null)
                {
                    OnLog($"Passthrough upstream connect failed for {host}");
                    SendErrorResponse(clientSsl, 502, "Bad Gateway");
                    return false;
                }
            }

            byte[] rawHeader = req.RawHeader ?? Array.Empty<byte>();
            byte[] rawBody = req.Body ?? Array.Empty<byte>();
            byte[] payload = new byte[rawHeader.Length + rawBody.Length];
            Buffer.BlockCopy(rawHeader, 0, payload, 0, rawHeader.Length);
            if (rawBody.Length > 0)
                Buffer.BlockCopy(rawBody, 0, payload, rawHeader.Length, rawBody.Length);

            try
            {
                upstreamSsl.Write(payload, 0, payload.Length);
                upstreamSsl.Flush();
            }
            catch
            {
                try { upstreamSsl.Dispose(); upstreamTcp?.Close(); } catch { }
                upstreamSsl = ConnectUpstream(host, out upstreamTcp);
                if (upstreamSsl == null)
                {
                    SendErrorResponse(clientSsl, 502, "Bad Gateway");
                    return false;
                }
                upstreamSsl.Write(payload, 0, payload.Length);
                upstreamSsl.Flush();
            }

            clientSsl.ReadTimeout = 600000;
            clientSsl.WriteTimeout = 600000;
            upstreamSsl.ReadTimeout = 600000;
            upstreamSsl.WriteTimeout = 600000;

            var buffer = new byte[65536];
            while (true)
            {
                int read;
                try { read = upstreamSsl.Read(buffer, 0, buffer.Length); }
                catch { return false; }
                if (read <= 0) return false;
                try
                {
                    clientSsl.Write(buffer, 0, read);
                    clientSsl.Flush();
                }
                catch { return false; }
            }
        }
        catch (Exception ex)
        {
            OnLog($"Passthrough error for {host}: {ex.Message}");
            return false;
        }
        finally
        {
            try { clientSsl.ReadTimeout = 30000; } catch { }
        }
    }

    private SslStream? ConnectUpstream(string host, out TcpClient? tcpClient)
    {
        tcpClient = null;
        try
        {
            IPAddress[] ips;
            if (!_upstreamIps.TryGetValue(host, out var cached) || cached == null || cached.Length == 0)
            {
                ips = ResolveHostRobust(host);
                if (ips.Length > 0)
                    _upstreamIps[host] = ips;
                else
                    return null;
            }
            else
            {
                ips = cached;
            }

            foreach (var ip in ips)
            {
                TcpClient? tcp = null;
                try
                {
                    tcp = new TcpClient();
                    tcp.NoDelay = true;
                    var connectTask = tcp.ConnectAsync(ip, 443);
                    if (!connectTask.Wait(5000))
                    {
                        try { tcp.Close(); } catch { }
                        OnLog($"Upstream connect timeout {host} via {ip} (5s), trying next...");
                        continue;
                    }

                    var ssl = new SslStream(tcp.GetStream(), false, (s, c, ch, e) => true);
                    var handshakeTask = ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    });
                    if (!handshakeTask.Wait(8000))
                    {
                        try { ssl.Dispose(); } catch { }
                        try { tcp.Close(); } catch { }
                        OnLog($"Upstream TLS handshake timeout {host} via {ip} (8s), trying next...");
                        continue;
                    }
                    ssl.ReadTimeout = 45000;
                    ssl.WriteTimeout = 45000;

                    tcpClient = tcp;
                    return ssl;
                }
                catch (Exception ex)
                {
                    try { tcp?.Close(); } catch { }
                    OnLog($"Upstream connect failed {host} via {ip}: {ex.Message}");
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            OnLog($"ConnectUpstream error for {host}: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region HTTP Parsing

    private HttpRequest? ReadHttpRequest(SslStream stream)
    {
        try
        {
            stream.ReadTimeout = 30000;
            stream.WriteTimeout = 30000;

            byte[]? headerBytes = ReadUntilDoubleNewline(stream, 1048576, out byte[] reqLeftover);
            if (headerBytes == null || headerBytes.Length == 0) return null;

            Stream reqBodyStream = reqLeftover.Length > 0 ? new PushbackStream(stream, reqLeftover) : stream;

            string headerStr = Encoding.ASCII.GetString(headerBytes);
            var lines = headerStr.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length < 1) return null;

            var parts = lines[0].Split(' ');
            string method = parts.Length > 0 ? parts[0] : "GET";
            string path = parts.Length > 1 ? parts[1] : "/";

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                    headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            headers.TryGetValue("Host", out string? hostHeader);
            string host = hostHeader ?? "";
            int colonIdx = host.LastIndexOf(':');
            if (colonIdx > 0 && host.IndexOf(':') == colonIdx)
                host = host[..colonIdx];
            host = host.Trim().ToLowerInvariant();

            byte[] body = Array.Empty<byte>();
            if (headers.TryGetValue("Transfer-Encoding", out string? te) &&
                te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                body = ReadChunkedBody(reqBodyStream);
            }
            else if (headers.TryGetValue("Content-Length", out string? clStr) &&
                     long.TryParse(clStr, out long cl) && cl > 0)
            {
                body = ReadExact(stream, (int)Math.Min(cl, 104857600));
            }

            bool keepAlive = true;
            if (headers.TryGetValue("Connection", out string? conn))
                keepAlive = !conn.Contains("close", StringComparison.OrdinalIgnoreCase);

            return new HttpRequest
            {
                Method = method,
                Host = host,
                Path = path,
                Headers = headers,
                Body = body,
                KeepAlive = keepAlive,
                RawHeader = headerBytes
            };
        }
        catch { return null; }
    }

    private HttpResponse? ReadHttpResponse(SslStream stream, bool allowCloseDelimited)
    {
        try
        {
            byte[]? headerBytes = ReadUntilDoubleNewline(stream, 1048576, out byte[] leftover);
            if (headerBytes == null || headerBytes.Length == 0) return null;

            Stream bodyStream = leftover.Length > 0 ? new PushbackStream(stream, leftover) : stream;

            string headerStr = Encoding.ASCII.GetString(headerBytes);
            var lines = headerStr.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length < 1) return null;

            var statusParts = lines[0].Split(new[] { ' ' }, 3);
            int statusCode = 0;
            if (statusParts.Length > 1) int.TryParse(statusParts[1], out statusCode);
            string statusText = statusParts.Length > 2 ? statusParts[2] : "";

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                    headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            bool chunked = headers.TryGetValue("Transfer-Encoding", out string? te) &&
                te.Contains("chunked", StringComparison.OrdinalIgnoreCase);

            long cl = 0;
            bool hasLength = headers.TryGetValue("Content-Length", out string? clStr) &&
                long.TryParse(clStr, out cl);

            bool noBody = statusCode == 204 || statusCode == 304 || (statusCode >= 100 && statusCode < 200);

            byte[] body;
            if (noBody)
            {
                body = Array.Empty<byte>();
            }
            else if (chunked)
            {
                body = ReadChunkedBody(bodyStream);
            }
            else if (hasLength)
            {
                body = cl > 0 ? ReadExact(bodyStream, (int)Math.Min(cl, 104857600)) : Array.Empty<byte>();
                if (body.Length != cl)
                {
                    OnLog($"Upstream response truncated: got {body.Length} of {cl} bytes");
                    return null;
                }
            }
            else if (allowCloseDelimited)
            {
                body = ReadUntilClose(bodyStream);
            }
            else
            {
                body = Array.Empty<byte>();
            }

            return new HttpResponse
            {
                StatusCode = statusCode,
                StatusText = statusText,
                Headers = headers,
                Body = body
            };
        }
        catch { return null; }
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var body = new byte[length];
        int totalRead = 0;
        while (totalRead < length)
        {
            int read;
            try { read = stream.Read(body, totalRead, length - totalRead); }
            catch { break; }
            if (read <= 0) break;
            totalRead += read;
        }
        if (totalRead == length) return body;
        var trimmed = new byte[totalRead];
        Buffer.BlockCopy(body, 0, trimmed, 0, totalRead);
        return trimmed;
    }

    private static byte[] ReadChunkedBody(Stream stream)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            string sizeLine = ReadLineFromStream(stream);
            if (string.IsNullOrEmpty(sizeLine)) break;
            string sizeStr = sizeLine.Split(';')[0].Trim();
            if (!int.TryParse(sizeStr, System.Globalization.NumberStyles.HexNumber, null, out int chunkSize))
                break;
            if (chunkSize == 0)
            {
                ReadLineFromStream(stream);
                break;
            }
            byte[] chunk = ReadExact(stream, chunkSize);
            ms.Write(chunk, 0, chunk.Length);
            ReadLineFromStream(stream);
        }
        return ms.ToArray();
    }

    private static byte[] ReadUntilClose(Stream stream)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[65536];
        while (true)
        {
            int read;
            try { read = stream.Read(buffer, 0, buffer.Length); }
            catch { break; }
            if (read <= 0) break;
            ms.Write(buffer, 0, read);
            if (ms.Length > 524288000) break;
        }
        return ms.ToArray();
    }

    private static byte[]? ReadUntilDoubleNewline(Stream stream, int maxBytes, out byte[] leftover)
    {
        var buf = new MemoryStream();
        byte[] chunk = new byte[8192];
        int crlfCount = 0;
        leftover = Array.Empty<byte>();
        while (buf.Length < maxBytes)
        {
            int read;
            try { read = stream.Read(chunk, 0, chunk.Length); }
            catch { break; }
            if (read <= 0) break;

            for (int i = 0; i < read; i++)
            {
                byte b = chunk[i];
                buf.WriteByte(b);
                if (crlfCount % 2 == 0 ? b == (byte)'\r' : b == (byte)'\n')
                    crlfCount++;
                else
                    crlfCount = b == (byte)'\r' ? 1 : 0;

                if (crlfCount >= 4)
                {
                    int remainingInChunk = read - (i + 1);
                    if (remainingInChunk > 0)
                    {
                        leftover = new byte[remainingInChunk];
                        Buffer.BlockCopy(chunk, i + 1, leftover, 0, remainingInChunk);
                    }
                    return buf.ToArray();
                }
            }
        }
        return buf.Length > 0 ? buf.ToArray() : null;
    }

    private sealed class PushbackStream : Stream
    {
        private readonly Stream _inner;
        private byte[]? _prefix;
        private int _prefixPos;

        public PushbackStream(Stream inner, byte[]? prefix)
        {
            _inner = inner;
            _prefix = prefix != null && prefix.Length > 0 ? prefix : null;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefix != null && _prefixPos < _prefix.Length)
            {
                int n = Math.Min(count, _prefix.Length - _prefixPos);
                Buffer.BlockCopy(_prefix, _prefixPos, buffer, offset, n);
                _prefixPos += n;
                if (_prefixPos >= _prefix.Length) _prefix = null;
                return n;
            }
            return _inner.Read(buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string ReadLineFromStream(Stream stream)
    {
        var sb = new StringBuilder();
        byte[] single = new byte[1];
        while (true)
        {
            int read;
            try { read = stream.Read(single, 0, 1); }
            catch { break; }
            if (read <= 0) break;
            if (single[0] == (byte)'\n') break;
            if (single[0] != (byte)'\r') sb.Append((char)single[0]);
        }
        return sb.ToString();
    }

    #endregion

    #region Request and Response Building

    private byte[] BuildSettingsUpstreamRequest(HttpRequest req)
    {
        var sb = new StringBuilder();
        sb.Append($"{req.Method} {req.Path} HTTP/1.1\r\n");
        sb.Append($"Host: {req.Host}\r\n");
        sb.Append("Accept-Encoding: gzip, deflate\r\n");

        foreach (var kv in req.Headers)
        {
            if (HopByHopHeaders.Contains(kv.Key)) continue;
            if (kv.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            if (ConditionalHeaders.Contains(kv.Key)) continue;
            sb.Append($"{kv.Key}: {kv.Value}\r\n");
        }

        sb.Append($"Content-Length: {req.Body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        byte[] result = new byte[headerBytes.Length + req.Body.Length];
        Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
        if (req.Body.Length > 0)
            Buffer.BlockCopy(req.Body, 0, result, headerBytes.Length, req.Body.Length);
        return result;
    }

    private (byte[] body, string encoding, bool injected) ProcessSettingsBody(string requestPath, HttpResponse resp)
    {
        byte[] body = resp.Body;
        resp.Headers.TryGetValue("Content-Encoding", out string? ceValue);
        string incomingEncoding = ceValue ?? "";

        if (resp.StatusCode < 200 || resp.StatusCode >= 300 || body.Length == 0)
            return (body, "", false);

        string effectiveEncoding = incomingEncoding.ToLowerInvariant();
        if (effectiveEncoding.Length == 0 && body.Length >= 2 && body[0] == 0x1F && body[1] == 0x8B)
            effectiveEncoding = "gzip";
        else if (effectiveEncoding.Length == 0 && body.Length >= 1 && body[0] == 0x78)
            effectiveEncoding = "deflate";

        bool isDcz = incomingEncoding.Contains("dcz", StringComparison.OrdinalIgnoreCase) ||
                     requestPath.Contains(".dcz", StringComparison.OrdinalIgnoreCase);

        byte[] decompressed;
        byte[]? dczDict = null;
        try
        {
            if (isDcz)
            {
                dczDict = GetDczDictionary(requestPath);
                if (dczDict == null)
                {
                    OnLog($"DCZ dictionary unavailable for {requestPath}, passing through");
                    return (body, incomingEncoding, false);
                }
                decompressed = DecompressDcz(body, dczDict) ?? body;
            }
            else
            {
                decompressed = DecompressResponseBody(body, effectiveEncoding);
            }
        }
        catch
        {
            decompressed = body;
        }

        if (decompressed == null || decompressed.Length == 0)
            return (body, incomingEncoding, false);

        byte[]? modified = InjectFlagsIntoBody(decompressed);
        if (modified == null || BytesEqual(modified, decompressed))
        {
            OnLog($"No injection applied to {requestPath} (encoding={incomingEncoding}, decoded={decompressed.Length} bytes)");
            if (isDcz)
                return (decompressed, "dcz", false);
            return (decompressed, "", false);
        }

        resp.Headers.Remove("ETag");
        resp.Headers.Remove("Content-MD5");
        resp.Headers.Remove("x-signature-ed25519");

        if (isDcz)
        {
            byte[]? recompressed = dczDict != null ? CompressDcz(modified, dczDict) : null;
            if (recompressed != null)
            {
                OnLog($"Injected {ActiveFlagsCount:N0} flags into {requestPath} (dcz)");
                Interlocked.Increment(ref _successfulInjections);
                return (recompressed, "dcz", true);
            }
            OnLog($"DCZ recompression failed for {requestPath}, serving identity");
            return (modified, "", true);
        }

        if (effectiveEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[] recompressed = GzipCompress(modified);
                OnLog($"Injected {ActiveFlagsCount} flags into {requestPath}");
                Interlocked.Increment(ref _successfulInjections);
                return (recompressed, "gzip", true);
            }
            catch { }
        }

        if (effectiveEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[] recompressed = DeflateCompress(modified);
                OnLog($"Injected {ActiveFlagsCount} flags into {requestPath}");
                Interlocked.Increment(ref _successfulInjections);
                return (recompressed, "deflate", true);
            }
            catch { }
        }

        OnLog($"Injected {ActiveFlagsCount} flags into {requestPath} (identity)");
        Interlocked.Increment(ref _successfulInjections);
        return (modified, "", true);
    }

    private byte[]? GetDczDictionary(string requestPath)
    {
        var m = System.Text.RegularExpressions.Regex.Match(requestPath,
                @"/([0-9a-f]{64})\.dcz", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string sha = m.Groups[1].Value.ToLowerInvariant();

            if (_dczDictCache.TryGetValue(sha, out var cachedDict))
                return cachedDict;                        byte[]? diskDict = LoadDictFromDisk(sha);
                        if (diskDict != null)
                        {
                            _dczDictCache[sha] = diskDict;
                            OnLog($"DCZ dictionary {sha[..16]}... loaded from disk cache ({diskDict.Length:N0} bytes)");
                            return diskDict;
                        }

                        foreach (var dictHost in SettingsHosts)
            {
                SslStream? upSsl = null;
                TcpClient? upTcp = null;
                try
                {
                    upSsl = ConnectUpstream(dictHost, out upTcp);
                    if (upSsl == null) continue;

                    string dictReq =
                        $"GET /v2/compression-dictionaries/{sha} HTTP/1.1\r\n" +
                        $"Host: {dictHost}\r\n" +
                        "Accept: application/octet-stream\r\n" +
                        "Accept-Encoding: identity\r\n" +
                        "User-Agent: Roblox/WinInet\r\n" +
                        "Connection: close\r\n" +
                        "\r\n";
                    byte[] reqBytes = Encoding.ASCII.GetBytes(dictReq);
                    upSsl.Write(reqBytes, 0, reqBytes.Length);
                    upSsl.Flush();

                    var dictResp = ReadHttpResponse(upSsl, allowCloseDelimited: true);
                    if (dictResp != null && dictResp.Value.StatusCode >= 200 && dictResp.Value.StatusCode < 300 &&
                        dictResp.Value.Body.Length > 0)
                    {
                        byte[] dictBody = dictResp.Value.Body;

                        if (dictResp.Value.Headers.TryGetValue("Content-Length", out string? dictCl) &&
                            long.TryParse(dictCl, out long expected) && expected != dictBody.Length)
                        {
                            OnLog($"DCZ dictionary truncated: got {dictBody.Length} of {expected} bytes, retrying...");
                            continue;
                        }

                        if (dictBody.Length >= 2 && dictBody[0] == 0x1f && dictBody[1] == 0x8b)
                        {
                            OnLog("DCZ dictionary came gzip-compressed, decompressing...");
                            try
                            {
                                using var gz = new System.IO.Compression.GZipStream(
                                    new MemoryStream(dictBody), System.IO.Compression.CompressionMode.Decompress);
                                using var outMs = new MemoryStream();
                                gz.CopyTo(outMs);
                                dictBody = outMs.ToArray();
                            }
                            catch (Exception gex)
                            {
                                OnLog($"DCZ dictionary gzip decompress failed: {gex.Message}");
                                continue;
                            }
                        }

                        bool looksRaw = dictBody.Length > 0 && (dictBody[0] == (byte)'{' ||
                            (dictBody.Length >= 4 && dictBody[0] == 0x28 && dictBody[1] == 0xB5 && dictBody[2] == 0x2F && dictBody[3] == 0xFD));
                        if (!looksRaw)
                        {
                            OnLog($"DCZ dictionary has unexpected format (first byte 0x{dictBody[0]:X2}), rejecting");
                            continue;
                        }

                        _dczDictCache[sha] = dictBody;
                        SaveDictToDisk(sha, dictBody);
                        OnLog($"Fetched DCZ dictionary {sha[..16]}... ({dictBody.Length:N0} bytes, verified)");
                        return dictBody;
                    }
                }
                catch { }
                finally
                {
                    try { upSsl?.Dispose(); } catch { }
                    try { upTcp?.Close(); } catch { }
                }
            }
        return null;
    }

    private static string DictCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leitostrap Injector", "Dictionaries");

    private void SaveDictToDisk(string sha, byte[] dict)
    {
        try
        {
            Directory.CreateDirectory(DictCacheDir);
            File.WriteAllBytes(Path.Combine(DictCacheDir, sha + ".dict"), dict);
        }
        catch { }
    }

    private static byte[]? LoadDictFromDisk(string sha)
    {
        try
        {
            string p = Path.Combine(DictCacheDir, sha + ".dict");
            if (!File.Exists(p)) return null;
            byte[] d = File.ReadAllBytes(p);
            if (d.Length < 100) return null;
            return d;

        }
        catch { return null; }

    }

    private byte[]? DecompressDcz(byte[] body, byte[] dict)
    {
        if (body == null || body.Length == 0 || dict == null || dict.Length == 0)
            return null;

        int[] destSizes =
        {
            4 * 1024 * 1024,
            16 * 1024 * 1024,
            64 * 1024 * 1024,
            256 * 1024 * 1024
        };

        try
        {
            using var decompressor = new ZstdSharp.Decompressor();
            decompressor.LoadDictionary(dict);

            foreach (int size in destSizes)
            {
                var dest = new byte[size];
                if (decompressor.TryUnwrap(body, dest, 0, out int written))
                    return dest.AsSpan(0, written).ToArray();
            }
        }
        catch (Exception ex)
        {
            OnLog($"DCZ decompress failed: {ex.Message}");
        }

        OnLog("DCZ decompress failed with all buffer sizes");
        return null;
    }

    private byte[]? DecompressDcz(byte[] body, string requestPath)
    {
        try
        {
            byte[]? dict = GetDczDictionary(requestPath);
            if (dict == null) return null;
            return DecompressDcz(body, dict);
        }
        catch { return null; }
    }

    private byte[]? CompressDcz(byte[] body, byte[] dict)
    {
        try
        {
            using var compressor = new ZstdSharp.Compressor();
            compressor.LoadDictionary(dict);
            return compressor.Wrap(body).ToArray();
        }
        catch (Exception ex)
        {
            OnLog($"DCZ compress failed: {ex.Message}");
            return null;
        }
    }

    private void SendResponse(SslStream clientStream, HttpResponse resp,
        byte[] body, string bodyEncoding, bool keepAlive)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {resp.StatusCode} {resp.StatusText}\r\n");

        foreach (var kv in resp.Headers)
        {
            if (HopByHopHeaders.Contains(kv.Key)) continue;
            if (kv.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            if (bodyEncoding.Length == 0 && SpoiledHeaders.Contains(kv.Key)) continue;
            sb.Append($"{kv.Key}: {kv.Value}\r\n");
        }

        if (bodyEncoding.Length > 0)
            sb.Append($"Content-Encoding: {bodyEncoding}\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append(keepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");
        sb.Append("\r\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        clientStream.Write(headerBytes, 0, headerBytes.Length);
        if (body.Length > 0)
            clientStream.Write(body, 0, body.Length);
        clientStream.Flush();
    }

    private void SendErrorResponse(SslStream stream, int code, string text)
    {
        try
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes($"{{\"error\":\"{text}\"}}");
            string response =
                $"HTTP/1.1 {code} {text}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Content-Type: application/json\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(response);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }
        catch { }
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    #endregion

    #region Compression

    private static byte[] DecompressResponseBody(byte[] body, string contentEncoding)
    {
        if (body.Length == 0) return body;

        if (contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) ||
            (body.Length >= 2 && body[0] == 0x1F && body[1] == 0x8B))
        {
            try
            {
                using var ms = new MemoryStream(body);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                gz.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch { }
        }

        if (contentEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var ms = new MemoryStream(body);
                using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                ds.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                try
                {
                    using var ms = new MemoryStream(body, 2, body.Length - 2);
                    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch { }
            }
        }

        return body;
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
            gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Fastest))
            ds.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    #endregion

    #region FFlags Injection

    private byte[]? InjectFlagsIntoBody(byte[] decompressedBody)
    {
        try
        {
            var currentFlags = Flags;
            if (currentFlags.Count == 0) return null;
            if (decompressedBody.Length == 0) return null;

            string json = Encoding.UTF8.GetString(decompressedBody);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return null;

            if (!root.TryGetPropertyValue("applicationSettings", out var appNode))
                return null;

            var appSettings = appNode as JsonObject;
            if (appSettings == null) return null;

            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in appSettings.Select(kv => kv.Key))
                existingKeys.Add(key);

            int applied = 0;
            foreach (var kv in currentFlags)
            {
                if (existingKeys.Contains(kv.Key))
                {
                    appSettings[kv.Key] = JsonValue.Create(kv.Value);
                    applied++;
                    continue;
                }

                bool mapped = false;
                foreach (string prefix in FlagPrefixes)
                {
                    string full = prefix + kv.Key;
                    if (existingKeys.Contains(full))
                    {
                        appSettings[full] = JsonValue.Create(kv.Value);
                        applied++;
                        mapped = true;
                        break;
                    }
                }
                if (!mapped)
                {
                    appSettings[kv.Key] = JsonValue.Create(kv.Value);
                    applied++;
                }
            }

            if (applied == 0) return null;

            var opts = new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string modified = root.ToJsonString(opts);
            return Encoding.UTF8.GetBytes(modified);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Flag Cache Priming

    private static string? FindLeitostrapFlagCachePath()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string versionsDir = Path.Combine(appData, "Roblox", "Versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith("version-", StringComparison.Ordinal) &&
                        dirName.Contains("Leitostrap", StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(dir, "flag_cache.dat");
                }
            }

            string? versionExe = ProcessService.Instance.FindRobloxExe();
            if (versionExe == null) return null;
            string? versionDir = Path.GetDirectoryName(versionExe);
            if (versionDir == null) return null;
            return Path.Combine(versionDir, "flag_cache.dat");
        }
        catch { return null; }
    }

    public bool PrimeFlagCache()
    {
        try
        {
            string? cachePath = FindLeitostrapFlagCachePath();
            if (cachePath == null)

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            var currentFlags = Flags;
            using var stream = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var bw = new BinaryWriter(stream);

            byte[] versionBytes = Encoding.UTF8.GetBytes("LeitostrapV7-Proxy");
            bw.Write((byte)0x4C);
            bw.Write((byte)0x56);
            bw.Write((byte)0x07);
            bw.Write(versionBytes.Length);
            bw.Write(versionBytes);

            bw.Write(currentFlags.Count);
            foreach (var kv in currentFlags)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(kv.Key);
                byte[] valBytes = Encoding.UTF8.GetBytes(kv.Value);
                bw.Write(keyBytes.Length);
                bw.Write(keyBytes);
                bw.Write(valBytes.Length);
                bw.Write(valBytes);
            }

            bw.Write(DateTime.UtcNow.ToBinary());
            bw.Write((byte)1);
            bw.Flush();

            OnLog($"Flag cache primed with {currentFlags.Count} flags at {cachePath}");
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"Flag cache prime failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Roblox Version Helpers

    private static string[] GetRobloxVersionDirs()
    {
        try
        {
            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (Directory.Exists(versionsDir))
                return Directory.GetDirectories(versionsDir, "version-*");
        }
        catch { }
        return Array.Empty<string>();
    }

    public static void ClearClientAppSettingsFile()
    {
        try
        {
            foreach (var dir in GetRobloxVersionDirs())
            {
                try
                {
                    string path = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
                    if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    #endregion

    #region Structs

    private struct HttpRequest
    {
        public string Method;
        public string Host;
        public string Path;
        public Dictionary<string, string> Headers;
        public byte[] Body;
        public bool KeepAlive;
        public byte[] RawHeader;
    }

    private struct HttpResponse
    {
        public int StatusCode;
        public string StatusText;
        public Dictionary<string, string> Headers;
        public byte[] Body;
    }

    #endregion
}
