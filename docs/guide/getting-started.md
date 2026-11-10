# Getting started

LizardDocs uses the .NET SDK to build and package your documentation. The generated site is ordinary HTML, CSS and JavaScript.

## Build from source

```sh
dotnet build src/LizardDocs.Tool
dotnet run --project src/LizardDocs.Tool -- new ../atlas-docs --name "Atlas Docs"
dotnet run --project src/LizardDocs.Tool -- package --root ../atlas-docs
```

The package is created at atlas-docs/artifacts/lizarddocs-site.zip. Extract it into your web directory, keeping its folders intact.

## Install the local .NET tool package

From the LizardDocs repository:

```sh
dotnet pack src/LizardDocs.Tool -c Release -o artifacts/packages
dotnet tool install LizardLabs.LizardDocs --version 0.1.1 --tool-path artifacts/tools --add-source artifacts/packages
```

The tool executable is artifacts/tools/lizarddocs (lizarddocs.exe on Windows). Add that directory to PATH or invoke it directly. A public NuGet publication is not required for a local installation.

```sh
lizarddocs new atlas-docs --name "Atlas Docs"
lizarddocs package --root atlas-docs
```

## Update the pages

Edit Markdown under the configured source directory. Add each published page to nav in lizarddocs.yml, then rebuild. Only navigation pages and their public assets are published; unlisted Markdown drafts are not silently included.

Use local assets and relative Markdown links. A link to ../configuration.md becomes a link to the corresponding HTML page. Keep assets/ beside the Markdown for images and custom CSS.

[Configure another project](../configuration.md).
