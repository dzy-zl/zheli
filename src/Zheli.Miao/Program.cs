using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Zheli.Miao;
public static class Program
{
    [STAThread] public static void Main()
    {
        using var presence=Zheli.Bridge.AppPaths.HoldRuntimePresence();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if(!Zheli.DesignSystem.AppActivation.IsPrimary("Miao"))return;
        Application.Start(_=>{SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));new MiaoApp();});
    }
}
public sealed class MiaoApp:Application
{
    private Window? _window;
    public MiaoApp()=>Resources.MergedDictionaries.Add(new XamlControlsResources());
    protected override void OnLaunched(LaunchActivatedEventArgs args){var window=new MiaoWindow();_window=window;Zheli.DesignSystem.AppActivation.Bind(window,window.AcceptContext);_window.Activate();}
}
