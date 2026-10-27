using System.IO;
using System.Windows;
using ContextMenuStudio.Core;

namespace ContextMenuStudio.Desktop;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        try
        {
            bool smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
            if (!smoke)
            {
                instance = new Mutex(true, @"Local\ContextMenuStudio.Editor", out ownsInstance);
                if (!ownsInstance) { MessageBox.Show("Context Menu Studio is already open."); Shutdown(); return; }
            }
            var window = new MainWindow(smoke);
            MainWindow = window;
            if (smoke)
                window.ContentRendered += (_, _) => { File.WriteAllText(e.Args[1], "Editor rendered successfully."); window.Close(); };
            window.Show();
        }
        catch (Exception ex) { Report(ex); Shutdown(1); }
    }
    private static void Report(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(ConfigurationStore.DataDirectory);
            File.AppendAllText(Path.Combine(ConfigurationStore.DataDirectory, "startup-errors.log"), $"{DateTimeOffset.Now:O}\n{exception}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        MessageBox.Show(exception.Message + "\n\nDetails: %LOCALAPPDATA%\\ContextMenuStudio\\startup-errors.log", "Context Menu Studio", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
