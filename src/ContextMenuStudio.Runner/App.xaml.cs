using System.IO;
using System.Windows;
using ContextMenuStudio.Core;

namespace ContextMenuStudio.Runner;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
            {
                var smokeWindow = new RunWindow(new() { Name = "Runner startup check" }, "No script will run", true, true);
                MainWindow = smokeWindow;
                smokeWindow.ContentRendered += (_, _) => { File.WriteAllText(e.Args[1], "Runner rendered successfully."); smokeWindow.Close(); };
                smokeWindow.Show();
                return;
            }
            string[] args = e.Args;
            bool test = args.Length == 6 && args[0] == "--test";
            MenuConfiguration config;
            if (test)
            {
                string path = Path.GetFullPath(args[1]);
                string testFolder = Path.GetFullPath(Path.Combine(ConfigurationStore.DataDirectory, "tests")) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(testFolder, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) != ".json") throw new ArgumentException("Invalid test configuration location.");
                config = ConfigurationStore.Load(path);
                File.Delete(path);
                args = args[2..];
            }
            else config = ConfigurationStore.Load(ConfigurationStore.InstalledPath);
            var (id, target) = MenuPlan.ParseInvocation(args);
            var command = config.Commands.SingleOrDefault(c => c.Id == id) ?? throw new InvalidDataException("This command is no longer installed. Apply menus again in the editor.");
            if (!test && !command.Enabled) throw new InvalidOperationException("This command is disabled.");
            MenuPlan.ValidateTarget(command, target);
            var window = new RunWindow(command, target, test);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Context Menu Studio — Cannot run command", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
