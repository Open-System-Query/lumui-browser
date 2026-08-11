# LUMI Browser

**Reference desktop and terminal browser for LUMUI.**

![LUMI desktop browser](Docs/Images/lumi-desktop-website.png)

LUMI requests validated LUMUI documents directly and renders semantic components as native Avalonia or Terminal.Gui controls. It does not embed an HTML browser engine.

Preview components render their nested component directly in a dedicated native or terminal area. They do not open an embedded viewer or load another surface.

| Terminal browser | Developer tools |
| --- | --- |
| ![LUMI terminal browser](Docs/Images/lumi-cli-website.png) | ![LUMI developer tools](Docs/Images/lumi-desktop-developer-tools.png) |

## Included

- Native desktop and terminal interfaces
- Tabs, history, bookmarks, downloads and credential storage
- Keyboard navigation and configurable accessibility presentation
- Semantic source, request, structure and accessibility inspection
- Image, audio and video presentation
- One `lumi.exe` launcher that selects desktop or terminal mode

## Build and run

LUMI requires Windows and the .NET 10 SDK.

```sh
cd Source
dotnet restore Lumi.sln
dotnet run --project src/Lumui.Browser
dotnet run --project src/Lumui.Cli -- https://lumuiopensource.com/
dotnet run --project src/Lumui.Launcher -- --cli
```

The repository contains separate `Lumi.Desktop.sln` and `Lumi.Cli.sln` entry points, plus `Lumi.sln` for the unified release. A versioned snapshot of the normative LUMUI resources, including the [component specification](Source/resources/lumui/lumui-components.md), is included for deterministic builds; the canonical source is the [LUMUI repository](https://github.com/Open-System-Query/lumui).

## Publish

```sh
cd Source
dotnet publish src/Lumui.Browser/Lumui.Browser.csproj -p:PublishProfile=win-x64-aot
dotnet publish src/Lumui.Cli/Lumui.Cli.csproj -p:PublishProfile=win-x64-aot
```

Both the Avalonia desktop browser in `Lumi.Desktop.sln` and the terminal browser in `Lumi.Cli.sln` publish with Native AOT. Their executable projects enable full trimming, generated JSON metadata and AOT compatibility analysis. Native AOT publishing targets the executable project inside each solution.

## Keyboard

Use `Ctrl+L` for the address, `Ctrl+T` for a new tab, `Ctrl+W` to close a tab, `Ctrl+Tab` to switch tabs, `Alt+Left` and `Alt+Right` for history, `Ctrl+U` for source, `F12` for developer tools and `F1` for terminal help.

## License

LUMI Browser is released under the [MIT License](LICENSE) by [Open System Query](https://opensystemquery.nl/).
