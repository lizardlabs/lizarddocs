using FluentFTP;

namespace LizardDocs;

internal interface IDeploymentTransport : IDisposable
{
    Task Connect(CancellationToken token);
    Task Upload(string local, string remote, CancellationToken token);
    Task Replace(string temporary, string destination, CancellationToken token);
    Task DeleteTemporary(string path, CancellationToken token);
}

internal sealed class FtpDeploymentTransport : IDeploymentTransport
{
    private readonly AsyncFtpClient client;
    internal FtpDeploymentTransport(DeploymentConfiguration config, DeploymentCredentials credentials, Action<AsyncFtpClient>? configure = null)
    {
        client = new(config.Host, credentials.Username, credentials.Password, config.Port, new FtpConfig {
            EncryptionMode = config.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
            DataConnectionEncryption = config.UseTls, ValidateAnyCertificate = false,
            DataConnectionType = FtpDataConnectionType.AutoPassive,
            ConnectTimeout = 15000, ReadTimeout = 30000, DataConnectionConnectTimeout = 15000, DataConnectionReadTimeout = 30000,
            LogToConsole = false, LogHost = false, LogUserName = false, LogPassword = false
        });
        // Tests may trust their own loopback certificate; no insecure TLS option is exposed to users.
        configure?.Invoke(client);
    }
    public Task Connect(CancellationToken token) => client.Connect(token);
    public async Task Upload(string local, string remote, CancellationToken token)
    {
        if (await client.UploadFile(local, remote, FtpRemoteExists.NoCheck, true, FtpVerify.None, token: token) != FtpStatus.Success)
            throw new IOException("FTP upload did not complete");
    }
    public async Task Replace(string temporary, string destination, CancellationToken token)
    {
        if (!await client.MoveFile(temporary, destination, FtpRemoteExists.Overwrite, token)) throw new IOException("FTP file replacement did not complete");
    }
    public Task DeleteTemporary(string path, CancellationToken token) => client.DeleteFile(path, token);
    public void Dispose() => client.Dispose();
}
