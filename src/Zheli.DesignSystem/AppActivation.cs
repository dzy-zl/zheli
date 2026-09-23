using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Zheli.Bridge;

namespace Zheli.DesignSystem;

// One UI process per app and Windows user. Redirection is separate from business IPC.
public static class AppActivation
{
    private static readonly object Gate=new();
    private static readonly Queue<string?> Pending=new();
    private static AppInstance? _instance;
    private static Window? _window;
    private static Action<string?>? _context;

    public static bool IsPrimary(string app)
    {
        _instance=AppInstance.FindOrRegisterForKey(AppPaths.Pipe(app+"-ui"));
        if(_instance.IsCurrent)
        {
            _instance.Activated+=(_,args)=>Enqueue(args);
            return true;
        }
        var activation=AppInstance.GetCurrent().GetActivatedEventArgs();
        // Pump COM while redirection runs on the thread pool, as required for an STA entry point.
        var done=new ManualResetEvent(false);
        var redirect=Task.Run(async()=>
        {
            try{await _instance.RedirectActivationToAsync(activation);}
            finally{done.Set();}
        });
        var hr=CoWaitForMultipleHandles(0,30000,1,[done.SafeWaitHandle.DangerousGetHandle()],out _);
        if(hr==0)
        {
            done.Dispose();redirect.GetAwaiter().GetResult();
            try{using var process=Process.GetProcessById((int)_instance.ProcessId);SetForegroundWindow(process.MainWindowHandle);}catch(ArgumentException){}
        }
        else
        {
            _=redirect.ContinueWith(_=>done.Dispose(),TaskScheduler.Default);
            throw new TimeoutException("原窗口暂未响应，请稍后重试。");
        }
        return false;
    }
    public static void Bind(Window window,Action<string?>? onContext=null)
    {
        lock(Gate){_window=window;_context=onContext;}
        Flush();
    }
    private static void Enqueue(AppActivationArguments args)
    {
        string? encoded=null;
        if(args.Kind==ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs launch)
        {
            var match=Regex.Match(launch.Arguments??"",@"(?:^|\s)--context\s+([A-Za-z0-9+/=]{1,4096})(?:\s|$)");
            if(match.Success)encoded=match.Groups[1].Value;
        }
        lock(Gate){if(Pending.Count<16)Pending.Enqueue(encoded);}
        Flush();
    }
    private static void Flush()
    {
        Window? window;lock(Gate)window=_window;if(window==null)return;
        window.DispatcherQueue.TryEnqueue(()=>
        {
            while(true)
            {
                string? encoded;lock(Gate){if(Pending.Count==0)break;encoded=Pending.Dequeue();}
                _context?.Invoke(encoded);
            }
            if(window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p && p.State==Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)p.Restore();
            window.Activate();
        });
    }
    [DllImport("ole32.dll",EntryPoint="CoWaitForMultipleHandles")]
    private static extern int CoWaitForMultipleHandles(uint flags,uint milliseconds,uint count,IntPtr[] handles,out uint index);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
