using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace ContextMenuStudio.Core;

public sealed record ExecutionResult(int ExitCode, bool Cancelled, bool TimedOut);

[SupportedOSPlatform("windows")]
public static class ScriptExecution
{
    public static string ResolveEngine(ScriptEngine engine)
    {
        var path = engine == ScriptEngine.WindowsPowerShell
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (!File.Exists(path)) throw new FileNotFoundException(engine == ScriptEngine.PowerShell7
            ? "PowerShell 7 was not found in Program Files\\PowerShell\\7. Install it there or select Windows PowerShell."
            : "Windows PowerShell was not found.", path);
        return path;
    }
    public static async Task<ExecutionResult> RunAsync(MenuCommand command, string target, Action<string> output, CancellationToken cancellationToken)
    {
        ConfigurationStore.Validate(new() { Commands = [command] });
        MenuPlan.ValidateTarget(command, target);
        var engine = ResolveEngine(command.Engine);
        var directory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;
        var runDirectory = Path.Combine(ConfigurationStore.DataDirectory, "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        try
        {
            var encoding = new UTF8Encoding(true); // Windows PowerShell 5.1 needs the BOM for Unicode script source.
            await File.WriteAllTextAsync(Path.Combine(runDirectory, "action.ps1"), command.Script, encoding, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, "context.json"), JsonSerializer.Serialize(new { TargetPath = target, CurrentDirectory = directory }), Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, "invoke.ps1"), Wrapper, encoding, cancellationToken);
            var start = new ProcessStartInfo(engine)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = directory
            };
            // This applies only to the child process. Machine/User group policy still takes precedence.
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", Path.Combine(runDirectory, "invoke.ps1") }) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("PowerShell could not be started.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var stdout = PumpAsync(process.StandardOutput, output, linked.Token);
            var stderr = PumpAsync(process.StandardError, text => output(text), linked.Token);
            bool cancelled = false, timedOut = false;
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                cancelled = cancellationToken.IsCancellationRequested;
                timedOut = !cancelled && timeout.IsCancellationRequested;
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            // A detached child can inherit a pipe. Bound draining even after the main process exits.
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { linked.Cancel(); }
            return new(process.ExitCode, cancelled, timedOut);
        }
        finally
        {
            try { Directory.Delete(runDirectory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private static async Task PumpAsync(StreamReader reader, Action<string> output, CancellationToken token)
    {
        char[] buffer = new char[4096];
        int emitted = 0;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            int remaining = 1_000_000 - emitted;
            if (remaining > 0)
            {
                int size = Math.Min(count, remaining);
                output(new string(buffer, 0, size));
                emitted += size;
                if (emitted == 1_000_000) output("\n[Output limit reached; remaining output discarded.]\n");
            }
        }
    }
    private const string Wrapper = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $OutputEncoding = [Console]::OutputEncoding
        try {
            $context = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'context.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            $TargetPath = [string]$context.TargetPath
            $SelectedPaths = @($TargetPath)
            $CurrentDirectory = [string]$context.CurrentDirectory
            $global:LASTEXITCODE = 0
            & (Join-Path $PSScriptRoot 'action.ps1')
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        } catch {
            [Console]::Error.WriteLine($_.ToString())
            exit 1
        }
        """;
}
