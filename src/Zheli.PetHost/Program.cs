using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Zheli.Bridge;
using Zheli.Domain;
using Zheli.Storage;
using Forms=System.Windows.Forms;

namespace Zheli.PetHost;

public static class Program
{
    [STAThread] public static void Main()
    {
        using var presence=AppPaths.HoldRuntimePresence();
        using var singleton=new Mutex(true,AppPaths.Pipe("pet")+"-singleton",out var created);
        if(!created)return;
        new Application().Run(new PetWindow());
    }
}

public sealed class PetWindow:Window
{
    private readonly Forms.NotifyIcon _tray;
    private readonly DispatcherTimer _settingsTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer _visibilityTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly DispatcherTimer _idleTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    private readonly ScaleTransform _breath=new(1,1),_blink=new(1,1);
    private readonly RotateTransform _leftEar=new(0,12,46),_rightEar=new(0,40,46),_tail=new(0,109,129);
    private readonly PetPointerGesture _gesture=new();
    private readonly CancellationTokenSource _lifetime=new();
    private readonly BridgeClient _core=new("core","Zheli.CoreHost");
    private readonly DraftStore<PetLocalState> _stateStore=new(System.IO.Path.Combine(AppPaths.DataRoot,"Miao","pet-window.json"));
    private PetLocalState _state=new();
    private bool _polling,_dragging,_restoring,_restoreQueued,_closed,_ready,_animated,_saveWarning,_readOnlyState;
    private int _idleCount;
    private HwndSource? _source;
    private IntPtr _handle;
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr h,out RectNative r);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr h,StringBuilder name,int size);
    [StructLayout(LayoutKind.Sequential)]private struct RectNative{public int Left,Top,Right,Bottom;}

    public PetWindow()
    {
        try
        {
            var saved=_stateStore.Load();
            if(saved?.Version==1)_state=saved;
            else if(saved!=null)_readOnlyState=true; // Preserve an unknown future state file.
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){_saveWarning=true;}
        Title="哲喵桌宠";Width=156;Height=162;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;Topmost=true;ResizeMode=ResizeMode.NoResize;ShowActivated=false;
        // Avoid a startup flash before restoring visibility and monitor placement.
        Opacity=0;
        Left=SystemParameters.WorkArea.Right-Width-24;Top=SystemParameters.WorkArea.Bottom-Height-24;
        Content=BuildCharacter();
        MouseLeftButtonDown+=(_,e)=>
        {
            var dpi=VisualTreeHelper.GetDpi(this);
            _gesture.Begin(Pointer(e),SystemParameters.MinimumHorizontalDragDistance*dpi.DpiScaleX,SystemParameters.MinimumVerticalDragDistance*dpi.DpiScaleY);
            CaptureMouse();e.Handled=true;
        };
        MouseMove+=(_,e)=>
        {
            if(e.LeftButton!=MouseButtonState.Pressed||_dragging||!_gesture.Move(Pointer(e)))return;
            _dragging=true;ReleaseMouseCapture();SetAnimations(false);
            try{DragMove();}catch(InvalidOperationException){}
            finally{_dragging=false;_gesture.Cancel();ClampAndRemember(true);ApplyVisibility();}
        };
        MouseLeftButtonUp+=(_,e)=>
        {
            var click=_gesture.Release(Pointer(e));ReleaseMouseCapture();e.Handled=true;
            if(click)OpenChat();
        };
        LostMouseCapture+=(_,_)=>{if(!_dragging)_gesture.Cancel();};
        var menu=new ContextMenu();
        AddMenu(menu,"打开对话",OpenChat);AddMenu(menu,"哲里设置",()=>Launch("Zheli.Settings"));
        AddMenu(menu,"移回主屏右下角",ResetPosition);
        AddMenu(menu,"隐藏桌宠",()=>{_state=_state with{HiddenByUser=true};SaveState();ApplyVisibility();});
        AddMenu(menu,"退出桌宠",Close);ContextMenu=menu;
        _tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="哲喵 · 双击打开对话",Visible=true};
        var trayMenu=new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开对话",null,(_,_)=>Dispatcher.Invoke(OpenChat));
        trayMenu.Items.Add("显示桌宠",null,(_,_)=>Dispatcher.Invoke(ShowFromTray));
        trayMenu.Items.Add("移回主屏右下角",null,(_,_)=>Dispatcher.Invoke(ResetPosition));
        trayMenu.Items.Add("哲里设置",null,(_,_)=>Dispatcher.Invoke(()=>Launch("Zheli.Settings")));
        trayMenu.Items.Add("退出桌宠",null,(_,_)=>Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip=trayMenu;_tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(OpenChat);
        SourceInitialized+=(_,_)=>
        {
            _handle=new WindowInteropHelper(this).Handle;_source=HwndSource.FromHwnd(_handle);_source.AddHook(Hook);
            if(!RegisterHotKey(_handle,1,0x4000|0x1|0x2,0x20))Notify("Ctrl+Alt+Space已被占用，可通过桌宠或托盘打开。");
        };
        Loaded+=async(_,_)=>
        {
            if(_ready)return;_ready=true;QueueRestore();ApplyVisibility();
            if(_readOnlyState)Notify("桌宠位置文件来自较新版本，本次不会覆盖该文件。");
            else if(_saveWarning)Notify("未能读取上次桌宠位置，已使用默认位置。");
            await PollSettings();
        };
        _settingsTimer.Tick+=async(_,_)=>await PollSettings();
        _visibilityTimer.Tick+=(_,_)=>ApplyVisibility();
        _idleTimer.Tick+=(_,_)=>IdleMotion();
        _settingsTimer.Start();_visibilityTimer.Start();
        Closed+=(_,_)=>
        {
            _closed=true;_lifetime.Cancel();_settingsTimer.Stop();_visibilityTimer.Stop();SetAnimations(false);
            SaveState();UnregisterHotKey(_handle,1);if(_source!=null)_source.RemoveHook(Hook);
            _tray.Dispose();_lifetime.Dispose();Application.Current.Shutdown();
        };
    }

    private Border BuildCharacter()
    {
        var canvas=new Canvas{Width=150,Height=150,Background=Brushes.Transparent};
        var blue=new SolidColorBrush(Color.FromRgb(168,205,228));var white=new SolidColorBrush(Color.FromRgb(251,250,243));var ink=new SolidColorBrush(Color.FromRgb(77,84,98));
        // Temporary native vector character. This does not replace the approved mascot artwork.
        ShapeAt(canvas,new System.Windows.Shapes.Path{Data=Geometry.Parse("M109,129 C137,137 148,111 138,102"),Stroke=blue,StrokeThickness=17,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,RenderTransform=_tail},0,0);
        ShapeAt(canvas,new Ellipse{Width=85,Height=82,Fill=white},33,64);
        ShapeAt(canvas,new Polygon{Points=new PointCollection{new(0,53),new(4,0),new(50,27)},Fill=blue,RenderTransform=_leftEar},15,8);
        ShapeAt(canvas,new Polygon{Points=new PointCollection{new(0,27),new(47,0),new(52,59)},Fill=blue,RenderTransform=_rightEar},86,8);
        ShapeAt(canvas,new Ellipse{Width=132,Height=106,Fill=blue},9,25);
        ShapeAt(canvas,new Ellipse{Width=119,Height=70,Fill=white},15,61);
        ShapeAt(canvas,new Ellipse{Width=12,Height=15,Fill=ink,RenderTransform=_blink,RenderTransformOrigin=new Point(.5,.5)},46,71);
        ShapeAt(canvas,new Ellipse{Width=12,Height=15,Fill=ink,RenderTransform=_blink,RenderTransformOrigin=new Point(.5,.5)},94,71);
        ShapeAt(canvas,new Ellipse{Width=22,Height=12,Fill=new SolidColorBrush(Color.FromRgb(247,192,195))},24,86);
        ShapeAt(canvas,new Ellipse{Width=22,Height=12,Fill=new SolidColorBrush(Color.FromRgb(247,192,195))},109,86);
        var mouth=new TextBlock{Text="ω",FontSize=21,Foreground=ink};Canvas.SetLeft(mouth,65);Canvas.SetTop(mouth,80);canvas.Children.Add(mouth);
        var body=new Border{Child=canvas,RenderTransform=_breath,RenderTransformOrigin=new Point(.5,1),ToolTip="哲喵 · 点击对话 · 拖动移动 · 右键菜单"};
        System.Windows.Automation.AutomationProperties.SetName(body,"哲喵桌宠，点击对话，拖动移动；也可使用托盘或Ctrl+Alt+Space打开对话");
        return body;
    }
    private async Task PollSettings()
    {
        if(_polling||_closed)return;_polling=true;
        try
        {
            var prefs=(await _core.Call<Snapshot<Preferences>>("settings.read",ct:_lifetime.Token)).Data;
            if(_closed)return;
            var next=_state with{LastShowPet=prefs.ShowPet,LastReduceMotion=prefs.ReduceMotion,
                HiddenByUser=prefs.ShowPet&&!_state.LastShowPet?false:_state.HiddenByUser};
            if(next!=_state){_state=next;SaveState();}
        }
        catch{ /* Cached settings keep working when the settings host is unavailable. */ }
        finally{_polling=false;if(!_closed)ApplyVisibility();}
    }
    private void ApplyVisibility()
    {
        if(_closed||!_ready||_dragging)return;
        var visible=!_state.HiddenByUser&&_state.LastShowPet&&!ForegroundFullscreen();
        if(!visible&&IsVisible)Hide();else if(visible&&!IsVisible)Show();
        if(!_restoring&&!_restoreQueued)Opacity=1;
        SetAnimations(visible&&!_state.LastReduceMotion&&SystemParameters.ClientAreaAnimation&&!SystemParameters.IsRemoteSession&&(RenderCapability.Tier>>16)>0);
    }
    private void ShowFromTray()
    {
        _state=_state with{HiddenByUser=false};SaveState();QueueRestore();ApplyVisibility();
        if(!_state.LastShowPet)Notify("已解除临时隐藏；请在哲里设置中开启“显示桌面哲喵”。");
        else if(ForegroundFullscreen())Notify("当前显示器有全屏应用，退出全屏后哲喵会重新出现。");
    }
    private void SetAnimations(bool enabled)
    {
        if(_animated==enabled)return;_animated=enabled;
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty,null);_blink.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        _leftEar.BeginAnimation(RotateTransform.AngleProperty,null);_rightEar.BeginAnimation(RotateTransform.AngleProperty,null);_tail.BeginAnimation(RotateTransform.AngleProperty,null);
        _idleTimer.Stop();
        if(!enabled)return;
        var breath=new DoubleAnimation(1,.985,TimeSpan.FromSeconds(2.6)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        var tail=new DoubleAnimation(-3,3,TimeSpan.FromSeconds(3.2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(breath,30);Timeline.SetDesiredFrameRate(tail,30);
        _breath.BeginAnimation(ScaleTransform.ScaleYProperty,breath);_tail.BeginAnimation(RotateTransform.AngleProperty,tail);
        _idleTimer.Interval=TimeSpan.FromSeconds(Random.Shared.NextDouble()*3+4);_idleTimer.Start();
    }
    private void IdleMotion()
    {
        if(!_animated)return;
        var blink=new DoubleAnimationUsingKeyFrames{FillBehavior=FillBehavior.Stop};
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(.08,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(.08,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130))));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240))));
        Timeline.SetDesiredFrameRate(blink,30);_blink.BeginAnimation(ScaleTransform.ScaleYProperty,blink);
        if(++_idleCount%2==0)
        {
            var ear=new DoubleAnimation(0,_idleCount%4==0?-4:4,TimeSpan.FromMilliseconds(240)){AutoReverse=true,FillBehavior=FillBehavior.Stop,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
            Timeline.SetDesiredFrameRate(ear,30);(_idleCount%4==0?_leftEar:_rightEar).BeginAnimation(RotateTransform.AngleProperty,ear);
        }
        _idleTimer.Interval=TimeSpan.FromSeconds(Random.Shared.NextDouble()*3+4);
    }
    private PetPoint Pointer(MouseEventArgs e){var p=PointToScreen(e.GetPosition(this));return new((int)Math.Round(p.X),(int)Math.Round(p.Y));}
    private static PetArea Area(Forms.Screen screen){var r=screen.WorkingArea;return new(r.Left,r.Top,r.Width,r.Height);}
    private int MarginPixels=>(int)Math.Round(12*VisualTreeHelper.GetDpi(this).DpiScaleX);
    private void QueueRestore()
    {
        if(!_ready||_closed||_dragging||_restoring||_restoreQueued)return;
        _restoreQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            _restoreQueued=false;if(_closed||_dragging)return;_restoring=true;
            ApplyPlacement();
            // Moving across DPI boundaries can change the window size. Recompute after WPF handles WM_DPICHANGED.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
            {
                if(!_closed&&!_dragging){ApplyPlacement();ClampAndRemember(false);}
                _restoring=false;if(!_closed){Opacity=1;ApplyVisibility();}
            }));
        }));
    }
    private void ApplyPlacement()
    {
        if(!GetWindowRect(_handle,out var rect))return;
        var displays=Forms.Screen.AllScreens.Select(s=>new PetDisplay(s.DeviceName,Area(s),s.Primary)).ToArray();
        if(displays.Length==0)return;
        var screen=PetDesktop.SelectDisplay(displays,_state.Placement?.Display);
        var p=PetDesktop.Restore(_state.Placement,screen.WorkArea,rect.Right-rect.Left,rect.Bottom-rect.Top,MarginPixels);
        MovePixels(p);
    }
    private void ClampAndRemember(bool snap)
    {
        if(_closed||_handle==IntPtr.Zero||!GetWindowRect(_handle,out var r))return;
        var screen=Forms.Screen.FromHandle(_handle);var area=Area(screen);var width=r.Right-r.Left;var height=r.Bottom-r.Top;
        var p=PetDesktop.Constrain(new(r.Left,r.Top),area,width,height,MarginPixels,snap?MarginPixels*2:0);
        if(!MovePixels(p))return;
        _state=_state with{Placement=PetDesktop.Capture(screen.DeviceName,p,area,width,height,MarginPixels)};SaveState();
    }
    private bool MovePixels(PetPoint p)
    {
        // Keep the existing size, Z order and focus; all coordinates are physical pixels under PerMonitorV2.
        if(SetWindowPos(_handle,IntPtr.Zero,p.X,p.Y,0,0,0x0001|0x0004|0x0010))return true;
        Notify("未能移动桌宠，可从托盘重试“移回主屏右下角”。");return false;
    }
    private void ResetPosition(){_state=_state with{Placement=null};SaveState();QueueRestore();}
    private void SaveState()
    {
        if(_readOnlyState)return;
        try{_stateStore.Save(_state);_saveWarning=false;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException)
        {if(!_saveWarning&&!_closed)Notify("桌宠位置暂未保存，请检查数据目录是否可写。");_saveWarning=true;}
    }
    private bool ForegroundFullscreen()
    {
        var foreground=GetForegroundWindow();if(foreground==IntPtr.Zero||foreground==_handle||!GetWindowRect(foreground,out var r))return false;
        var name=new StringBuilder(128);GetClassName(foreground,name,name.Capacity);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")return false;
        var screen=Forms.Screen.FromHandle(foreground);
        if(!string.Equals(screen.DeviceName,Forms.Screen.FromHandle(_handle).DeviceName,StringComparison.OrdinalIgnoreCase))return false;
        var bounds=screen.Bounds;
        return r.Left<=bounds.Left&&r.Top<=bounds.Top&&r.Right>=bounds.Right&&r.Bottom>=bounds.Bottom;
    }
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
    {
        if(msg==0x312&&w.ToInt32()==1){OpenChat();handled=true;}
        else if(msg is 0x007E or 0x02E0 || (msg==0x001A&&w.ToInt64()==47))QueueRestore();
        return IntPtr.Zero;
    }
    private static void ShapeAt(Canvas c,Shape s,double x,double y){Canvas.SetLeft(s,x);Canvas.SetTop(s,y);c.Children.Add(s);}
    private static void AddMenu(ContextMenu c,string label,Action action){var m=new MenuItem{Header=label};m.Click+=(_,_)=>action();c.Items.Add(m);}
    private void Notify(string text)=>_tray.ShowBalloonTip(4000,"哲喵",text,Forms.ToolTipIcon.Info);
    private void OpenChat()=>Launch("Zheli.Miao");
    private void Launch(string app){try{AppPaths.Launch(app);}catch(Exception e){_tray.ShowBalloonTip(4000,"哲喵",e.Message,Forms.ToolTipIcon.Warning);}}
}
