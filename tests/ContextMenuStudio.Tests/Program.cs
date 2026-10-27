using ContextMenuStudio.Core;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text;

namespace ContextMenuStudio.Tests;

internal static class Program
{
    private static int passed, failed;
    private static readonly string Temp = Path.Combine(Path.GetTempPath(), "CMS tests " + Guid.NewGuid().ToString("N"));
    private static async Task<int> Main()
    {
        Directory.CreateDirectory(Temp);
        try
        {
            Check("Sample configurations round-trip", () =>
            {
                var sample = Samples.Create();
                var restored = ConfigurationStore.Parse(ConfigurationStore.Serialize(sample));
                Equal(sample.Commands[0].Script, restored.Commands[0].Script);
                Equal(sample.Commands[0].Id, restored.Commands[0].Id);
            });
            Check("Atomic save preserves Unicode", () =>
            {
                var config = Samples.Create(); config.Commands[0].Name = "Résumé — 文件";
                var path = Path.Combine(Temp, "config.json"); ConfigurationStore.Save(path, config);
                Equal(config.Commands[0].Name, ConfigurationStore.Load(path).Commands[0].Name);
                Equal(0, Directory.GetFiles(Temp, "*.tmp").Length);
            });
            Check("Invalid save leaves previous data intact", () =>
            {
                var path = Path.Combine(Temp, "existing.json"); ConfigurationStore.Save(path, Samples.Create()); var before = File.ReadAllText(path);
                var bad = Samples.Create(); bad.Commands[0].Script = "";
                Throws<InvalidDataException>(() => ConfigurationStore.Save(path, bad)); Equal(before, File.ReadAllText(path));
            });
            Check("Reject future schema", () => Throws<InvalidDataException>(() => ConfigurationStore.Validate(new() { SchemaVersion = 99 })));
            Check("Reject duplicate IDs", () => Invalid(c => c.Commands[1].Id = c.Commands[0].Id));
            Check("Reject empty ID", () => Invalid(c => c.Commands[0].Id = Guid.Empty));
            Check("Reject empty label", () => Invalid(c => c.Commands[0].Name = " "));
            Check("Reject missing script", () => Invalid(c => c.Commands[0].Script = ""));
            Check("Reject invalid timeout", () => Invalid(c => c.Commands[0].TimeoutSeconds = 0));
            Check("Reject oversized timeout", () => Invalid(c => c.Commands[0].TimeoutSeconds = 3601));
            Check("Reject scope injection", () => Invalid(c => { c.Commands[0].Scope = MenuScope.FileExtension; c.Commands[0].Extension = @".txt\shell\bad"; }));
            Check("Reject undefined scope", () => Invalid(c => c.Commands[0].Scope = (MenuScope)999));
            Check("Reject null commands", () => Throws<InvalidDataException>(() => ConfigurationStore.Parse("{\"SchemaVersion\":1,\"Commands\":null}")));
            Check("Extension scope is normalized", () => Equal(@"SystemFileAssociations\.txt\shell", MenuPlan.Parent(new() { Scope = MenuScope.FileExtension, Extension = ".TXT" })));
            Check("Background uses current folder placeholder", () => True(MenuPlan.Invocation(new() { Scope = MenuScope.FolderBackground }, @"C:\Apps\Runner.exe").Contains("%V|CMS")));
            Check("Invocation carries GUID and quoted target", () =>
            {
                var command = new MenuCommand(); var value = MenuPlan.Invocation(command, @"C:\My Tools\Runner.exe");
                True(value.StartsWith("\"C:\\My Tools\\Runner.exe\"")); True(value.Contains(command.Id.ToString("D"))); True(value.EndsWith("\"%1|CMS\""));
            });
            Check("Reject percent in executable path", () => Throws<InvalidDataException>(() => MenuPlan.Invocation(new(), @"C:\%1\Runner.exe")));
            Check("Drive root survives argument parsing", () =>
            {
                var id = Guid.NewGuid(); var parsed = MenuPlan.ParseInvocation(["--command", id.ToString(), "--target", @"C:\|CMS"]);
                Equal(@"C:\", parsed.Target); Equal(id, parsed.Id);
            });
            Check("Path metacharacters remain data", () =>
            {
                const string target = @"C:\Résumé\it's $x; & [a]`!.txt";
                Equal(target, MenuPlan.ParseInvocation(["--command", Guid.NewGuid().ToString(), "--target", target + MenuPlan.TargetSuffix]).Target);
            });
            Check("Malformed invocation is rejected", () => Throws<ArgumentException>(() => MenuPlan.ParseInvocation(["--command", "no", "--target", "C:\\a"])));
            Check("Scope rejects mismatched target", () =>
            {
                var file = Path.Combine(Temp, "a.txt"); File.WriteAllText(file, "test");
                Throws<ArgumentException>(() => MenuPlan.ValidateTarget(new() { Scope = MenuScope.Folders }, file));
                Throws<ArgumentException>(() => MenuPlan.ValidateTarget(new() { Scope = MenuScope.FileExtension, Extension = ".jpg" }, file));
                MenuPlan.ValidateTarget(new() { Scope = MenuScope.FileExtension, Extension = ".TXT" }, file);
            });
            if (OperatingSystem.IsWindows()) await WindowsChecks();
            else Console.WriteLine("SKIP Windows registry, PowerShell, and GUI launch checks (this host is not Windows).");
        }
        finally { Directory.Delete(Temp, true); }
        Console.WriteLine($"{passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    [SupportedOSPlatform("windows")]
    private static async Task WindowsChecks()
    {
        Check("Windows: registration, update, and removal are isolated", () =>
        {
            string registryPath = @"Software\ContextMenuStudio.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                var service = new RegistryMenuService(registryPath, Path.Combine(Temp, "installed.json"));
                var configuration = Samples.Create();
                configuration.Commands.Add(new() { Scope = MenuScope.FileExtension, Extension = ".txt" });
                string runner = Path.Combine(Temp, "runner.exe"); File.WriteAllText(runner, "fixture");
                service.Apply(configuration, runner);
                using (var key = Registry.CurrentUser.OpenSubKey(registryPath + @"\*\shell\ContextMenuStudio")) Equal(MenuPlan.OwnerId, (string?)key?.GetValue("Owner"));
                configuration.Commands.Clear(); service.Apply(configuration, runner);
                using var removed = Registry.CurrentUser.OpenSubKey(registryPath + @"\*\shell\ContextMenuStudio"); True(removed is null);
                using var extension = Registry.CurrentUser.OpenSubKey(registryPath + @"\SystemFileAssociations\.txt\shell\ContextMenuStudio"); True(extension is null);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
        });
        Check("Windows: conflicting registry entry is preserved", () =>
        {
            string registryPath = @"Software\ContextMenuStudio.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (var foreign = Registry.CurrentUser.CreateSubKey(registryPath + @"\*\shell\ContextMenuStudio")) foreign.SetValue("Owner", "someone-else");
                Throws<IOException>(() => new RegistryMenuService(registryPath, Path.Combine(Temp, "foreign.json")).Apply(new(), "unused.exe"));
                using var key = Registry.CurrentUser.OpenSubKey(registryPath + @"\*\shell\ContextMenuStudio"); Equal("someone-else", (string?)key?.GetValue("Owner"));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
        });
        var target = Path.Combine(Temp, "it's $x; & [a]`! Résumé.txt"); File.WriteAllText(target, "test");
        await CheckAsync("Windows: PowerShell receives literal Unicode target", async () =>
        {
            var output = new StringBuilder();
            var result = await ScriptExecution.RunAsync(new() { Script = "Write-Output $TargetPath" }, target, s => { lock(output) output.Append(s); }, CancellationToken.None);
            Equal(0, result.ExitCode); True(output.ToString().Contains(target));
        });
        await CheckAsync("Windows: PowerShell errors have nonzero exit status", async () =>
        {
            var result = await ScriptExecution.RunAsync(new() { Script = "throw 'expected failure'" }, target, _ => { }, CancellationToken.None);
            True(result.ExitCode != 0);
        });
        await CheckAsync("Windows: timeout stops PowerShell", async () =>
        {
            var result = await ScriptExecution.RunAsync(new() { Script = "Start-Sleep -Seconds 30", TimeoutSeconds = 1 }, target, _ => { }, CancellationToken.None);
            True(result.TimedOut);
        });
        await CheckAsync("Windows: cancellation stops PowerShell", async () =>
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var result = await ScriptExecution.RunAsync(new() { Script = "Start-Sleep -Seconds 30" }, target, _ => { }, cancel.Token);
            True(result.Cancelled);
        });
    }
    private static void Invalid(Action<MenuConfiguration> change) { var c = Samples.Create(); change(c); Throws<InvalidDataException>(() => ConfigurationStore.Validate(c)); }
    private static void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); } }
    private static async Task CheckAsync(string name, Func<Task> action) { try { await action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); } }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
