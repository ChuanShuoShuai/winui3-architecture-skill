using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;

namespace MyApp;

/// <summary>
/// Composition root. Order matters here:
///   1. UnhandledException is wired in the constructor, before any XAML is parsed.
///   2. The single-instance mutex is taken before the window is created.
///   3. Window creation is wrapped in try/catch that writes crash.log.
/// </summary>
public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    private static Mutex? _mutex;
    private const string MutexName = @"Local\MyApp_SingleInstance";
    private const string DataDirName = "MyApp";

    public App()
    {
        LogStartup("App ctor begin");
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            LogCrash("App.InitializeComponent", ex);
            throw;
        }
        LogStartup("App ctor done");

        UnhandledException += (s, e) =>
        {
            LogCrash("UnhandledException", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LogStartup("OnLaunched begin");

        // Local\ keeps the mutex scoped to this session, so a second Windows user
        // on the same machine is not locked out.
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // A second launch exits silently. If you support toast/activation
            // arguments, forward them over a named pipe before exiting.
            Environment.Exit(0);
            return;
        }

        AppSettings.Load();
        LogStartup("Settings loaded");

        try
        {
            MainWindow = new MainWindow();
            LogStartup("MainWindow ctor done");
            MainWindow.Activate();
            LogStartup("MainWindow activated");
        }
        catch (Exception ex)
        {
            LogCrash("OnLaunched / new MainWindow", ex);
            throw;
        }
    }

    // ---- forensics -------------------------------------------------------

    private static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirName);

    /// <summary>Append a step marker to startup.log. Used to find where a silent exit happened.</summary>
    public static void LogStartup(string step)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "startup.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + step + Environment.NewLine);
        }
        catch { /* logging must never crash the app */ }
    }

    /// <summary>Write the full inner-exception chain to crash.log.</summary>
    public static void LogCrash(string where, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("===== Crash @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");
            sb.AppendLine("Where: " + where);

            Exception? cur = ex;
            int depth = 0;
            while (cur != null)
            {
                sb.AppendLine(new string('-', 40));
                sb.AppendLine($"[{depth}] {cur.GetType().FullName}: {cur.Message}");
                sb.AppendLine("HResult: 0x" + cur.HResult.ToString("X8"));
                sb.AppendLine(cur.StackTrace);
                cur = cur.InnerException;
                depth++;
            }

            File.AppendAllText(Path.Combine(DataDir, "crash.log"), sb.ToString());
        }
        catch { /* ignore */ }
    }
}
