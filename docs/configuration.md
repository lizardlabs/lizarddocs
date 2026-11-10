# Configuration

Each project has its own lizarddocs.yml. The older documentation.yml filename is also accepted for migration. Neither filename implies DirectSharp-specific rules.

## Names and appearance

```yaml
site_name: Atlas Docs
site_description: Guides for the Atlas API.
site_url: https://example.org/atlas/
docs_dir: docs
site_dir: site
brand_mark: A
brand_label: Documentation
logo: assets/atlas.svg
theme_color: '#3757b5'
tagline: Clear guides for the Atlas platform.
sidebar_note: API guides and integration examples.
language: en
```

Only site_name and nav are required. docs_dir defaults to docs and site_dir to site. The source must stay inside the project root. Output must remain separate from source and cannot replace an unrecognized existing directory. theme_color uses six hexadecimal digits. A local logo is optional; HTTP(S) logo URLs are also accepted.

## Navigation and home cards

```yaml
nav:
  - Start:
      - Overview: index.md
      - Quick start: guide/quick-start.md
  - Reference:
      - API: api.md
home_cards:
  - title: Quick start
    description: Follow one complete request.
    page: guide/quick-start.md
```

Every published page needs one navigation entry. The homepage is index.md. Home cards are optional and must link to published pages. Topic folders are preserved: guide/quick-start.md becomes guide/quick-start.html. A section landing page guide/index.md becomes guide.html; only the root homepage uses index.html.

## Repository and footer

```yaml
repo_url: https://github.com/example/atlas
repository_label: Source
edit_uri: edit/main/docs/
footer_links:
  - Company: https://example.org/
  - Privacy: https://example.org/privacy/
extra_css:
  - assets/custom.css
extra_javascript:
  - assets/custom.js
```

The embedded theme always supplies its own search and presentation assets. Extra assets are local and copied with the site. Footer and repository URLs must be HTTP(S) URLs without credentials. LizardDocs supplies no legal policies, cookie banner or consent provider; link or integrate the ones appropriate to your project.

## Adopt trusted earlier generated output

```yaml
legacy_marker: .previous-generator.json
legacy_generator: PreviousGenerator
```

Use these only when intentionally migrating an output directory or ZIP that carries that exact ownership marker. They do not authorize replacing arbitrary directories. New builds always write the LizardDocs marker.
