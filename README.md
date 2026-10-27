# Context Menu Studio

A launchable Windows desktop scaffold for creating Explorer context-menu commands backed by PowerShell scripts. Version 0.1.0.

## Start the included application

1. Extract **the entire ZIP** to a stable local folder, such as `C:\Tools\ContextMenuStudio`. Do not launch it from inside the ZIP.
2. Double-click **Launch.cmd**, or open `app\ContextMenuStudio.Desktop.exe` directly.
3. Choose a sample command or click **Add**. Set its scope, name, group, and script.
4. Use **Test with file…** or **Test with folder…** to execute it against a chosen item. The test runner asks before execution.
5. Click **Apply menus**. In Windows 11 Explorer, right-click a matching item → **Show more options** → **Context Menu Studio**.

The bundled Windows x64 application includes .NET; no SDK or separate .NET installation is required to run it. Windows PowerShell 5.1 is the default script engine. PowerShell 7 is optional and must be installed under `Program Files\PowerShell\7` when selected. Registry changes are per-user and do not require an administrator account.

The build was cross-compiled on Linux. Actual Windows window rendering, Explorer integration, and PowerShell execution were **not run on the authoring host**. Double-click **Verify.cmd** to run the included GUI launch checks on your Windows machine. See [VALIDATION.md](VALIDATION.md) for the precise verification record.

## Features included

- WPF editor with add, duplicate, delete, reorder, enable/disable, and preview.
- Scopes: files, folders, folder background, desktop background, drives, and one specific extension.
- Optional submenu groups, custom icons, and Shift-only commands.
- Script engine selection, confirmation preference, and 1–3600 second timeouts.
- Separate PowerShell runner with output, cancellation, exit status, and execution logs.
- Save draft, apply installed snapshot, JSON import/export, three sample scripts.
- Import leaves commands disabled until reviewed and enabled.
- Per-user registry registration, ownership checks, pre-change backups, failure rollback, and removal.
- Editor startup error reporting and a single-editor-instance guard.
- Automated portable tests, Windows integration tests, Windows GUI smoke checks, and GitHub Actions.

## Scope of this scaffold

This version uses the **classic Explorer context menu**. It does not include a C++ shell DLL or Windows 11's modern top-level context-menu integration. Menus live under a single `Context Menu Studio` submenu; entries and optional groups are arranged inside it. Explorer controls placement relative to other applications.

Commands process **one selected item per invocation**; batch/multi-selection handling is a future shell-extension feature. `$SelectedPaths` is provided as a one-element array to make future migration easier. Scripts are non-interactive: `Read-Host`, interactive console programs, and elevation prompts are not supported by the runner. Automated administrator elevation and a background service are not implemented.

## Script contract

Write the script body directly in the editor. These variables are already available:

| Variable | Value |
|---|---|
| `$TargetPath` | Exact selected file/folder path, or clicked background's folder |
| `$SelectedPaths` | Array containing `$TargetPath` |
| `$CurrentDirectory` | Selected directory, or the selected file's parent |

Example:

```powershell
Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256 | Format-List
```

Use `-LiteralPath` when available. The program sends paths as JSON data, never by replacing text inside your script. It runs the script in a separate PowerShell process with `-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned`. This policy choice is limited to that process and respects higher-priority Group Policy. It does not change system-wide execution-policy settings.

These are ordinary scripts with your account's permissions, not sandboxed code. The editor's Test buttons execute the script; they are not dry runs. Review imported scripts before enabling them. Cancellation terminates the PowerShell process tree when possible; it cannot reverse file changes already made or reliably stop independently detached processes.

Non-terminating PowerShell errors are promoted to terminating errors by default. A thrown exception or nonzero final native-command exit code is reported as failure. A script can deliberately override that behavior.

## Save versus Apply

**Save draft** writes `commands.json` without changing installed actions. **Apply menus** registers the enabled entries and writes an installed snapshot. Right-click invocations read that snapshot, so unsaved/unapplied script edits cannot accidentally change a live command. **Remove installed menus** removes only keys carrying this app's ownership marker and keeps the draft.

Keep the application folder at its installed location: registry entries point to the runner there. After moving/rebuilding the app, open it from its new location and click Apply again. Close running editor/runner windows before replacing application binaries.

## Build from source

Install the **.NET 10 SDK** from https://dotnet.microsoft.com/download/dotnet/10.0. Then double-click **Build.cmd**. The script builds, runs tests, publishes both executables, and launches each in smoke-test mode. A failed check stops the build. Output is written to `artifacts\app`; Launch.cmd prefers this rebuilt copy over the bundled app.

PowerShell alternative:

```powershell
.\scripts\build.ps1 -Launch
```

For a smaller package requiring the .NET 10 **Desktop Runtime**:

```powershell
.\scripts\build.ps1 -FrameworkDependent
```

For native ARM64, run `build.ps1 -Runtime win-arm64` on an ARM64 Windows machine so its launch checks can execute. Internet access is required for the initial SDK/package restore. There are no third-party NuGet application dependencies.

In Visual Studio with .NET 10 SDK support, open `ContextMenuStudio.sln` and set `ContextMenuStudio.Desktop` as the startup project. The build copies the Runner's output alongside the editor for debugging.

The `.cmd` helpers use process-only execution-policy Bypass to run the included build/maintenance scripts without permanently changing your PowerShell configuration. Organizational policy can still prohibit them.

## Project structure

| Project/file | Responsibility |
|---|---|
| `src/ContextMenuStudio.Core` | Models, validation, atomic saves, menu registration, argument parsing, PowerShell execution |
| `src/ContextMenuStudio.Desktop` | WPF editor and configuration workflow |
| `src/ContextMenuStudio.Runner` | Separate output window, confirmation, cancellation, and logs |
| `src/Shared/Theme.xaml` | Shared WPF styling |
| `tests/ContextMenuStudio.Tests` | Dependency-free automated test executable |
| `scripts/build.ps1` | Build, test, publish, and Windows launch verification |
| `.github/workflows/windows.yml` | Windows CI and downloadable application artifact |

## Data and recovery

Data lives under `%LOCALAPPDATA%\ContextMenuStudio`:

- `commands.json`: saved editable draft.
- `installed.json`: active command definitions.
- `backups`: snapshots of the prior installed config and app-owned registry trees before Apply/Remove.
- `logs`: last 50 execution logs, including output and target paths.
- `runs`: temporary script/context files, normally removed after execution.
- `startup-errors.log`: unexpected editor startup/runtime errors.

If the editor cannot launch, inspect its error dialog and `startup-errors.log`. A damaged `commands.json` is never silently replaced; move it aside and relaunch to load the sample draft, then recover commands from your exports or backups. If application files are missing, extract the whole package again or rebuild it.

If menus need removal while the editor is unavailable, close the program and run **Remove-Menus.cmd**. It deletes only matching app-owned registry keys and the installed snapshot. Remove menus before deleting the application folder. Backups can contain script source; logs can contain whatever your scripts print.

## Validation and extension points

Run `dotnet run --project tests/ContextMenuStudio.Tests -c Release`. Portable tests cover validation, serialization, atomic writes, command argument handling, and target scopes. On Windows the same executable also exercises isolated registry registration/removal/conflict handling and actual PowerShell Unicode paths, errors, timeouts, and cancellation. Registry tests use a temporary key outside `Software\Classes`.

`scripts/smoke-test.ps1 -AppDirectory .\app` starts both WPF executables, waits for `ContentRendered`, checks their exit codes, and requires success marker files. It does not execute user scripts or install menus.

Next architectural step: a small native `IExplorerCommand` component for modern Windows 11 menu integration and true multi-selection. Keep its menu-construction work small and preserve the separate process runner. Other useful additions are syntax highlighting, drag-and-drop ordering, configurable script parameters, and an installer with upgrade/removal integration.

## References

- [Microsoft: extending shortcut menus](https://learn.microsoft.com/windows/win32/shell/context)
- [Microsoft: static cascading menus](https://learn.microsoft.com/windows/win32/shell/creating-static-cascading-menus)
- [Microsoft: managed shell-extension guidance](https://learn.microsoft.com/windows/win32/shell/shell-and-managed-code)
- [Microsoft: modern File Explorer commands](https://learn.microsoft.com/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)

License: MIT; bundled .NET runtime components retain their own notices and licenses.
