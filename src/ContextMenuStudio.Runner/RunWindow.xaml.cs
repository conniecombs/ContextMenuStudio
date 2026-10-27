using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using ContextMenuStudio.Core;

namespace ContextMenuStudio.Runner;

public partial class RunWindow : Window
{
    private readonly MenuCommand command;
    private readonly string target;
    private readonly bool test, smoke;
    private readonly CancellationTokenSource cancellation = new();
    private readonly StringBuilder transcript = new();
    private readonly object outputGate = new();
    private bool running, started, closeWhenFinished;
    public RunWindow(MenuCommand command, string target, bool test, bool smoke = false)
    {
        InitializeComponent();
        this.command = command; this.target = target; this.test = test; this.smoke = smoke;
        Heading.Text = command.Name + (test ? " (test)" : "");
        Target.Text = target;
        ContentRendered += OnRendered;
        Closed += (_, _) => cancellation.Dispose();
    }
    private async void OnRendered(object? sender, EventArgs e)
    {
        if (started || smoke) return;
        started = true;
        if ((test || command.Confirm) && MessageBox.Show(this, $"Run '{command.Name}' on:\n{target}\n\nThe script runs with your account permissions.", "Run PowerShell command", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            Status.Text = "Cancelled before execution."; CancelButton.IsEnabled = false; CloseButton.IsEnabled = true; return;
        }
        running = true;
        Status.Text = "Running…";
        try
        {
            var result = await ScriptExecution.RunAsync(command, target, Append, cancellation.Token);
            Status.Text = result.TimedOut ? "Stopped: time limit reached." : result.Cancelled ? "Cancelled." : result.ExitCode == 0 ? "Completed successfully." : $"Failed — exit code {result.ExitCode}.";
        }
        catch (OperationCanceledException) { Status.Text = "Cancelled."; }
        catch (Exception ex) { Append(ex.Message + Environment.NewLine); Status.Text = "Could not complete the command."; }
        finally
        {
            running = false;
            CancelButton.IsEnabled = false;
            CloseButton.IsEnabled = true;
            try
            {
                var folder = Path.Combine(ConfigurationStore.DataDirectory, "logs");
                Directory.CreateDirectory(folder);
                string text;
                lock (outputGate) text = transcript.ToString();
                File.WriteAllText(Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log"), $"{command.Name}\n{target}\n{Status.Text}\n\n{text}");
                foreach (var old in new DirectoryInfo(folder).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(50))
                    try { old.Delete(); } catch (IOException) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text += " Log could not be saved."; }
            if (closeWhenFinished) Close();
        }
    }
    private void Append(string text)
    {
        lock (outputGate) { if (transcript.Length < 2_100_000) transcript.Append(text); }
        Dispatcher.BeginInvoke(() => { Output.AppendText(text); Output.ScrollToEnd(); });
    }
    private void CancelClick(object sender, RoutedEventArgs e) { cancellation.Cancel(); CancelButton.IsEnabled = false; Status.Text = "Cancelling…"; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        e.Cancel = true;
        closeWhenFinished = true;
        cancellation.Cancel();
        Status.Text = "Stopping the running command before closing…";
    }
}
