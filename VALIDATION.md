# Validation record — 0.1.0

Authoring environment: Linux x64. SDK: .NET 10.0.403. Included Windows runtime: .NET / Windows Desktop 10.0.14.

## Completed

- Release build of all four projects: **passed; 0 warnings, 0 errors**.
- WPF XAML compilation for editor and runner: **passed**.
- Portable regression suite: **21 passed; 0 failed**.
- Windows x64 self-contained publish of editor and runner: **passed**.
- Both entry points identified as Windows x64 GUI PE executables.
- PowerShell parser checks for build, GUI smoke check, recovery removal, and generated invocation wrapper: **passed**.
- Bundled runtime configuration identifies both frameworks as 10.0.14.

The PowerShell parser was an isolated verification tool; its dependencies are not included in this project's dependencies or binaries.

## Requires Windows; not executed here

- Editor and runner window rendering and interaction.
- Windows registry integration tests (isolated temporary keys).
- Actual Windows PowerShell execution, Unicode target handling, timeouts, and cancellation.
- Live Explorer menu placement, icons, group visibility, and invocation.

Run **Verify.cmd** for non-mutating window launch checks. Run **Build.cmd** for a complete Windows build, tests, publish, and GUI launch checks. The included GitHub Actions workflow performs the same build sequence on Windows; it has not been submitted to a repository or run remotely in this session.

## Suggested first Windows acceptance check

1. Extract the entire package and run Verify.cmd; both checks should pass.
2. Launch the editor; choose Show SHA-256 and test with a small text file.
3. Apply menus; use Show more options → Context Menu Studio → File tools → Show SHA-256.
4. Edit the script and save only: the live command should still use the installed script.
5. Apply again: the live command should now use the edited script.
6. Remove installed menus: the app's submenu should disappear while unrelated entries remain.

Build, publish, and test output is included under `validation/` in the downloadable package.
