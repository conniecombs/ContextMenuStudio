using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ContextMenuStudio.Core;
using Microsoft.Win32;

namespace ContextMenuStudio.Desktop;

public partial class MainWindow : Window
{
    private MenuConfiguration configuration;
    private string savedState;
    private readonly bool smoke;
    private string RunnerPath => Path.Combine(AppContext.BaseDirectory, "ContextMenuStudio.Runner.exe");
    private MenuCommand? Selected => CommandList.SelectedItem as MenuCommand;
    public MainWindow(bool smoke = false)
    {
        this.smoke = smoke;
        InitializeComponent();
        ScopeCombo.ItemsSource = Enum.GetValues<MenuScope>();
        EngineCombo.ItemsSource = Enum.GetValues<ScriptEngine>();
        configuration = !smoke && File.Exists(ConfigurationStore.DraftPath) ? ConfigurationStore.Load(ConfigurationStore.DraftPath) : Samples.Create();
        savedState = ConfigurationStore.Serialize(configuration);
        BindList();
        if (!smoke && File.Exists(ConfigurationStore.InstalledPath))
        {
            try { Status.Text = $"{ConfigurationStore.Load(ConfigurationStore.InstalledPath).Commands.Count(c => c.Enabled)} commands installed. Draft edits take effect only when you Apply menus."; }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { Status.Text = "Installed configuration could not be read. Apply menus to repair it."; }
        }
    }
    private void BindList()
    {
        CommandList.ItemsSource = configuration.Commands;
        CommandList.SelectedIndex = configuration.Commands.Count == 0 ? -1 : 0;
        Editor.DataContext = Selected;
        Editor.IsEnabled = Selected is not null;
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Editor is null) return;
        Editor.DataContext = Selected;
        Editor.IsEnabled = Selected is not null;
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Status.Text = "Action failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Context Menu Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void ValidateEditor()
    {
        if (HasValidationError(Editor)) throw new InvalidDataException("Correct the highlighted field before continuing. Timeout must be a whole number.");
        ConfigurationStore.Validate(configuration);
    }
    private static bool HasValidationError(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    private void Save()
    {
        ValidateEditor();
        ConfigurationStore.Save(ConfigurationStore.DraftPath, configuration);
        savedState = ConfigurationStore.Serialize(configuration);
        Status.Text = "Draft saved. Click Apply menus to update installed commands.";
    }
    private void SaveClick(object sender, RoutedEventArgs e) => Guard(Save);
    private void ApplyClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        ValidateEditor();
        new RegistryMenuService().Apply(configuration, RunnerPath);
        Save();
        Status.Text = $"Applied {configuration.Commands.Count(c => c.Enabled)} commands. Right-click → Show more options → Context Menu Studio. Keep this application folder in place.";
    });
    private void RemoveClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (MessageBox.Show(this, "Remove all context menus installed by Context Menu Studio? Your draft and scripts will be kept.", "Remove installed menus", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        new RegistryMenuService().Apply(new(), RunnerPath);
        Status.Text = "Installed menus removed. Your saved draft is available for later use.";
    });
    private void AddClick(object sender, RoutedEventArgs e)
    {
        var command = new MenuCommand();
        configuration.Commands.Add(command);
        CommandList.SelectedItem = command;
    }
    private void DuplicateClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Selected is null) return;
        ValidateEditor();
        var copy = ConfigurationStore.Parse(ConfigurationStore.Serialize(new() { Commands = [Selected] })).Commands[0];
        copy.Id = Guid.NewGuid();
        copy.Name = (copy.Name.Length > 90 ? copy.Name[..90] : copy.Name) + " (copy)";
        configuration.Commands.Add(copy);
        CommandList.SelectedItem = copy;
    });
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (MessageBox.Show(this, $"Delete '{Selected.Name}' from the draft? Apply menus to update Explorer.", "Delete command", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        int index = CommandList.SelectedIndex;
        configuration.Commands.Remove(Selected);
        CommandList.SelectedIndex = Math.Min(index, configuration.Commands.Count - 1);
    }
    private void Move(int offset)
    {
        int index = CommandList.SelectedIndex, destination = index + offset;
        if (index < 0 || destination < 0 || destination >= configuration.Commands.Count) return;
        configuration.Commands.Move(index, destination);
        CommandList.SelectedIndex = destination;
    }
    private void UpClick(object sender, RoutedEventArgs e) => Move(-1);
    private void DownClick(object sender, RoutedEventArgs e) => Move(1);
    private bool CanReplaceDraft()
    {
        if (ConfigurationStore.Serialize(configuration) == savedState && !HasValidationError(Editor)) return true;
        var choice = MessageBox.Show(this, "Save your draft changes before continuing?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) Save();
        return true;
    }
    private void ImportClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Menu configuration (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var imported = ConfigurationStore.Load(dialog.FileName);
        if (!CanReplaceDraft()) return;
        foreach (var command in imported.Commands) { command.Enabled = false; command.Confirm = true; }
        configuration = imported;
        BindList();
        Status.Text = "Imported into the draft. Review scripts, enable the commands you want, then Apply menus.";
    });
    private void ExportClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        ValidateEditor();
        var dialog = new SaveFileDialog { Filter = "Menu configuration (*.json)|*.json", FileName = "my-context-menus.json" };
        if (dialog.ShowDialog(this) == true) { ConfigurationStore.Save(dialog.FileName, configuration); Status.Text = "Configuration exported, including script source."; }
    });
    private void DataFolderClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Directory.CreateDirectory(ConfigurationStore.DataDirectory);
        Process.Start(new ProcessStartInfo(ConfigurationStore.DataDirectory) { UseShellExecute = true });
    });
    private void IconClick(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var dialog = new OpenFileDialog { Filter = "Icons and resources|*.ico;*.exe;*.dll|All files|*.*" };
        if (dialog.ShowDialog(this) == true) Selected.Icon = dialog.FileName;
    }
    private void PreviewClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        ValidateEditor();
        var lines = new List<string> { "Context Menu Studio", "" };
        foreach (var scope in configuration.Commands.Where(c => c.Enabled).GroupBy(c => c.Scope == MenuScope.FileExtension ? c.Extension : c.Scope.ToString()))
        {
            lines.Add(scope.Key);
            foreach (var item in scope) lines.Add($"  {(string.IsNullOrWhiteSpace(item.Group) ? "" : item.Group + " / ")}{item.Name}{(item.ShiftOnly ? "  [Shift]" : "")}");
            lines.Add("");
        }
        new Window { Title = "Menu preview", Width = 600, Height = 500, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox { Text = string.Join(Environment.NewLine, lines), IsReadOnly = true, Margin = new Thickness(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
    });
    private void TestFileClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog(this) == true) Test(dialog.FileName);
    });
    private void TestFolderClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) Test(dialog.FolderName);
    });
    private void Test(string target)
    {
        if (Selected is null) return;
        ValidateEditor();
        MenuPlan.ValidateTarget(Selected, target);
        if (!File.Exists(RunnerPath)) throw new FileNotFoundException("The runner is missing. Run Build.cmd first.");
        var path = Path.Combine(ConfigurationStore.DataDirectory, "tests", Guid.NewGuid().ToString("N") + ".json");
        ConfigurationStore.Save(path, new() { Commands = [Selected] });
        try
        {
            var start = new ProcessStartInfo(RunnerPath) { UseShellExecute = false };
            foreach (var arg in new[] { "--test", path, "--command", Selected.Id.ToString("D"), "--target", target + MenuPlan.TargetSuffix }) start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch { File.Delete(path); throw; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (smoke) return;
        try { e.Cancel = !CanReplaceDraft(); }
        catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message, "Cannot save draft"); }
    }
}
