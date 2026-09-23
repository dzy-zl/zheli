using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Zheli.Bridge;
using Zheli.Domain;
using Zheli.Storage;

namespace Zheli.DesignSystem;

public class ShellWindow : Window
{
    protected readonly Grid Root=new();
    protected readonly StackPanel Navigation=new() {Spacing=8};
    protected readonly ContentControl Workspace=new() {HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    protected readonly StackPanel Toolbar=new() {Orientation=Orientation.Horizontal,Spacing=8};
    protected readonly TextBlock Status=Ui.Text("本地数据 · 开发版本 0.2.3");
    protected readonly Grid Body=new();
    protected Preferences Appearance=new();
    protected readonly BridgeClient Core=new("core","Zheli.CoreHost");
    private readonly List<WeakReference<GlassPanel>> _glass=[];
    private readonly ColumnDefinition _navColumn=new(){Width=new GridLength(224)};
    private readonly DispatcherTimer _themeTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly string _windowFile;
    private readonly DraftStore<Preferences> _appearanceCache;
    private bool _polling,_closed,_sidebar=true;
    private string? _lastAppearance;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility=new();
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings=new();

    public ShellWindow(string app,string title)
    {
        Title=title;
        _windowFile=Path.Combine(AppPaths.DataRoot,app,"window.json");
        _appearanceCache=new(Path.Combine(AppPaths.DataRoot,app,"appearance-cache.json"));
        try{Appearance=_appearanceCache.Load()??new();}catch{Appearance=new();}
        ExtendsContentIntoTitleBar=true;
        Root.RowDefinitions.Add(new(){Height=new GridLength(44)});
        Root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var titleBar=Ui.Row(Ui.Text(title,14,true)); titleBar.Margin=new Thickness(24,10,150,0);
        Root.Children.Add(titleBar); SetTitleBar(titleBar);
        Body.Margin=new Thickness(16,0,16,16); Body.ColumnSpacing=12;
        Body.ColumnDefinitions.Add(_navColumn); Body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetRow(Body,1); Root.Children.Add(Body);
        Navigation.Children.Add(Ui.Text(title,20,true));
        Navigation.Children.Add(Ui.Button("☰ 收起 / 展开",()=>{_sidebar=!_sidebar;ResizeLayout();}));
        var sidebar=Glass(Ui.Scroll(Navigation)); Body.Children.Add(sidebar);
        var main=new Grid {RowSpacing=12}; Grid.SetColumn(main,1); Body.Children.Add(main);
        main.RowDefinitions.Add(new(){Height=GridLength.Auto});
        main.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        main.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var toolbar=Glass(new ScrollViewer {Content=Toolbar,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        main.Children.Add(toolbar); Grid.SetRow(Workspace,1); main.Children.Add(Workspace);
        var status=Glass(Status); Grid.SetRow(status,2); main.Children.Add(status);
        Content=Root;
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        var dpi=NativeDpi.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))/96d;
        var initialWidth=Math.Min(area.Width-32,(int)(1360*dpi));var initialHeight=Math.Min(area.Height-32,(int)(860*dpi));
        AppWindow.Resize(new SizeInt32(initialWidth,initialHeight));
        AppWindow.Move(new PointInt32(area.X+(area.Width-initialWidth)/2,area.Y+(area.Height-initialHeight)/2));
        RestoreWindow(area);
        if(AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth=760;
        Root.SizeChanged+=(_,_)=>ResizeLayout();
        Root.ActualThemeChanged+=(_,_)=>ApplyTheme();
        Root.KeyDown+=OnKey;
        Activated+=(_,_)=>{if(!_closed)Guard(PollTheme);};
        _themeTimer.Tick+=(_,_)=>Guard(PollTheme); _themeTimer.Start();
        Closed+=(_,_)=>{_closed=true;_themeTimer.Stop();SaveWindow();};
        ApplyTheme();
    }
    protected GlassPanel Glass(UIElement child)
    { var panel=new GlassPanel(child);_glass.Add(new(panel));panel.Apply(Appearance,Root.ActualTheme==ElementTheme.Dark,_accessibility.HighContrast);return panel; }
    protected void Nav(string label,Action action)=>Navigation.Children.Add(Ui.Button(label,action));
    protected void AddGlobalNavigation()
    {
        Nav("哲喵",()=>Guard(()=>{AppPaths.Launch("Zheli.Miao");return Task.CompletedTask;}));
        Nav("哲里设置",()=>Guard(()=>{AppPaths.Launch("Zheli.Settings");return Task.CompletedTask;}));
    }
    protected async void Guard(Func<Task> action)
    {
        try { await action(); }
        catch(OperationCanceledException) {Status.Text="操作已停止；已保存的数据保持不变。";}
        catch(Exception e) {Status.Text=e.Message;}
    }
    protected virtual void OnKey(object sender,KeyRoutedEventArgs e) { }
    protected virtual void AppearanceChanged() { }
    protected bool AnimationsAllowed=>!Appearance.ReduceMotion&&_uiSettings.AnimationsEnabled;
    private async Task PollTheme()
    {
        if(_polling||_closed)return; _polling=true;
        try
        {
            var value=await Core.Call<Snapshot<Preferences>>("settings.read");
            var serialized=JsonSerializer.Serialize(value.Data);
            if(serialized==_lastAppearance)return;
            _lastAppearance=serialized; Appearance=value.Data;ApplyTheme();AppearanceChanged();
            try{_appearanceCache.Save(Appearance with{KnowledgeFolders=[]});}catch{ /* cache is not authoritative */ }
        }
        catch { /* app remains usable with cached in-memory defaults; status must not falsely claim service connected */ }
        finally {_polling=false;}
    }
    protected void ApplyTheme()
    {
        Root.RequestedTheme=Appearance.Theme switch {"Light"=>ElementTheme.Light,"Dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
        var dark=Root.ActualTheme==ElementTheme.Dark;
        Root.Background=_accessibility.HighContrast?new SolidColorBrush(_uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)):Ui.Brush(dark?"171D25":"F5F4F0");
        Ui.ApplyTextScale(Root,Appearance.TextScale);
        foreach(var weak in _glass.ToArray())
        {if(weak.TryGetTarget(out var panel))panel.Apply(Appearance,dark,_accessibility.HighContrast);else _glass.Remove(weak);}
    }
    private void ResizeLayout()
    {
        _navColumn.Width=new GridLength(Root.ActualWidth<960||!_sidebar?68:224);
        foreach(var item in Navigation.Children.OfType<Button>())
        {
            if(item.Tag is not string full){full=item.Content.ToString()!;item.Tag=full;}
            item.Content=_navColumn.Width.Value<100?full[..Math.Min(2,full.Length)]:full;
        }
    }
    protected async Task<bool> Confirm(string title,string content)
    {
        var dialog=new ContentDialog {Title=title,Content=Ui.Text(content),PrimaryButtonText="确认",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme};
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    protected async Task<bool> Form(string title,UIElement content,string primary="保存")
    {
        var dialog=new ContentDialog {Title=title,Content=Ui.Scroll(content),PrimaryButtonText=primary,CloseButtonText="取消",XamlRoot=Root.XamlRoot,RequestedTheme=Root.ActualTheme};
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    protected void Fade(FrameworkElement element)
    {
        if(!AnimationsAllowed)return;
        var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.InsertKeyFrame(0,.65f);animation.InsertKeyFrame(1,1);animation.Duration=TimeSpan.FromMilliseconds(180);
        visual.StartAnimation("Opacity",animation);
    }
    private void RestoreWindow(RectInt32 area)
    {
        try
        {
            if(!File.Exists(_windowFile))return;
            var saved=JsonSerializer.Deserialize<Placement>(File.ReadAllText(_windowFile))!;
            _sidebar=saved.Sidebar;
            var w=Math.Clamp(saved.Width,760,Math.Max(760,area.Width)); var h=Math.Clamp(saved.Height,600,Math.Max(600,area.Height));
            AppWindow.MoveAndResize(new RectInt32(Math.Clamp(saved.X,area.X,Math.Max(area.X,area.X+area.Width-w)),Math.Clamp(saved.Y,area.Y,Math.Max(area.Y,area.Y+area.Height-h)),w,h));
            if(saved.Maximized && AppWindow.Presenter is OverlappedPresenter p)p.Maximize();
        }
        catch {Status.Text="上次窗口位置不可用，已恢复默认布局。";}
    }
    private void SaveWindow()
    {
        try
        {
            var placement=new Placement(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height,
                AppWindow.Presenter is OverlappedPresenter p && p.State==OverlappedPresenterState.Maximized,_sidebar);
            Directory.CreateDirectory(Path.GetDirectoryName(_windowFile)!);
            File.WriteAllText(_windowFile+".tmp",JsonSerializer.Serialize(placement));File.Move(_windowFile+".tmp",_windowFile,true);
        }
        catch { /* window geometry is non-critical; never block closing */ }
    }
    private sealed record Placement(int X,int Y,int Width,int Height,bool Maximized,bool Sidebar);
    private static class NativeDpi
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]internal static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
