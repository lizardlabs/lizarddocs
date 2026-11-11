# Hosting and ZIP files

LizardDocs generates static files. Your production host needs no .NET runtime or build tools.

## Upload the package

Run package, upload the ZIP, and extract its contents into the site's web directory. index.html and the topic folders belong directly in that directory. Keep assets/ and search/ with them.

Configure index.html as the default document. Configure 404.html as your host's missing-page response with HTTP status 404. The generator does not change server configuration. For direct upload instead of extracting a ZIP, configure [FTP/FTPS deployment](deployment.md) and run the explicit deploy command.

## Local browsing

Open index.html directly from disk or use serve for a loopback preview. Search loads a local script instead of fetching JSON, so it can work over file URLs too. Browser policy can still affect clipboard access; the copy control falls back to selecting the code.

## Canonical URLs and subfolders

Set site_url to the public root, including any subfolder. Canonical URLs, social metadata and sitemap entries are derived from it. No modification dates or ranking claims are invented.

If publishing under /atlas/, the host's domain-root robots.txt must point to that subfolder's sitemap. Search and navigation use relative URLs, so the same output can also be reviewed locally.

## Update an existing site

Replace previously generated files with the new package contents so stale pages are removed. Rebuilding locally removes stale files only from a recognized generated output directory. User content and source directories are protected.
