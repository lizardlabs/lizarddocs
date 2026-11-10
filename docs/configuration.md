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
favicon: favicon.svg
theme_color: '#3757b5'
theme_switcher: true
tagline: Clear guides for the Atlas platform.
sidebar_note: API guides and integration examples.
language: en
```

Only site_name and nav are required. docs_dir defaults to docs and site_dir to site. The source must stay inside the project root. Output must remain separate from source and cannot replace an unrecognized existing directory. theme_color uses six hexadecimal digits. A local logo is optional; HTTP(S) logo URLs are also accepted.

`favicon` is an optional path relative to the Markdown source directory; SVG works without an image build step. The small header theme button cycles system, light and dark, remembering the choice locally. System is the default; without JavaScript the CSS still follows the operating system. Set `theme_switcher: false` to hide the control. Code colours use distinct Visual Studio-style blue keywords, red strings and green comments in light mode, with corresponding brighter dark-mode colours.

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
show_source_link: false
external_links_new_tab: true
footer_links:
  - Company: https://example.org/
  - Privacy: https://example.org/privacy/
extra_css:
  - assets/custom.css
extra_javascript:
  - assets/custom.js
```

The embedded theme always supplies its own search and presentation assets. Extra assets are local and copied with the site. Footer and repository URLs must be HTTP(S) URLs without credentials. Omit `footer_links` to omit that footer. LizardDocs supplies no legal policies: link to your own.

`show_source_link` defaults to true, but a source link appears only when both `repo_url` and `edit_uri` are set. Set it to false to remove “View Markdown” while keeping the header repository link. `external_links_new_tab` defaults to false. When true, HTTP(S) links to other origins open with `target="_blank"` and `rel="noopener noreferrer"`; relative links, fragments, email links and absolute links to `site_url`'s origin retain their normal navigation. Existing `rel` tokens are preserved. The option applies to content, header and footer links, including the 404 page.

## Cookie consent and optional analytics

```yaml
google_analytics_id: ''
cookie_consent:
  enabled: true
  privacy_url: https://example.org/privacy/
  popup_color: '#d86016'
  button_color: '#102144'
```

Both integrations are off by default. Enabling cookie consent adds Cookie Consent 3.0.3 CSS and JavaScript from cdnjs to each page's head, with the classic bottom-left notice and a control to revisit the choice. Its legal-policy link uses `privacy_url`. These are optional external assets; reading pages, navigation and search still work without them.

Leave `google_analytics_id` empty to make no Google Analytics requests. To enable GA4, set a `G-…` measurement ID and enable cookie consent. The notice then offers allow/decline choices. The analytics script loads only after an explicit or previously saved allowance; a blocked consent library leaves analytics unloaded. Revoking the choice disables collection and updates consent. Advertising storage and personalisation remain denied. This integration records a visitor's choice; it does not supply privacy or legal-policy text.

## Adopt trusted earlier generated output

```yaml
legacy_marker: .previous-generator.json
legacy_generator: PreviousGenerator
```

Use these only when intentionally migrating an output directory or ZIP that carries that exact ownership marker. They do not authorize replacing arbitrary directories. New builds always write the LizardDocs marker.
