# Commands

The commands below use an installed lizarddocs tool. During source development, use dotnet run --project src/LizardDocs.Tool -- followed by the same command.

| Command | Result |
| --- | --- |
| new PATH --name NAME | Create a small site in a new or empty directory |
| check | Validate configured pages, local links, anchors and assets |
| build | Write the complete static site |
| package | Build, validate and create an uploadable ZIP |
| site-check | Validate existing generated output |
| serve | Serve existing generated output on loopback HTTP |
| deploy | Build, validate and upload the site using private FTP/FTPS settings |

## Common options

Use --root PATH to choose the project. Without it, the tool searches upward from the current directory for lizarddocs.yml or documentation.yml.

Use --output PATH with build, package, site-check, serve or deploy. Relative output, archive and explicit deployment-configuration paths are resolved from the caller's working directory. Use --archive PATH with package to choose a separate ZIP path. The default archive is PROJECT/artifacts/lizarddocs-site.zip.

Use --site-url https://example.org/docs/ with build, package or deploy to override the canonical publication URL. An empty value suppresses canonical URLs, sitemap and robots output. Use --port NUMBER with serve; 8080 is the default and 0 selects an available local port.

`deploy --dry-run` builds and validates the site, then lists public upload paths without connecting or reading credential values. `--deploy-config PATH` chooses a private settings file instead of PROJECT/lizarddocs.deploy.yml. Both options require deploy. See [deployment](deployment.md) for configuration, credential handling and update limitations. Ctrl+C cancels deployment with exit code 130; ordinary failures return 1.

```sh
lizarddocs package --root atlas-docs --site-url https://example.org/atlas/
lizarddocs serve --root atlas-docs --port 8080
```

## Failure and output safety

Success returns exit code 0; invalid input, validation failure or an I/O failure returns 1. Failed checks do not report a successful build. Failed source/output validation preserves previous generated output and ZIP files. Builds use staging directories and ZIP entries have deterministic timestamps.
