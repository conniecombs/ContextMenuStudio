using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ContextMenuStudio.Core;

public enum MenuScope { Files, Folders, FolderBackground, DesktopBackground, Drives, FileExtension }
public enum ScriptEngine { WindowsPowerShell, PowerShell7 }

public sealed class MenuCommand : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string name = "New command", group = "", extension = ".txt", icon = "", script = "Write-Output $TargetPath";
    private bool enabled = true, shiftOnly, confirm = true;
    private MenuScope scope = MenuScope.Files;
    private ScriptEngine engine;
    private int timeoutSeconds = 120;
    public string Name { get => name; set => Set(ref name, value); }
    public string Group { get => group; set => Set(ref group, value); }
    public string Extension { get => extension; set => Set(ref extension, value); }
    public string Icon { get => icon; set => Set(ref icon, value); }
    public string Script { get => script; set => Set(ref script, value); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public bool ShiftOnly { get => shiftOnly; set => Set(ref shiftOnly, value); }
    public bool Confirm { get => confirm; set => Set(ref confirm, value); }
    public MenuScope Scope { get => scope; set => Set(ref scope, value); }
    public ScriptEngine Engine { get => engine; set => Set(ref engine, value); }
    public int TimeoutSeconds { get => timeoutSeconds; set => Set(ref timeoutSeconds, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

public sealed class MenuConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public ObservableCollection<MenuCommand> Commands { get; set; } = [];
}

public static class Samples
{
    public static MenuConfiguration Create() => new()
    {
        Commands =
        [
            new() { Name = "Show SHA-256", Group = "File tools", Script = "Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256 | Format-List" },
            new() { Name = "Copy full path", Script = "Set-Clipboard -Value $TargetPath\nWrite-Output ('Copied: ' + $TargetPath)" },
            new() { Name = "List folder contents", Scope = MenuScope.Folders, Script = "Get-ChildItem -LiteralPath $TargetPath | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize" }
        ]
    };
}
