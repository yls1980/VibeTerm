# Repository Guidelines

## Project Structure & Module Organization

VibeTerm is a Windows-only WPF terminal manager built on .NET 7. The main app lives in `VibeTerm/`, with XAML views in `Views/`, view models in `ViewModels/`, terminal/session logic in `Terminal/`, ConPTY interop in `ConPTY/`, logging in `Logging/`, settings in `Settings/`, and WebView terminal assets in `Resources/terminal.html`. Tests live in `VibeTerm.Tests/` and reference the app project through `VibeTerm.sln`.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore .\VibeTerm.sln
dotnet build .\VibeTerm.sln --configuration Release
dotnet test .\VibeTerm.sln --configuration Release
dotnet run --project .\VibeTerm\VibeTerm.csproj
dotnet publish .\VibeTerm\VibeTerm.csproj -c Release -r win-x64 --self-contained false
```

`restore` downloads NuGet packages, `build` compiles the app and tests, `test` runs the xUnit suite, `run` starts the WPF app locally, and `publish` creates a framework-dependent Windows x64 build.

## Coding Style & Naming Conventions

Use C# with nullable reference types and implicit usings enabled. Follow the existing style: file-scoped namespaces, 4-space indentation, PascalCase for public types and members, camelCase for locals and parameters, and `_camelCase` for private fields. Keep WPF UI behavior in view models or services where practical, and keep XAML code-behind focused on view-specific wiring.

## Testing Guidelines

The test project uses xUnit with `Microsoft.NET.Test.Sdk` and `coverlet.collector`. Place unit tests in `VibeTerm.Tests/`, name test files after the subject under test, and use descriptive method names such as `EnabledModeMasksSensitiveValues`. Prefer `[Theory]` with `[InlineData]` for input/output cases and `[Fact]` for single behavior checks. Run `dotnet test .\VibeTerm.sln` before submitting changes.

## Commit & Pull Request Guidelines

The current history has only `Initial commit`, so no strict commit convention is established. Use short, imperative commit subjects such as `Add terminal logging tests` or `Fix ConPTY resize handling`. Pull requests should describe the change, list validation commands, link related issues when available, and include screenshots or short screen recordings for visible WPF UI changes.

## Security & Configuration Tips

Do not commit generated output or local runtime data such as `bin/`, `obj/`, `publish/`, `TestResults/`, `logs/`, `settings.json`, or `WebView2Cache/`. Terminal logs can contain secrets; review or redact them before sharing outside the repository.
