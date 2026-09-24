using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Zheli.Domain;

namespace Zheli.DesignSystem;

public static class Ui
{
    private sealed record TextMetric(double Size);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock,TextMetric> Sizes=new();
    private static double _textScale=1;
    public const double Radius=16,Gap=12;
    public static readonly FontFamily Font=new("PingFang SC, Microsoft YaHei UI, Segoe UI");
    public static SolidColorBrush Brush(string hex,byte alpha=255)
    {
        hex=hex.TrimStart('#');
        return new(Windows.UI.Color.FromArgb(alpha,Convert.ToByte(hex[..2],16),Convert.ToByte(hex.Substring(2,2),16),Convert.ToByte(hex.Substring(4,2),16)));
    }
    public static TextBlock Text(string text,double size=14,bool bold=false)
    {
        var block=new TextBlock { Text=text,FontSize=size*_textScale,FontFamily=Font,TextWrapping=TextWrapping.Wrap,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal };
        Sizes.Add(block,new(size));return block;
    }
    public static void ApplyTextScale(DependencyObject root,double scale)
    {
        _textScale=Math.Clamp(scale,.9,1.5);
        if(root is TextBlock text && Sizes.TryGetValue(text,out var metric))text.FontSize=metric.Size*_textScale;
        if(root is Control control)control.FontSize=14*_textScale;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)ApplyTextScale(VisualTreeHelper.GetChild(root,i),scale);
    }
    public static Button Button(string label,Action action)
    {
        var b=new Button { Content=label,MinHeight=36,FontSize=14*_textScale,CornerRadius=new CornerRadius(10),FontFamily=Font,Padding=new Thickness(12,7,12,7) };
        AutomationProperties.SetName(b,label); ToolTipService.SetToolTip(b,label); b.Click+=(_,_)=>action(); return b;
    }
    // SVG assets adapted from microsoft/fluentui-system-icons (MIT); see Assets/Fluent/LICENSE.
    // Keep the visible label so the action remains usable if an asset cannot load.
    public static Button IconButton(string label,string icon,Action action)
    {
        var button=Button(label,action);
        var row=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center};
        var path=Path.Combine(AppContext.BaseDirectory,"Assets","Fluent",icon+".svg");
        var source=new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource();
        row.Children.Add(new Image {Source=source,Width=18,Height=18});
        row.Children.Add(Text(label,14));
        button.Content=row;
        _=LoadIconAsync(source,path);
        return button;
    }
    private static async Task LoadIconAsync(Microsoft.UI.Xaml.Media.Imaging.SvgImageSource source,string path)
    {
        try
        {
            using var stream=await Windows.Storage.Streams.FileRandomAccessStream.OpenAsync(path,Windows.Storage.FileAccessMode.Read);
            await source.SetSourceAsync(stream);
        }
        catch { /* the button keeps its visible text if the optional icon cannot load */ }
    }
    public static TextBox Input(string header,string text="") => new()
    { Header=header,Text=text,FontFamily=Font,FontSize=14*_textScale,MinWidth=120,HorizontalAlignment=HorizontalAlignment.Stretch };
    public static StackPanel Stack(params UIElement[] children)
    { var s=new StackPanel {Spacing=Gap}; foreach(var c in children)s.Children.Add(c); return s; }
    public static StackPanel Row(params UIElement[] children)
    { var s=Stack(children); s.Orientation=Orientation.Horizontal; return s; }
    public static Border Card(UIElement child) => new()
    { Child=child,Padding=new Thickness(20),CornerRadius=new CornerRadius(Radius),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(14,120,140,155)) };
    public static FrameworkElement Field(string label,UIElement input)=>Stack(Text(label,14,true),input);
    public static ScrollViewer Scroll(UIElement child)=>new() {Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
}

public sealed class GlassPanel : UserControl
{
    private readonly Border _surface=new();
    public GlassPanel(UIElement child)
    {
        _surface.Child=child; _surface.CornerRadius=new CornerRadius(Ui.Radius); _surface.BorderThickness=new Thickness(1); _surface.Padding=new Thickness(12);
        Content=_surface;HorizontalContentAlignment=HorizontalAlignment.Stretch;VerticalContentAlignment=VerticalAlignment.Stretch;
    }
    public void Apply(Preferences prefs,bool dark,bool highContrast)
    {
        var system=new Windows.UI.ViewManagement.UISettings();
        _surface.BorderBrush=highContrast ? new SolidColorBrush(system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)) : Ui.Brush(dark?"AEBCCB":"FFFFFF",85);
        if(highContrast || prefs.ReduceTransparency)
        { _surface.Background=highContrast?new SolidColorBrush(system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)):Ui.Brush(dark?"242A32":"F8F8F6"); return; }
        _surface.Background=new AcrylicBrush
        {
            TintColor=Ui.Brush(dark?"242D38":"F8FAFC").Color,
            TintOpacity=Math.Clamp(1-prefs.GlassClarity,.18,.95),
            FallbackColor=Ui.Brush(dark?"242D38":"F4F6F8").Color
        };
    }
}
