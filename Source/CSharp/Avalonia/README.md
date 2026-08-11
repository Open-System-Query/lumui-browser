# C# Avalonia Reference Browser

This implementation renders LUMUI surfaces as native Avalonia controls. It provides a graphical desktop browser with tabs, navigation, settings, media presentation and developer tools.

## Build and run

From the repository `Source` directory:

```sh
dotnet restore CSharp/Avalonia/Lumui.Avalonia.sln
dotnet run --project CSharp/Avalonia/Lumui.Browser -- https://lumuiopensource.com/
```

## Publish with Native AOT

```sh
dotnet publish CSharp/Avalonia/Lumui.Browser/Lumui.Browser.csproj -p:PublishProfile=win-x64-aot
```

The solution references the shared C# protocol client in `../Shared/Lumui.Client`.
