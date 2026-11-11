# FTP and FTPS deployment

`build` and `package` stay local. `deploy` explicitly builds, validates and uploads the generated files to your web host. It uploads the site's contents, not the ZIP, Markdown sources, private settings or the local output-ownership marker.

## Private destination settings

Create `lizarddocs.deploy.yml` beside your site's configuration. This filename is ignored by LizardDocs' repository and newly created projects; add it to your own repository's ignore rules when adopting the tool. Keep private configuration outside the Markdown source and generated output directories.

```yaml
protocol: ftps
host: ftp.example.org
port: 21
remote_path: /public_html/docs
username_env: LIZARDDOCS_FTP_USERNAME
password_env: LIZARDDOCS_FTP_PASSWORD
```

Use the FTP directory reported by your hosting provider, which may differ from the public URL. `ftps` means explicit TLS on port 21 by default; the tool encrypts the control and data connections, checks server certificates and does not fall back to plaintext. Choose `ftp` explicitly only when your host requires unencrypted FTP. SFTP and implicit FTPS are not implemented.

For private destination values stored in environment variables, replace `host` with `host_env` and `remote_path` with `remote_path_env`. Each pair is mutually exclusive. Credentials must always come from the named environment variables: inline username/password configuration is rejected. No credentials are printed or written to generated output, and raw server errors are omitted from failure reports.

You can keep the settings outside the project and select them with `--deploy-config PATH`. Custom filenames need their own ignore rule or should stay outside the checkout.

## Preview and upload

```sh
lizarddocs deploy --root atlas-docs --dry-run
lizarddocs deploy --root atlas-docs
```

The dry run reads destination settings, builds and validates the site, and lists relative paths without opening a connection or reading credential values. Configure the two credential environment variables through your machine's secure environment settings before a real deployment; do not paste passwords into chat, command-line arguments or tracked files.

`deploy` rebuilds before uploading, so failed source or output validation prevents every remote connection. Applications using LizardDocs.Core can pass an output-validation callback to retain their own publication checks. A missing or invalid credential also prevents connecting. Cancellation is supported through Ctrl+C or the API's cancellation token.

## How updates behave

Files upload to unique temporary names in their destination folders, then replace the destination after the transfer completes. Assets and search data are published before HTML, with the homepage last. Unrelated remote files and files absent from the new build are left untouched; this command does not prune old pages, alter server settings or recursively delete a destination.

A whole-site update is not atomic. If an upload fails or is cancelled, files already replaced may be live, and a temporary file can remain if cleanup also fails. The command returns failure and reports the number of completed replacements; rerun it after fixing the cause. Some FTP servers implement overwrite by deleting the old file before renaming the new one, so even an individual replacement is not guaranteed atomic.

## Deploying from a cloud task

The task must have the destination configuration, credential variables and outbound TCP access to the host's FTP control and passive data ports. An HTTPS-only proxy or permission to use GitHub does not establish FTP connectivity. Raw FTP clients also need real environment-based credentials; an HTTP proxy-secret placeholder is not an FTP password. Ask for a build and deploy explicitly when you want publication; a build request alone remains local.

Tests exercise real FTP transfers and FTPS control/data encryption against a .NET loopback server, plus certificate rejection, authentication failures, dry runs and validation. They do not establish compatibility or network access to your particular hosting provider. A live deployment requires your configured destination and credentials.
