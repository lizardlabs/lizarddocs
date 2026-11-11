using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LizardDocs;

internal static class FtpIntegrationTests
{
    public static void Run(string project, string configuration, Action<string, Action> test)
    {
        void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        CheckResult Deploy(LoopbackFtpServer server, bool tls, bool trustTestCertificate = false, string password = "test-password") {
            File.WriteAllText(configuration, $"host: 127.0.0.1\nport: {server.Port}\nprotocol: {(tls ? "ftps" : "ftp")}\nremote_path: /docs\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            return SiteDeployment.ExecuteAsync(project, null, null, null, false, null,
                name => name.EndsWith("USERNAME", StringComparison.Ordinal) ? "test-user" : password,
                (settings, credentials) => new FtpDeploymentTransport(settings, credentials, client => {
                    client.Config.ConnectTimeout = 3000; client.Config.ReadTimeout = 3000;
                    client.Config.DataConnectionConnectTimeout = 3000; client.Config.DataConnectionReadTimeout = 3000;
                    if (trustTestCertificate) client.ValidateCertificate += (_, e) => e.Accept = e.Certificate?.GetCertHashString() == server.Certificate.GetCertHashString();
                }), timeout.Token).GetAwaiter().GetResult();
        }
        test("real passive FTP publishes byte-exact files replaces existing pages and preserves unrelated remote content", () => {
            using var server = new LoopbackFtpServer(false);
            server.Files["/docs/index.html"] = Encoding.UTF8.GetBytes("old page");
            server.Files["/docs/manual.txt"] = Encoding.UTF8.GetBytes("keep");
            var result = Deploy(server, false);
            Assert(result.Errors.Count == 0, string.Join('\n', result.Errors) + " Commands: " + string.Join(' ', server.Commands));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(project, "site"), "*", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(Path.Combine(project, "site"), file).Replace('\\', '/');
                if (relative == SiteBuilder.Marker) continue;
                Assert(server.Files.TryGetValue("/docs/" + relative, out var bytes) && bytes.SequenceEqual(File.ReadAllBytes(file)), relative);
            }
            Assert(Encoding.UTF8.GetString(server.Files["/docs/manual.txt"]) == "keep");
            Assert(!server.Files.Keys.Any(path => path.Contains("lizarddocs-upload-") || path.EndsWith(SiteBuilder.Marker, StringComparison.Ordinal)));
        });
        test("real FTPS encrypts control and data channels with a specifically trusted loopback certificate", () => {
            using var server = new LoopbackFtpServer(true); var result = Deploy(server, true, true);
            Assert(result.Errors.Count == 0, string.Join('\n', result.Errors) + " Commands: " + string.Join(' ', server.Commands));
            Assert(server.ControlEncrypted && server.EncryptedTransfers > 0);
            Assert(server.Files["/docs/index.html"].SequenceEqual(File.ReadAllBytes(Path.Combine(project, "site/index.html"))));
        });
        test("FTPS rejects an untrusted certificate before credentials and never falls back to plaintext", () => {
            using (var server = new LoopbackFtpServer(true)) {
                Assert(Deploy(server, true).Errors.Count > 0);
                Assert(server.LoginAttempts == 0 && server.Files.Count == 0);
            }
            using (var server = new LoopbackFtpServer(false)) {
                Assert(Deploy(server, true).Errors.Count > 0);
                Assert(server.LoginAttempts == 0 && server.Files.Count == 0);
            }
        });
        test("real FTP authentication rejection returns failure without exposing the password", () => {
            using var server = new LoopbackFtpServer(false); var result = Deploy(server, false, password: "rejected-private-password");
            Assert(result.Errors.Count == 1 && !string.Join(' ', result.Errors).Contains("rejected-private-password"));
            Assert(server.Files.Count == 0);
        });
    }
}

// Test-only FTP/explicit-FTPS peer. It implements the commands used by FluentFTP,
// retains bytes in memory and listens exclusively on loopback. No external server.
internal sealed class LoopbackFtpServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(30));
    private readonly Task service;
    private readonly bool allowTls;
    public X509Certificate2 Certificate { get; }
    public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public ConcurrentQueue<string> Commands { get; } = new();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public bool ControlEncrypted { get; private set; }
    public int EncryptedTransfers { get; private set; }
    public int LoginAttempts { get; private set; }

    public LoopbackFtpServer(bool tls)
    {
        allowTls = tls;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        listener.Start(); service = Serve();
    }

    private async Task Serve()
    {
        TcpListener? passive = null;
        try {
            using var connection = await listener.AcceptTcpClientAsync(shutdown.Token);
            Stream control = connection.GetStream();
            StreamReader Reader() => new(control, Encoding.UTF8, false, 1024, leaveOpen: true);
            StreamWriter Writer() => new(control, new UTF8Encoding(false), 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            var reader = Reader(); var writer = Writer();
            var directories = new HashSet<string>(StringComparer.Ordinal) { "/" };
            var current = "/"; string? rename = null; bool protectedData = false;
            string PathOf(string value) => (value.StartsWith('/') ? value : current.TrimEnd('/') + "/" + value).TrimEnd('/') is { Length: > 0 } path ? path : "/";
            await writer.WriteLineAsync("220 Loopback test FTP");
            while (await reader.ReadLineAsync(shutdown.Token) is { } line) {
                var parts = line.Split(' ', 2); var command = parts[0].ToUpperInvariant(); var argument = parts.Length > 1 ? parts[1] : "";
                Commands.Enqueue(command); // Deliberately omit arguments: PASS must never enter test logs.
                switch (command) {
                    case "AUTH":
                        if (!allowTls) { await writer.WriteLineAsync("502 TLS unsupported"); break; }
                        await writer.WriteLineAsync("234 Start TLS");
                        var secure = new SslStream(control, leaveInnerStreamOpen: false);
                        await secure.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, shutdown.Token);
                        reader.Dispose(); writer.Dispose(); control = secure; reader = Reader(); writer = Writer(); ControlEncrypted = true; break;
                    case "USER": LoginAttempts++; await writer.WriteLineAsync("331 Password required"); break;
                    case "PASS": await writer.WriteLineAsync(argument == "test-password" ? "230 Logged in" : "530 Login rejected"); break;
                    case "SYST": await writer.WriteLineAsync("215 UNIX Type: L8"); break;
                    case "FEAT": await writer.WriteLineAsync("211-Features\r\n UTF8\r\n SIZE\r\n MDTM\r\n EPSV\r\n211 End"); break;
                    case "PWD": await writer.WriteLineAsync($"257 \"{current}\""); break;
                    case "CWD":
                        var directory = PathOf(argument);
                        if (directories.Contains(directory)) { current = directory; await writer.WriteLineAsync("250 Directory changed"); }
                        else await writer.WriteLineAsync("550 Directory missing"); break;
                    case "MKD": directories.Add(PathOf(argument)); await writer.WriteLineAsync("257 Directory created"); break;
                    case "SIZE": await writer.WriteLineAsync(Files.TryGetValue(PathOf(argument), out var bytes) ? "213 " + bytes.Length : "550 File missing"); break;
                    case "MDTM": await writer.WriteLineAsync(Files.ContainsKey(PathOf(argument)) ? "213 20260101000000" : "550 File missing"); break;
                    case "EPSV": case "PASV":
                        passive?.Stop(); passive = new TcpListener(IPAddress.Loopback, 0); passive.Start();
                        var port = ((IPEndPoint)passive.LocalEndpoint).Port;
                        await writer.WriteLineAsync(command == "EPSV" ? $"229 Entering Extended Passive Mode (|||{port}|)" : $"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})"); break;
                    case "PROT": protectedData = argument == "P"; await writer.WriteLineAsync("200 Protection set"); break;
                    case "STOR":
                        if (passive is null) { await writer.WriteLineAsync("425 No data connection"); break; }
                        await writer.WriteLineAsync("150 Opening data connection");
                        using (var data = await passive.AcceptTcpClientAsync(shutdown.Token)) {
                            Stream stream = data.GetStream();
                            if (protectedData) {
                                var encrypted = new SslStream(stream);
                                await encrypted.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Certificate }, shutdown.Token);
                                stream = encrypted; EncryptedTransfers++;
                            }
                            using var content = new MemoryStream(); await stream.CopyToAsync(content, shutdown.Token); Files[PathOf(argument)] = content.ToArray();
                            await stream.DisposeAsync();
                        }
                        passive.Stop(); passive = null; await writer.WriteLineAsync("226 Transfer complete"); break;
                    case "RNFR": rename = PathOf(argument); await writer.WriteLineAsync(Files.ContainsKey(rename) ? "350 Rename destination required" : "550 File missing"); break;
                    case "RNTO":
                        if (rename is not null && Files.TryRemove(rename, out var renamed)) { Files[PathOf(argument)] = renamed; await writer.WriteLineAsync("250 Renamed"); }
                        else await writer.WriteLineAsync("550 File missing"); break;
                    case "DELE": Files.TryRemove(PathOf(argument), out _); await writer.WriteLineAsync("250 Deleted"); break;
                    case "TYPE": case "OPTS": case "PBSZ": case "NOOP": case "CLNT": await writer.WriteLineAsync("200 OK"); break;
                    case "QUIT": await writer.WriteLineAsync("221 Goodbye"); return;
                    default: await writer.WriteLineAsync("502 Unsupported"); break;
                }
            }
        } catch (Exception error) when (error is IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException or SocketException) {
            // Negative tests intentionally disconnect during authentication/TLS.
        } finally { passive?.Stop(); }
    }

    public void Dispose()
    {
        shutdown.Cancel(); listener.Stop(); service.GetAwaiter().GetResult(); Certificate.Dispose(); shutdown.Dispose();
    }
}
