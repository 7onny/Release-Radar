using System.Windows;
using ReleaseRadar.Core;

namespace ReleaseRadar;

public partial class App : Application
{
    public static string DataDirectory { get; private set; } = "";
    public static bool Demo { get; private set; }
    public static IntPtr NearWindow { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Demo = e.Args.Contains("--demo");
        var nearArg = Array.IndexOf(e.Args, "--near-window");
        if (nearArg >= 0 && nearArg + 1 < e.Args.Length && long.TryParse(e.Args[nearArg + 1], out var handle)) NearWindow = new IntPtr(handle);
        var dataArg = Array.IndexOf(e.Args, "--data-dir");
        DataDirectory = dataArg >= 0 && dataArg + 1 < e.Args.Length ? System.IO.Path.GetFullPath(e.Args[dataArg + 1]) :
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReleaseRadar");
        if (Demo) DataDirectory = System.IO.Path.Combine(DataDirectory, "demo");
        var window = new MainWindow(new CacheStore(DataDirectory));
        MainWindow = window;
        window.Show();
    }
}
