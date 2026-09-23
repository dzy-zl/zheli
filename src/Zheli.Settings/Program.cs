using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Zheli.Settings;
public static class Program
{
    [STAThread] public static void Main()
    {
        Zheli.DesignSystem.StartupDiagnostics.Trace("Main entered");
        using var presence=Zheli.Bridge.AppPaths.HoldRuntimePresence();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Zheli.DesignSystem.StartupDiagnostics.Trace("COM wrappers ready");
        if(!Zheli.DesignSystem.AppActivation.IsPrimary("Settings"))return;
        Zheli.DesignSystem.StartupDiagnostics.Trace("Primary instance ready; starting XAML");
        Application.Start(_=>{Zheli.DesignSystem.StartupDiagnostics.Trace("XAML callback entered");SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));new SettingsApp();});
    }
}
public sealed class SettingsApp:Application
{
    private Window? _window;
    public SettingsApp(){Zheli.DesignSystem.StartupDiagnostics.Attach(this);Resources.MergedDictionaries.Add(new XamlControlsResources());Zheli.DesignSystem.StartupDiagnostics.Trace("Resources ready");}
    protected override void OnLaunched(LaunchActivatedEventArgs args){Zheli.DesignSystem.StartupDiagnostics.Trace("Creating window");_window=new SettingsWindow();Zheli.DesignSystem.StartupDiagnostics.Trace("Window created");Zheli.DesignSystem.AppActivation.Bind(_window);_window.Activate();}
}
