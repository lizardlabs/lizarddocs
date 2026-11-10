# LizardDocs

**Markdown in. A complete static documentation site out. Built with .NET.**

LizardDocs turns Markdown into a static website with navigation, on-page contents,
ranked search, readable code examples and an uploadable ZIP. Give each project its
own name, logo, colours, navigation and footer. No Python, Node, CSS build chain,
CDN or server-side .NET process is needed to host the output.

Created by [Lizard Labs Software](https://lizard-labs.com/).
See a generated site: [DirectSharp documentation](https://direct-sharp.lizard-labs.com/).

## A small site

Create a documentation project with the CLI:

```sh
lizarddocs new atlas-docs --name "Atlas Docs"
lizarddocs package --root atlas-docs
```

Upload `atlas-docs/artifacts/lizarddocs-site.zip` and extract its contents into your
web directory. You can also open the generated `index.html` from disk: search
uses a local script and does not depend on fetching JSON over HTTP.

A site's `lizarddocs.yml` controls the presentation:

```yaml
site_name: Atlas Docs
site_description: Guides for the Atlas API.
site_url: https://example.org/atlas/
brand_mark: A
brand_label: Documentation
theme_color: '#3757b5'
tagline: Clear guides for the Atlas platform.
nav:
  - Start:
      - Overview: index.md
      - Quick start: guide/quick-start.md
```

Put the pages under `docs/`, add them to navigation, and rebuild. A topic such as
`guide/quick-start.md` becomes `guide/quick-start.html`; only the homepage uses
`index.html`. Unlisted Markdown drafts are not silently published.

## What is included

- Collapsible navigation, breadcrumbs, previous/next links and page contents.
- Search ranked by titles, headings and body text, with highlighted results and
  direct section links. Literal operators such as `??` are searchable too.
- Code-copy controls and deliberately minimal, non-semantic highlighting.
- A responsive local theme with dark mode, optional logos and custom CSS/scripts.
- A discreet system/light/dark switch, an optional favicon, configurable footer
  and source links, and an option to open external links in new tabs.
- Optional Cookie Consent integration and GA4 analytics loaded only after consent.
- Canonical URLs, descriptions, social metadata, sitemap and robots output.
- Source/link/asset validation, output guards, atomic replacement and deterministic
  ZIP packaging. Failed validation preserves previous output.
- A loopback preview server for reviewing generated files over HTTP.

## Build and install

Source builds use the .NET 8 SDK selected in `global.json`:

```sh
dotnet restore tests/LizardDocs.Tests --locked-mode
dotnet run --no-restore --project tests/LizardDocs.Tests
dotnet pack src/LizardDocs.Tool -c Release -o artifacts/packages
dotnet tool install LizardLabs.LizardDocs --version 0.1.1 --tool-path artifacts/tools --add-source artifacts/packages
```

Invoke `artifacts/tools/lizarddocs` (`lizarddocs.exe` on Windows), or add that
directory to PATH. The package is built locally; no public NuGet release is
claimed. During development, use `dotnet run --project src/LizardDocs.Tool --`
followed by the same command.

[Getting started](docs/guide/getting-started.md) ·
[Configuration](docs/configuration.md) ·
[Commands](docs/commands.md) ·
[Hosting](docs/hosting.md)

## Reuse it across projects

Each project has its own configuration. `examples/research-notes/` demonstrates a
second site with different branding. The `LizardLabs.LizardDocs.Core` package also
exposes the builder, validator, packager and preview server to C# applications.
An optional output-validation callback runs before publication, so a project can
retain extra checks without forking the renderer or theme.

LizardDocs was extracted from DirectSharp's documentation builder. DirectSharp's
language design, specification, compiler, source-history policies and licensing
are not included or changed. No project-specific chapter counts, naming rules or
editorial restrictions are imposed on other sites.

## License

First-party code is [MIT licensed](LICENSE). Dependencies retain their own
licenses; their full notices are preserved in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
and `third-party/`. Runtime dependencies are Markdig, YamlDotNet and AngleSharp.
Managed Jint tests execute the shipped search functions without Node; this does
not claim full-browser visual, clipboard or accessibility verification.

GitHub Actions runs only through `workflow_dispatch`. Building, packaging and
previewing do not deploy a site or publish a package.
