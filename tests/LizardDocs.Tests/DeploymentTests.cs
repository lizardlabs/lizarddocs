using LizardDocs;
using Cli = LizardDocs.Tool.Program;

internal static class DeploymentTests
{
    public static void Run(string temporary, Action<string, Action> test)
    {
        void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        var project = Path.Combine(temporary, "deployment"); SiteInitializer.Create(project, "Deploy Docs");
        var path = Path.Combine(project, DeploymentConfiguration.FileName);
        const string config = "host: ftp.example.org\nremote_path: /public_html/docs\n";
        string? Environment(string name) => name switch { "LIZARDDOCS_FTP_USERNAME" => "test-user", "LIZARDDOCS_FTP_PASSWORD" => "private-test-password", _ => null };
        CheckResult Deploy(RecordingTransport transport, bool dryRun = false, Func<string, string?>? environment = null, Func<string, CheckResult>? validator = null)
            => SiteDeployment.ExecuteAsync(project, null, null, null, dryRun, validator, environment ?? Environment, (_, _) => transport, default).GetAwaiter().GetResult();
        void Reject(string text) {
            File.WriteAllText(path, text);
            try { DeploymentConfiguration.Load(project, null, Environment); } catch (InvalidDataException) { return; }
            throw new Exception("Invalid deployment settings accepted");
        }
        test("private deployment settings default to FTPS support environment destinations and reject unsafe values", () => {
            File.WriteAllText(path, config); var settings = DeploymentConfiguration.Load(project);
            Assert(settings.UseTls && settings.Port == 21 && settings.RemotePath == "/public_html/docs");
            Assert(settings.Credentials(Environment).ToString() == "[deployment credentials redacted]");
            File.WriteAllText(path, "host_env: FTP_HOST\nremote_path_env: FTP_PATH\nprotocol: ftp\nport: 2121\n");
            settings = DeploymentConfiguration.Load(project, null, name => name == "FTP_HOST" ? "localhost" : "/docs/");
            Assert(!settings.UseTls && settings.Port == 2121 && settings.RemotePath == "/docs");
            foreach (var value in new[] { "protocol: sftp", "password: do-not-print-this", "username: user", "port: 0", "port: 65536", "username_env: bad-name", "password_env: ''", "protocol: [ftp]" }) Reject(config + value + "\n");
            foreach (var remote in new[] { "relative", "/docs/../other", "/docs/./other", "/docs//other", "/docs\\other" }) Reject("host: localhost\nremote_path: " + remote + "\n");
            foreach (var host in new[] { "ftp://localhost", "user:secret@localhost", "localhost:21", "localhost/path" }) Reject("host: " + host + "\nremote_path: /docs\n");
            Reject(config + "host_env: FTP_HOST\n"); Reject("host_env: MISSING\nremote_path: /docs\n");
            var publicConfig = Path.Combine(project, "docs/private.yml"); File.WriteAllText(publicConfig, config);
            var rejected = false; try { DeploymentConfiguration.Load(project, publicConfig); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected); File.Delete(publicConfig);
            File.WriteAllText(path, "password: [private-test-password\n");
            try { DeploymentConfiguration.Load(project); throw new Exception("Malformed YAML accepted"); }
            catch (InvalidDataException error) { Assert(!error.Message.Contains("private-test-password")); }
            File.WriteAllText(path, config);
            Assert(File.ReadAllText(Path.Combine(project, ".gitignore")).Contains(DeploymentConfiguration.FileName));
        });
        test("deployment dry run builds a public file plan without credentials or connections", () => {
            var transport = new RecordingTransport(); var result = Deploy(transport, true, _ => throw new Exception("Dry run read credentials"));
            Assert(result.Errors.Count == 0 && result.Summary.Contains("No connection made"));
            Assert(result.Summary.Contains("guide/getting-started.html") && !result.Summary.Contains("lizarddocs.deploy.yml") && !result.Summary.Contains(SiteBuilder.Marker));
            Assert(!transport.Connected && transport.Files.Count == 0);
        });
        test("deployment uploads generated files with temporary replacement and homepage last", () => {
            var transport = new RecordingTransport(); var result = Deploy(transport);
            Assert(result.Errors.Count == 0 && transport.Connected && transport.Disposed);
            Assert(transport.Files.ContainsKey("/public_html/docs/index.html") && transport.Files.ContainsKey("/public_html/docs/search/search_index.js"));
            Assert(transport.Published.Last() == "/public_html/docs/index.html");
            Assert(transport.Published.Any(file => file.EndsWith("guide/getting-started.html", StringComparison.Ordinal)));
            Assert(!transport.Files.Keys.Any(file => file.Contains("lizarddocs-upload-") || file.EndsWith(".md", StringComparison.Ordinal) || file.Contains("deploy.yml") || file.EndsWith(SiteBuilder.Marker, StringComparison.Ordinal)));
            Assert(transport.Files["/public_html/docs/index.html"].SequenceEqual(File.ReadAllBytes(Path.Combine(project, "site/index.html"))));
        });
        test("failed source or project validation and missing secrets prevent connections", () => {
            var transport = new RecordingTransport(); var result = Deploy(transport, validator: _ => new("Project policy", ["Not publishable"]));
            Assert(result.Errors.Contains("Not publishable") && !transport.Connected);
            var source = Path.Combine(project, "docs/index.md"); var original = File.ReadAllText(source);
            try { File.AppendAllText(source, "\n[broken](missing.md)\n"); result = Deploy(transport); Assert(result.Errors.Count > 0 && !transport.Connected); }
            finally { File.WriteAllText(source, original); }
            try { Deploy(transport, environment: _ => null); throw new Exception("Missing secrets accepted"); }
            catch (InvalidDataException) { Assert(!transport.Connected); }
        });
        test("failed and cancelled transfers clean temporary files and redact server replies", () => {
            var transport = new RecordingTransport { Fail = true }; var result = Deploy(transport);
            Assert(result.Errors.Count == 1 && result.Summary == "Deployment failed.");
            Assert(!string.Join(' ', result.Errors).Contains("private-test-password") && !transport.Files.Keys.Any(path => path.Contains("lizarddocs-upload-")));
            Assert(transport.Deleted.Count == 1 && transport.Disposed && transport.Published.Count == 0);
            transport = new RecordingTransport { Cancel = true };
            try { Deploy(transport); throw new Exception("Cancellation swallowed"); }
            catch (OperationCanceledException) { Assert(transport.Deleted.Count == 1 && transport.Disposed); }
        });
        test("deployment CLI supports dry run and rejects deployment flags on local commands", () => {
            var stdout = Console.Out; var stderr = Console.Error;
            try {
                using var output = new StringWriter(); using var error = new StringWriter(); Console.SetOut(output); Console.SetError(error);
                Assert(Cli.Main(["deploy", "--dry-run", "--root", project]) == 0);
                Assert(Cli.Main(["deploy", "--root", project, "--dry-run", "--dry-run"]) == 1);
                Assert(Cli.Main(["build", "--root", project, "--dry-run"]) == 1);
                Assert(Cli.Main(["check", "--root", project, "--deploy-config", path]) == 1);
                Assert(Cli.Main(["deploy", "--root", project, "--archive", "site.zip"]) == 1);
                Assert(Cli.Main(["build", "--root", project]) == 0);
                Assert(output.ToString().Contains("Dry run") && !output.ToString().Contains("private-test-password"));
            } finally { Console.SetOut(stdout); Console.SetError(stderr); }
        });
        FtpIntegrationTests.Run(project, path, test);
    }

    private sealed class RecordingTransport : IDeploymentTransport
    {
        public bool Connected, Disposed, Fail, Cancel;
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> Published { get; } = [];
        public List<string> Deleted { get; } = [];
        public Task Connect(CancellationToken token) { Connected = true; return Task.CompletedTask; }
        public Task Upload(string local, string remote, CancellationToken token) {
            Files[remote] = File.ReadAllBytes(local);
            if (Cancel) throw new OperationCanceledException();
            if (Fail) throw new IOException("Server echoed private-test-password and private host");
            return Task.CompletedTask;
        }
        public Task Replace(string temporary, string destination, CancellationToken token) {
            Files[destination] = Files[temporary]; Files.Remove(temporary); Published.Add(destination); return Task.CompletedTask;
        }
        public Task DeleteTemporary(string path, CancellationToken token) { Deleted.Add(path); Files.Remove(path); return Task.CompletedTask; }
        public void Dispose() { Disposed = true; }
    }
}
