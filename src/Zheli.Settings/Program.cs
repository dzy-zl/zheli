using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Zheli.Settings;
public static class Program
{
    [STAThread] public static void Main()
    {
        using var presence=Zheli.Bridge.AppPaths.HoldRuntimePresence();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if(!Zheli.DesignSystem.AppActivation.IsPrimary("Settings"))return;
        Application.Start(_=>{SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));new SettingsApp();});
    }
}
public sealed class SettingsApp:Application
{
    private Window? _window;
    public SettingsApp()=>Resources.MergedDictionaries.Add(new XamlControlsResources());
    protected override void OnLaunched(LaunchActivatedEventArgs args){_window=new SettingsWindow();Zheli.DesignSystem.AppActivation.Bind(_window);_window.Activate();}
}
