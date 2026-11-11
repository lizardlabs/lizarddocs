namespace LizardDocs;

public static class SiteDeployment
{
    public static Task<CheckResult> DeployAsync(string root, string? output = null, string? configuration = null, string? siteUrl = null,
        bool dryRun = false, Func<string, CheckResult>? outputValidator = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(root, output, configuration, siteUrl, dryRun, outputValidator, Environment.GetEnvironmentVariable,
            (settings, credentials) => new FtpDeploymentTransport(settings, credentials), cancellationToken);

    internal static async Task<CheckResult> ExecuteAsync(string root, string? output, string? configuration, string? siteUrl, bool dryRun,
        Func<string, CheckResult>? outputValidator, Func<string, string?> environment,
        Func<DeploymentConfiguration, DeploymentCredentials, IDeploymentTransport> createTransport, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); root = Data.Resolve(root);
        var settings = DeploymentConfiguration.Load(root, configuration, environment);
        output = Data.Resolve(output ?? Path.Combine(root, SiteConfiguration.Load(root).Output));
        var privateConfig = Data.Resolve(configuration ?? Path.Combine(root, DeploymentConfiguration.FileName));
        if (Data.Within(output, privateConfig)) throw new InvalidDataException("Deployment configuration overlaps generated output");
        var built = SiteBuilder.Build(root, output, siteUrl, outputValidator);
        if (built.Errors.Count > 0) return built;
        // Deploy only the freshly generated public site, never source, ZIPs, private settings or the local ownership marker.
        var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => (Local: path, Relative: Path.GetRelativePath(output, path).Replace('\\', '/')))
            .Where(file => file.Relative != SiteBuilder.Marker)
            .OrderBy(file => file.Relative == "index.html" ? 2 : file.Relative.EndsWith(".html", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(file => file.Relative, StringComparer.Ordinal).ToArray();
        foreach (var file in files)
            if (SitePaths.Internal(file.Relative) || !Data.Within(output, Data.Resolve(file.Local))) throw new InvalidDataException("Deployment file leaves the public generated site");
        if (dryRun) return new($"Dry run: {files.Length} generated files ready for {(settings.UseTls ? "FTPS" : "FTP")} deployment. No connection made; credentials not read.\n" + string.Join('\n', files.Select(file => file.Relative)), []);
        var credentials = settings.Credentials(environment);
        var uploaded = 0;
        try
        {
            using var transport = createTransport(settings, credentials);
            await transport.Connect(token);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var destination = settings.RemotePath.TrimEnd('/') + "/" + file.Relative;
                var directory = destination[..(destination.LastIndexOf('/') + 1)];
                var temporary = directory + ".lizarddocs-upload-" + Guid.NewGuid().ToString("N");
                try {
                    await transport.Upload(file.Local, temporary, token);
                    await transport.Replace(temporary, destination, token);
                    uploaded++;
                } catch {
                    try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await transport.DeleteTemporary(temporary, cleanup.Token); } catch { /* Preserve the original failure; never expose server replies. */ }
                    throw;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) {
            // Server replies and exception messages can contain credentials or private destination details.
            return new("Deployment failed.", [$"FTP/FTPS deployment failed after {uploaded} of {files.Length} files. Check credentials, certificate, destination permissions and network access. Previously replaced files may already be live; rerun deploy after fixing the failure."]);
        }
        return new($"Deployed {uploaded} validated files using {(settings.UseTls ? "FTPS" : "FTP")}. Remote files absent from this build were left untouched.", []);
    }
}
