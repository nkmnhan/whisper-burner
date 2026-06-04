namespace WhisperBurner.WinUI;

internal static class Program
{
    internal static readonly string StartupLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.burner", "startup.log");

    [STAThread]
    private static void Main(string[] args)
    {
        Trace("Main() entered");
        try
        {
            Trace("Bootstrap TryInitialize...");
            bool bootstrapOk = Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap
                .TryInitialize(0x00020001, out int bootstrapHr);
            Trace($"Bootstrap: ok={bootstrapOk} hr=0x{bootstrapHr:X8}");
            if (!bootstrapOk)
            {
                Trace("FATAL: Bootstrap failed");
                throw new InvalidOperationException($"WinAppSDK bootstrap failed: 0x{bootstrapHr:X8}");
            }

            Trace("InitializeComWrappers...");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Trace("InitializeComWrappers done");

            Trace("Application.Start...");
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                Trace("Application.Start callback: setting SynchronizationContext");
                try
                {
                    var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    Trace("Application.Start callback: new App()...");
                    _ = new App();
                    Trace("Application.Start callback: new App() returned");
                }
                catch (Exception ex)
                {
                    Trace($"FATAL in Application.Start callback:\n{ex}");
                    throw;
                }
            });
            Trace("Application.Start returned (app exited normally)");
        }
        catch (Exception ex)
        {
            Trace($"FATAL in Main:\n{ex}");
            throw;
        }
    }

    internal static void Trace(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLog)!);
            File.AppendAllText(StartupLog,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
