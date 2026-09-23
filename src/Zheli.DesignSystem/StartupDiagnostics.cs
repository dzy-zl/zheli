using Microsoft.UI.Xaml;

namespace Zheli.DesignSystem;

// Opt-in only, used by the isolated Windows installer runner. No normal-user telemetry.
public static class StartupDiagnostics
{
    public static void Attach(Application app)
    {
        var directory=Environment.GetEnvironmentVariable("ZHELI_STARTUP_DIAGNOSTICS");
        if(string.IsNullOrWhiteSpace(directory))return;
        void Write(Exception exception)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory,$"startup-{Environment.ProcessId}.txt"),
                    $"{DateTimeOffset.UtcNow:O} {AppDomain.CurrentDomain.FriendlyName}\n{exception}\n");
            }
            catch { /* Diagnostics must never replace the original failure. */ }
        }
        app.UnhandledException+=(_,args)=>Write(args.Exception);
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>
        {if(args.ExceptionObject is Exception exception)Write(exception);};
    }
}
