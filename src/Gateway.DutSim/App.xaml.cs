using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Gateway.DutSim;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show(e.Exception.ToString(), "Unhandled error");
        e.Handled = true;
    }

    private static void Log(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                $"{DateTime.Now}{Environment.NewLine}{ex}");
        }
        catch { /* ignore */ }
    }
}
