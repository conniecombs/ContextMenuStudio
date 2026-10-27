using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ContextMenuStudio.Core;

public sealed class RegistryNode
{
    public Dictionary<string, string> Values { get; set; } = [];
    public Dictionary<string, RegistryNode> Children { get; set; } = [];
}

[SupportedOSPlatform("windows")]
public sealed class RegistryMenuService(string classesPath = @"Software\Classes", string? installedPath = null)
{
    private readonly string installedPath = installedPath ?? ConfigurationStore.InstalledPath;
    public void Apply(MenuConfiguration configuration, string runnerPath)
    {
        ConfigurationStore.Validate(configuration);
        if (configuration.Commands.Any(c => c.Enabled) && !File.Exists(runnerPath)) throw new FileNotFoundException("Publish the application first: the PowerShell runner is missing.", runnerPath);
        foreach (var item in configuration.Commands.Where(c => c.Enabled)) _ = MenuPlan.Invocation(item, runnerPath);
        using var gate = new Mutex(false, @"Local\ContextMenuStudio.RegistryUpdate");
        bool acquired;
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another menu update is in progress. Try again.");
        try { ApplyLocked(configuration, runnerPath); }
        finally { gate.ReleaseMutex(); }
    }
    private void ApplyLocked(MenuConfiguration configuration, string runnerPath)
    {
        using var classes = Registry.CurrentUser.CreateSubKey(classesPath, true);
        var parents = new HashSet<string>(MenuPlan.BaseParents, StringComparer.OrdinalIgnoreCase);
        using (var associations = classes.OpenSubKey("SystemFileAssociations"))
            if (associations is not null)
                foreach (var extension in associations.GetSubKeyNames().Where(n => n.StartsWith('.')))
                    parents.Add(@"SystemFileAssociations\" + extension + @"\shell");
        foreach (var command in configuration.Commands) parents.Add(MenuPlan.Parent(command));

        var prior = new Dictionary<string, RegistryNode>();
        foreach (var parent in parents)
        {
            var path = parent + "\\" + MenuPlan.RootName;
            using var existing = classes.OpenSubKey(path);
            if (existing is null) continue;
            if ((string?)existing.GetValue("Owner") != MenuPlan.OwnerId)
                throw new IOException($"An unrelated registry key already uses '{path}'. No changes were made.");
            prior[path] = ReadNode(existing);
        }
        string? previousConfig = File.Exists(installedPath) ? File.ReadAllText(installedPath) : null;
        var backupDirectory = Path.Combine(Path.GetDirectoryName(installedPath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        ConfigurationStore.AtomicWrite(Path.Combine(backupDirectory, $"menus-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { InstalledConfiguration = previousConfig, Registry = prior }, ConfigurationStore.JsonOptions));
        try
        {
            DeleteOwned(classes, parents);
            foreach (var grouping in configuration.Commands.Where(c => c.Enabled).GroupBy(MenuPlan.Parent))
            {
                using var root = classes.CreateSubKey(grouping.Key + "\\" + MenuPlan.RootName);
                root.SetValue("Owner", MenuPlan.OwnerId);
                root.SetValue("MUIVerb", "Context Menu Studio");
                root.SetValue("SubCommands", "");
                using var children = root.CreateSubKey("shell");
                int index = 0;
                // Order follows the editor. Entries in the same named group share a submenu.
                var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var command in grouping)
                {
                    var parent = children;
                    RegistryKey? groupKey = null;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(command.Group))
                        {
                            if (!groups.TryGetValue(command.Group.Trim(), out var groupPath))
                            {
                                groupPath = $"{index++:D3}_group";
                                groups.Add(command.Group.Trim(), groupPath);
                                using var group = children.CreateSubKey(groupPath);
                                group.SetValue("MUIVerb", command.Group.Trim());
                                group.SetValue("SubCommands", "");
                            }
                            groupKey = children.CreateSubKey(groupPath + @"\shell");
                            parent = groupKey;
                        }
                        using var verb = parent.CreateSubKey($"{index++:D3}_{command.Id:N}");
                        verb.SetValue("MUIVerb", command.Name);
                        verb.SetValue("MultiSelectModel", "Single");
                        if (command.ShiftOnly) verb.SetValue("Extended", "");
                        if (!string.IsNullOrWhiteSpace(command.Icon)) verb.SetValue("Icon", command.Icon);
                        using var action = verb.CreateSubKey("command");
                        action.SetValue("", MenuPlan.Invocation(command, runnerPath));
                    }
                    finally { groupKey?.Dispose(); }
                }
            }
            ConfigurationStore.Save(installedPath, configuration);
        }
        catch (Exception failure)
        {
            try
            {
                DeleteOwned(classes, parents);
                foreach (var (path, node) in prior)
                {
                    using var key = classes.CreateSubKey(path);
                    WriteNode(key, node);
                }
                if (previousConfig is null) File.Delete(installedPath);
                else ConfigurationStore.AtomicWrite(installedPath, previousConfig);
            }
            catch (Exception rollback) { throw new AggregateException("Menu update and rollback failed. The previous state is in the backups folder.", failure, rollback); }
            throw;
        }
        finally { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
    }
    private static void DeleteOwned(RegistryKey classes, IEnumerable<string> parents)
    {
        foreach (var parent in parents)
        {
            var path = parent + "\\" + MenuPlan.RootName;
            bool owned;
            using (var key = classes.OpenSubKey(path)) owned = (string?)key?.GetValue("Owner") == MenuPlan.OwnerId;
            if (owned) classes.DeleteSubKeyTree(path, false);
        }
    }
    private static RegistryNode ReadNode(RegistryKey key)
    {
        var node = new RegistryNode();
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValueKind(name) != RegistryValueKind.String) throw new IOException("An installed menu has been modified externally. Remove the conflicting value before applying changes.");
            node.Values[name] = (string)key.GetValue(name)!;
        }
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name)!;
            node.Children[name] = ReadNode(child);
        }
        return node;
    }
    private static void WriteNode(RegistryKey key, RegistryNode node)
    {
        foreach (var (name, value) in node.Values) key.SetValue(name, value, RegistryValueKind.String);
        foreach (var (name, child) in node.Children)
        {
            using var childKey = key.CreateSubKey(name);
            WriteNode(childKey, child);
        }
    }
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
