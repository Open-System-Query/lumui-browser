# C# Terminal.Gui Reference Browser

This implementation renders LUMUI surfaces as Terminal.Gui controls. It provides a keyboard-first browser with tabs, navigation, settings, terminal media presentation and developer tools.

## Build and run

From the repository `Source` directory:

```sh
dotnet restore CSharp/TerminalGui/Lumui.TerminalGui.sln
dotnet run --project CSharp/TerminalGui/Lumui.Cli -- https://lumuiopensource.com/
```

## Publish with Native AOT

```sh
dotnet publish CSharp/TerminalGui/Lumui.Cli/Lumui.Cli.csproj -p:PublishProfile=win-x64-aot
```

The solution references the shared C# protocol client in `../Shared/Lumui.Client`.
