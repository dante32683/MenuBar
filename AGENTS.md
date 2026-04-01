# Repository Guidelines

## Project Structure & Module Organization
This repository is a WinUI 3 desktop app targeting `.NET 8` on Windows.

- `App.xaml`, `App.xaml.cs`: app bootstrap.
- `MainWindow.xaml`, `MainWindow.xaml.cs`: primary UI, flyouts, app bar behavior, and most interaction logic.
- `Services/`: platform and domain services such as media, battery, settings, Win32 interop, and virtual desktop integration.
- `ViewModels/`: UI state models, primarily `MainViewModel.cs`.
- `settings.json`: runtime configuration copied to the output directory.
- `publish/`: published build output. Treat this as generated output, not source.

There is no dedicated test project in the current tree.

## Build, Test, and Development Commands
- `dotnet build MenuBar.csproj`: build the app and validate XAML/C# changes.
- `dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish`: publish the runnable build to `publish/`.
- Run locally from `publish/MenuBar.exe` after publish.

Expected workflow for changes:
1. Make one logical phase of changes.
2. Review the diff.
3. Run `dotnet build`.
4. Fix errors before continuing.
5. Publish to `publish/` when the task is complete.

## Coding Style & Naming Conventions
- Use 4-space indentation in C# and XAML.
- Keep WinUI UI definitions in XAML and behavior in code-behind or `Services/`.
- Use `PascalCase` for types, methods, properties, and XAML element names.
- Use `camelCase` for local variables and private method parameters.
- Private fields use leading underscore, for example `_mediaService`.
- Reuse existing theme resources and WinUI controls before introducing custom styling.

## Testing Guidelines
There is no automated test suite yet. Validation is manual:
- Build with `dotnet build`.
- Verify affected flyouts, settings toggles, and layout changes in the published app.
- For settings work, confirm `settings.json` defaults, loading, and visibility bindings.

## Commit & Pull Request Guidelines
Recent commit messages are short and direct, for example `bug fixes and battery icon changes` and `consistency stuff`.

- Keep commit subjects brief, imperative, and specific.
- Group related UI/config changes into a single commit.
- PRs should include:
  - a short summary of behavior changes
  - affected files or areas
  - screenshots or short recordings for UI changes
  - build status (`dotnet build` / publish result)

## Configuration Notes
- `bar_height` currently supports `26–56`.
- Keep `settings.json` help text in sync with `MenuBarSettings`.
- Avoid editing generated folders such as `bin/`, `obj/`, or `publish/` by hand.
