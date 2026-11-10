# LizardDocs

Build and test first-party code with C#/.NET only. Browser JavaScript is a shipped
site asset; managed Jint tests execute its search functions without Node.

LizardDocs is a general-purpose static documentation engine. Keep product-specific
language rules, branding, source-preservation policies and private documentation
out of the core. Branding belongs in lizarddocs.yml. Never publish repository
credentials, generated build output, private project content or internal history.

Preserve the MIT license and third-party notices. GitHub Actions must remain
manual-only. Do not publish packages or deploy sites without authorization.

Run dotnet run --project tests/LizardDocs.Tests, then verify tool packing and a
site package. Protect source directories and existing unowned output; preserve
atomic replacement, deterministic packaging, local-file search and named HTML
routes. Passing managed JavaScript tests does not establish full-browser behavior.
