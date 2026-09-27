using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Zheli.Bridge;
using Zheli.Domain;
using Zheli.Platform;
using Zheli.Storage;

namespace Zheli.PetHost;

// A lightweight companion to the full WinUI window. Both windows use the same
// versioned conversation store; the pet never holds a copy of the API key.
public sealed class QuickPanelWindow : Window
{
    private readonly Action _openFull;
    private readonly BridgeClient _core=new("core","Zheli.CoreHost");
    private readonly DocumentStore<MiaoState> _store=new(Path.Combine(AppPaths.DataRoot,"Miao","miao.db"));
    private readonly Border _surface;
    private readonly TextBlock _title,_status;
    private readonly StackPanel _messages;
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly Button _send;
    private CancellationTokenSource? _sending;
    private string? _conversationId;
    private bool _dark,_themeReady;

    public QuickPanelWindow(Action openFull)
    {
        _openFull=openFull;
        Title="哲喵 · 快捷面板";Width=400;Height=535;WindowStyle=WindowStyle.None;
        AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;
        ResizeMode=ResizeMode.NoResize;Topmost=true;
        FontFamily=new FontFamily("PingFang SC, Microsoft YaHei UI, Segoe UI");
        _surface=new Border{CornerRadius=new CornerRadius(23),BorderThickness=new Thickness(1),Padding=new Thickness(0),
            Effect=new DropShadowEffect{BlurRadius=30,ShadowDepth=12,Opacity=.22,Color=Colors.SlateGray}};
        var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid{Margin=new Thickness(18,15,14,12)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var mark=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromRgb(204,226,243)),
            Child=new TextBlock{Text="喵",FontSize=17,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(72,115,143)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        header.Children.Add(mark);
        var labels=new StackPanel{Margin=new Thickness(9,0,0,0)};Grid.SetColumn(labels,1);header.Children.Add(labels);
        _title=new TextBlock{Text="哲喵",FontSize=15,FontWeight=FontWeights.SemiBold};labels.Children.Add(_title);
        _status=new TextBlock{Text="快捷对话 · 本地保存",FontSize=11,Margin=new Thickness(0,3,0,0)};labels.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(buttons,2);header.Children.Add(buttons);
        buttons.Children.Add(HeaderButton("⤢","打开完整窗口",()=>{Hide();_openFull();}));
        buttons.Children.Add(HeaderButton("×","收起面板",StopAndHide));
        layout.Children.Add(header);
        _messages=new StackPanel{Margin=new Thickness(18,10,18,12)};
        _scroll=new ScrollViewer{Content=_messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Grid.SetRow(_scroll,1);layout.Children.Add(_scroll);
        var composer=new Grid{Margin=new Thickness(15,8,15,15)};
        composer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});composer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        composer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        composer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        _input=new TextBox{MinHeight=54,MaxHeight=90,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Padding=new Thickness(11,8,11,8),BorderThickness=new Thickness(1),FontSize=13};
        _input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&(Keyboard.Modifiers&ModifierKeys.Shift)==0){e.Handled=true;_ = Send();}};
        composer.Children.Add(_input);
        _send=new Button{Content="发送",Padding=new Thickness(12,7,12,7),Margin=new Thickness(8,0,0,0),MinWidth=57,VerticalAlignment=VerticalAlignment.Bottom};
        _send.Click+=(_,_)=>_ = Send();Grid.SetColumn(_send,1);composer.Children.Add(_send);
        var hint=new TextBlock{Text="Enter 发送 · Shift+Enter 换行 · 文件与课表请到完整窗口",FontSize=10,Margin=new Thickness(2,9,0,0)};
        Grid.SetRow(hint,1);Grid.SetColumnSpan(hint,2);composer.Children.Add(hint);
        Grid.SetRow(composer,2);layout.Children.Add(composer);
        _surface.Child=layout;Content=new Grid{Margin=new Thickness(17),Children={_surface}};
        ApplyTheme(false);
        IsVisibleChanged+=(_,_)=>{if(IsVisible){Refresh();_input.Focus();}};
        Closed+=(_,_)=>{_sending?.Cancel();_sending?.Dispose();};
    }

    public void ApplyTheme(bool dark)
    {
        if(_themeReady&&_dark==dark)return;
        _themeReady=true;
        _dark=dark;
        _surface.Background=new SolidColorBrush(dark?Color.FromArgb(241,32,47,61):Color.FromArgb(239,248,250,249));
        _surface.BorderBrush=new SolidColorBrush(dark?Color.FromArgb(74,212,236,250):Colors.White);
        Foreground=new SolidColorBrush(dark?Color.FromRgb(229,240,247):Color.FromRgb(39,62,75));
        _title.Foreground=Foreground;_status.Foreground=new SolidColorBrush(dark?Color.FromRgb(170,194,208):Color.FromRgb(105,132,147));
        _input.Background=new SolidColorBrush(dark?Color.FromRgb(45,65,80):Colors.White);
        _input.Foreground=Foreground;_input.BorderBrush=_surface.BorderBrush;
        if(_sending==null)Refresh();
    }

    private Button HeaderButton(string symbol,string hint,Action action)
    {
        var button=new Button{Content=symbol,Width=31,Height=31,Margin=new Thickness(4,0,0,0),ToolTip=hint,
            Background=Brushes.Transparent,BorderThickness=new Thickness(0),FontSize=18,Foreground=new SolidColorBrush(Color.FromRgb(104,137,158))};
        button.Click+=(_,_)=>action();return button;
    }
    private void Refresh()
    {
        if(_sending!=null)return;
        _messages.Children.Clear();
        try
        {
            var snapshot=_store.Read();
            var active=snapshot.Data.Conversations.FirstOrDefault(c=>c.Id==_conversationId&&c.DeletedAt==null)
                ??snapshot.Data.Conversations.FirstOrDefault(c=>c.Source=="quick"&&c.DeletedAt==null);
            _conversationId=active?.Id;
            if(active==null||active.Messages.Count==0)
            {
                _messages.Children.Add(new TextBlock{Text="今天想做点什么？",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=Foreground,Margin=new Thickness(0,12,0,8)});
                _messages.Children.Add(new TextBlock{Text="这里适合快速提问。课程查询、文件检索和操作记录可以在完整窗口中查看。",TextWrapping=TextWrapping.Wrap,Foreground=_status.Foreground,FontSize=12});
            }
            else foreach(var m in active.Messages.TakeLast(12))AddBubble(m.Text,m.Role=="user");
            Dispatcher.BeginInvoke(new Action(()=>_scroll.ScrollToEnd()));
        }
        catch(Exception e)when(e is IOException or StoreException or JsonException)
        {_messages.Children.Add(new TextBlock{Text="会话暂时无法读取，请打开完整窗口检查数据。",Foreground=Foreground,TextWrapping=TextWrapping.Wrap});}
    }
    private TextBlock AddBubble(string text,bool user)
    {
        var block=new TextBlock{Text=text,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Foreground};
        var card=new Border{Child=block,Background=new SolidColorBrush(user
            ?_dark?Color.FromRgb(58,91,112):Color.FromRgb(215,232,241)
            :_dark?Color.FromRgb(47,68,84):Colors.White),CornerRadius=new CornerRadius(13),Padding=new Thickness(12),
            Margin=new Thickness(user?38:0,0,user?0:38,10),HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,MaxWidth=310};
        _messages.Children.Add(card);_scroll.ScrollToEnd();return block;
    }
    private void Persist(string role,string text,bool local=false)
    {
        for(var attempt=0;attempt<3;attempt++)
        {
            var snapshot=_store.Read();
            var id=_conversationId;
            var active=snapshot.Data.Conversations.FirstOrDefault(c=>c.Id==id&&c.DeletedAt==null);
            if(active==null)
            {
                id=Guid.NewGuid().ToString("N");
                active=new Conversation(id,"快捷对话",[],null,"quick");
            }
            var updated=active with{Messages=active.Messages.Append(new Message(role,text,DateTimeOffset.Now,local)).ToList(),
                Title=active.Title=="快捷对话"&&role=="user"?text[..Math.Min(22,text.Length)]:active.Title};
            var next=snapshot.Data with{Conversations=snapshot.Data.Conversations.Any(c=>c.Id==id)
                ?snapshot.Data.Conversations.Select(c=>c.Id==id?updated:c).ToList()
                :snapshot.Data.Conversations.Prepend(updated).ToList()};
            try
            {
                _store.Write(Guid.NewGuid().ToString(),snapshot.Revision,"快捷对话消息",_=>next,DocumentStore<MiaoState>.Hash(JsonSerializer.Serialize(next)));
                _conversationId=id;return;
            }
            catch(StoreException e)when(e.Code=="STALE_VERSION"&&attempt<2){}
        }
    }
    private async Task Send()
    {
        if(_sending!=null)return;
        var question=_input.Text.Trim();if(question.Length==0)return;
        if(question.Length>4000){_status.Text="单条消息最多 4000 字";return;}
        _input.Clear();_input.IsEnabled=false;_send.IsEnabled=false;
        _sending=new CancellationTokenSource();
        try
        {
            var prefs=(await _core.Call<Snapshot<Preferences>>("settings.read",ct:_sending.Token)).Data;
            if(string.IsNullOrWhiteSpace(prefs.Model)||CredentialVault.Get()==null)
                throw new InvalidOperationException("请先在哲里设置中配置密钥并选择模型。");
            Persist("user",question);AddBubble(question,true);
            var reply=AddBubble("",false);_status.Text="正在回复…";
            var state=_store.Read().Data.Conversations.Single(c=>c.Id==_conversationId);
            var messages=state.Messages.Where(m=>!m.LocalOnly).TakeLast(20).Select(m=>new ChatMessage(m.Role,m.Text)).ToList();
            messages.Insert(0,new("system","你是哲喵快捷助手，使用简体中文。当前面板只有文字对话能力，不可声称修改过文件、课表或日程。查询个人数据请引导用户打开完整窗口。"));
            using var client=new DeepSeekClient();
            var answer=await client.Stream(prefs.Model,messages,delta=>Dispatcher.BeginInvoke(new Action(()=>{reply.Text+=delta;_scroll.ScrollToEnd();})),_sending.Token);
            Persist("assistant",answer);_status.Text="回答完成 · 已保存";Refresh();
        }
        catch(OperationCanceledException){_status.Text="回答已取消";Refresh();}
        catch(DeepSeekException e){_status.Text=e.Message;Refresh();}
        catch(Exception e)when(e is InvalidOperationException or IOException or StoreException or JsonException or UnauthorizedAccessException)
        {_status.Text=e.Message;Refresh();}
        finally{_sending?.Dispose();_sending=null;Refresh();_input.IsEnabled=true;_send.IsEnabled=true;_input.Focus();}
    }
    public void StopAndHide(){_sending?.Cancel();Hide();}
}
