using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace LizardDocs;

public sealed record DeploymentConfiguration(string Host, int Port, string RemotePath, bool UseTls, string UsernameEnvironment, string PasswordEnvironment)
{
    public const string FileName = "lizarddocs.deploy.yml";
    public static DeploymentConfiguration Load(string root, string? path = null) => Load(root, path, Environment.GetEnvironmentVariable);

    internal static DeploymentConfiguration Load(string root, string? path, Func<string, string?> environment)
    {
        root = Data.Resolve(root); path = Data.Resolve(path ?? Path.Combine(root, FileName));
        var site = SiteConfiguration.Load(root);
        if (Data.Within(site.SourcePath(root), path) || Data.Within(Data.Resolve(Path.Combine(root, site.Output)), path))
            throw new InvalidDataException("Deployment configuration must stay outside documentation source and generated output");
        if (!File.Exists(path)) throw new InvalidDataException("Missing private deployment configuration. Create lizarddocs.deploy.yml or use --deploy-config PATH; see the deployment guide.");
        YamlMappingNode mapping;
        try {
            var yaml = new YamlStream(); yaml.Load(new StringReader(Data.Read(path)));
            mapping = yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode node ? node : throw new InvalidDataException("Expected one deployment configuration mapping");
        } catch (YamlDotNet.Core.YamlException) { throw new InvalidDataException("Invalid deployment configuration; check YAML formatting."); }
        var allowed = new HashSet<string>(["host", "host_env", "port", "protocol", "remote_path", "remote_path_env", "username_env", "password_env"], StringComparer.Ordinal);
        if (mapping.Children.Keys.Any(key => key is not YamlScalarNode { Value: { } name } || !allowed.Contains(name)))
            throw new InvalidDataException("Unknown deployment setting. Credentials must be supplied through environment variables, not configuration values.");
        string Field(string key, string fallback = "") => !mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? fallback
            : value is YamlScalarNode { Value: { } text } ? text : throw new InvalidDataException("Deployment settings must be scalar values");
        string EnvironmentName(string value)
        {
            if (!Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z")) throw new InvalidDataException("Invalid deployment environment-variable name");
            return value;
        }
        string Destination(string key)
        {
            var value = Field(key); var variable = Field(key + "_env");
            if (variable.Length == 0) return value;
            if (value.Length > 0) throw new InvalidDataException("Choose either a deployment value or its environment variable");
            return environment(EnvironmentName(variable)) ?? throw new InvalidDataException("Required deployment destination environment variable is not set");
        }
        var host = Destination("host");
        if (host.Length == 0 || host.Any(char.IsControl) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidDataException("Deployment host must be a hostname or IP address without scheme, port, path or credentials");
        var protocol = Field("protocol", "ftps");
        if (protocol is not ("ftps" or "ftp")) throw new InvalidDataException("Deployment protocol must be ftps (explicit TLS) or ftp");
        if (!int.TryParse(Field("port", "21"), out var port) || port is < 1 or > 65535) throw new InvalidDataException("Deployment port must be between 1 and 65535");
        var remote = Destination("remote_path");
        if (!remote.StartsWith('/') || remote.Contains('\\') || remote.Any(char.IsControl) || remote.Split('/').Any(part => part is "." or "..") || remote.Contains("//", StringComparison.Ordinal))
            throw new InvalidDataException("Deployment remote_path must be an absolute FTP directory without traversal or control characters");
        remote = remote.TrimEnd('/'); if (remote.Length == 0) remote = "/";
        return new(host, port, remote, protocol == "ftps", EnvironmentName(Field("username_env", "LIZARDDOCS_FTP_USERNAME")), EnvironmentName(Field("password_env", "LIZARDDOCS_FTP_PASSWORD")));
    }

    internal DeploymentCredentials Credentials(Func<string, string?> environment)
    {
        string Required(string name) {
            var value = environment(name);
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new InvalidDataException("Required deployment credential environment variable is missing or invalid: " + name);
            return value;
        }
        return new(Required(UsernameEnvironment), Required(PasswordEnvironment));
    }
}

internal sealed class DeploymentCredentials(string username, string password)
{
    internal string Username { get; } = username;
    internal string Password { get; } = password;
    public override string ToString() => "[deployment credentials redacted]";
}
