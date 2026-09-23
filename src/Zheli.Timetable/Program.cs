using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Zheli.Timetable;
public static class Program
{
    [STAThread] public static void Main()
    {
        using var presence=Zheli.Bridge.AppPaths.HoldRuntimePresence();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if(!Zheli.DesignSystem.AppActivation.IsPrimary("Timetable"))return;
        Application.Start(_=>{SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));new TimetableApp();});
    }
}
public sealed class TimetableApp:Application
{
    private Window? _window;
    public TimetableApp(){Zheli.DesignSystem.StartupDiagnostics.Attach(this);Resources.MergedDictionaries.Add(new XamlControlsResources());}
    protected override void OnLaunched(LaunchActivatedEventArgs args){_window=new TimetableWindow();Zheli.DesignSystem.AppActivation.Bind(_window);_window.Activate();}
}
