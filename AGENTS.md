# Repository Guidelines

WinUI 3 desktop app targeting `.NET 8`.

## Project layout (high level)
- `MainWindow.xaml(.cs)`: UI + AppBar behavior
- `Services/`: platform/domain services (media, battery, settings, Win32, virtual desktop)
- `ViewModels/`: UI state (`MainViewModel.cs`)
- `settings.json`: copied to output
- `publish/`: generated build output (don’t edit/commit)

No test project; validation is manual.

## Build / publish
- Build: `dotnet build MenuBar.csproj`
- Publish: `dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish --no-self-contained`
- Run: `publish/MenuBar.exe`

Workflow: change → diff → build → fix → publish.

## Coding Style & Naming Conventions
- 4-space indent; XAML for UI, code-behind/`Services/` for behavior
- `PascalCase` types/members; `_camelCase` private fields; `camelCase` locals
- Prefer existing WinUI controls/resources over custom styling

## Manual check
- `dotnet build`
- Smoke-test the published app; verify affected flyouts/settings/layout

## Commit & Pull Request Guidelines
- Keep commit subjects short, imperative, specific
- PRs: summary, affected area, screenshots for UI, and build status

## Configuration Notes
- `bar_height`: `24–56`
- Keep `settings.json` help text in sync with `MenuBarSettings`
- Don’t hand-edit `bin/`, `obj/`, `publish/`
