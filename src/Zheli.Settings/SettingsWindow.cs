using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Zheli.Bridge;
using Zheli.Contracts;
using Zheli.DesignSystem;
using Zheli.Domain;
using Zheli.Platform;
using Zheli.Storage;

namespace Zheli.Settings;

public sealed class SettingsWindow:ShellWindow
{
    private Snapshot<Preferences> _settings=new(0,new());
    private Receipt? _last;
    private readonly BridgeClient _timetable=new("timetable","Zheli.Timetable.Host");
    private readonly SemaphoreSlim _saveGate=new(1,1);
    private readonly TextBlock _header=Ui.Text("统一设置",24,true);
    public SettingsWindow():base("Settings","哲里设置")
    {
        Toolbar.Children.Add(_header);
        Toolbar.Children.Add(Ui.Button("撤销设置",()=>Guard(Undo)));
        Nav("外观与个性化",()=>Guard(ShowAppearance));
        Nav("哲里课表",()=>Guard(ShowTimetable));
        Nav("哲喵与DeepSeek",()=>Guard(ShowMiao));
        Nav("应用与接口",()=>Guard(ShowPermissions));
        Nav("文件知识库",()=>Guard(ShowKnowledge));
        Nav("数据与备份",()=>ShowData());
        Nav("更新与诊断",()=>ShowStatus());
        Guard(ShowAppearance);
    }
    private void Page(string title,params UIElement[] controls)
    {
        _header.Text=title;
        var content=Ui.Stack(controls);content.Padding=new Thickness(12,4,12,24);content.MaxWidth=1000;
        Workspace.Content=Ui.Scroll(content);
    }
    private async Task Load()=>_settings=await Core.Call<Snapshot<Preferences>>("settings.read");
    private async Task Save(Func<Preferences,Preferences> change)
    {
        await _saveGate.WaitAsync();
        try
        {
            await Load();var next=change(_settings.Data);
            _last=await Core.Call<Receipt>("settings.save",new SavePreferences(_settings.Revision,next));
            _settings=new(_last.Revision,next);Appearance=next;ApplyTheme();Status.Text="已保存全局设置 · 各应用将在2秒内同步 · 可撤销";
        }
        finally{_saveGate.Release();}
    }
    private async Task Undo()
    {
        if(_last==null){Status.Text="本窗口尚无可撤销的设置修改。";return;}
        await Core.Call<Receipt>("settings.undo",new UndoRequest(_last.Revision,_last.OperationId));_last=null;await ShowAppearance();
    }
    private async Task ShowAppearance()
    {
        await Load();var p=_settings.Data;
        var modes=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedItem=p.Theme,MinWidth=220};
        modes.SelectionChanged+=(_,_)=>Guard(()=>Save(x=>x with{Theme=(string)modes.SelectedItem}));
        var clarity=new Slider{Minimum=0,Maximum=100,Value=p.GlassClarity*100,StepFrequency=1};
        var preview=Glass(Ui.Stack(Ui.Text("玻璃材质预览",20,true),Ui.Text("单层等宽描边 · 正文保持清晰"),Ui.Row(Ui.Button("主要操作",()=>{}),Ui.Button("次要操作",()=>{}))));
        var label=Ui.Text($"玻璃通透度 {clarity.Value:0}%");
        var debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};
        debounce.Tick+=(_,_)=>{debounce.Stop();Guard(()=>Save(x=>x with{GlassClarity=clarity.Value/100}));};
        clarity.ValueChanged+=(_,_)=>{label.Text=$"玻璃通透度 {clarity.Value:0}%";preview.Apply(p with{GlassClarity=clarity.Value/100},Root.ActualTheme==ElementTheme.Dark,false);debounce.Stop();debounce.Start();};
        var transparency=new ToggleSwitch{Header="降低透明度",IsOn=p.ReduceTransparency};
        transparency.Toggled+=(_,_)=>Guard(()=>Save(x=>x with{ReduceTransparency=transparency.IsOn}));
        var motion=new ToggleSwitch{Header="减少动态效果",IsOn=p.ReduceMotion};
        motion.Toggled+=(_,_)=>Guard(()=>Save(x=>x with{ReduceMotion=motion.IsOn}));
        var accent=new ComboBox{Header="全局强调色",ItemsSource=new[]{"#7195B7","#9082B1","#718F80","#B6906F"},SelectedItem=p.Accent,MinWidth=220};
        accent.SelectionChanged+=(_,_)=>{if(accent.SelectedItem is string color)Guard(()=>Save(x=>x with{Accent=color}));};
        var scale=new ComboBox{Header="界面字号",ItemsSource=new[]{90,100,110,125,150},SelectedItem=(int)Math.Round(p.TextScale*100),MinWidth=220};
        scale.SelectionChanged+=(_,_)=>{if(scale.SelectedItem is int percent)Guard(()=>Save(x=>x with{TextScale=percent/100d}));};
        Page("外观与个性化",Ui.Text("作用于所有哲里软件；设置窗口关闭后仍然生效。"),
            Ui.Card(Ui.Field("主题：跟随系统 / 浅色 / 深色",modes)),
            Ui.Card(Ui.Stack(label,clarity,transparency)),preview,Ui.Card(Ui.Stack(accent,scale,motion)),
            Ui.Text("当前使用Windows Acrylic基础材质。精确10px模糊及145%饱和度的定制材质尚待Windows实测，不将系统默认效果宣称为最终液态玻璃。",12));
    }
    private async Task ShowTimetable()
    {
        var snapshot=await _timetable.Call<Snapshot<TimetableState>>("timetable.read");
        var weekends=new ToggleSwitch{Header="显示周末",IsOn=snapshot.Data.ShowWeekends};
        var teacher=new ToggleSwitch{Header="显示教师",IsOn=snapshot.Data.ShowTeacher};
        async Task Apply()
        {
            var latest=await _timetable.Call<Snapshot<TimetableState>>("timetable.read");
            await _timetable.Call<Receipt>("timetable.preferences",new{revision=latest.Revision,showWeekends=weekends.IsOn,showTeacher=teacher.IsOn});
            Status.Text="课表显示设置已保存；返回课表时刷新。";
        }
        weekends.Toggled+=(_,_)=>Guard(Apply);teacher.Toggled+=(_,_)=>Guard(Apply);
        Page("哲里课表",Ui.Card(Ui.Stack(weekends,teacher)),Ui.Button("打开课表管理学期与作息",()=>Guard(()=>{AppPaths.Launch("Zheli.Timetable");return Task.CompletedTask;})));
    }
    private async Task ShowPermissions()
    {
        await Load();
        var local=new ToggleSwitch{Header="允许哲喵本地查询课表",IsOn=_settings.Data.AllowMiaoReadTimetable};
        var cloud=new ToggleSwitch{Header="允许在明确确认后将课程摘要发送给DeepSeek",IsOn=_settings.Data.AllowCloudTimetable};
        var write=new ToggleSwitch{Header="允许哲喵提出并确认单次调课、停课及撤销",IsOn=_settings.Data.AllowMiaoWriteTimetable};
        local.Toggled+=(_,_)=>Guard(()=>Save(p=>p with{AllowMiaoReadTimetable=local.IsOn}));
        cloud.Toggled+=(_,_)=>Guard(()=>Save(p=>p with{AllowCloudTimetable=cloud.IsOn}));
        write.Toggled+=(_,_)=>Guard(()=>Save(p=>p with{AllowMiaoWriteTimetable=write.IsOn}));
        Page("应用与接口",Ui.Card(Ui.Stack(local,cloud,write)),Ui.Text("本地查询、云端发送和单次修改分别授权。修改会先显示对比并由你确认；删除课程与长期批量修改尚未向哲喵开放。"));
    }
    private async Task ShowMiao()
    {
        await Load();var key=new PasswordBox{Header="DeepSeek API密钥",MinWidth=300};
        var pet=new ToggleSwitch{Header="显示桌面哲喵",IsOn=_settings.Data.ShowPet};
        pet.Toggled+=(_,_)=>Guard(async()=>{await Save(p=>p with{ShowPet=pet.IsOn});if(pet.IsOn)AppPaths.Launch("Zheli.PetHost");});
        var model=new ComboBox{Header="可用模型",MinWidth=280};
        var info=Ui.Text(CredentialVault.Get()==null?"尚未配置密钥":"密钥已保存在Windows凭据管理器中");
        var controls=Ui.Stack(key,Ui.Row(Ui.Button("保存密钥",()=>Guard(()=>{CredentialVault.Set(key.Password);key.Password="";info.Text="密钥已保存";return Task.CompletedTask;})),
            Ui.Button("删除密钥",()=>Guard(async()=>{if(await Confirm("删除API密钥","本地课表与会话不会被删除。")){CredentialVault.Remove();info.Text="密钥已删除";}}))),info,
            Ui.Button("连接并获取模型",()=>Guard(async()=>{using var client=new DeepSeekClient();model.ItemsSource=await client.Models();model.SelectedItem=_settings.Data.Model;Status.Text="连接成功，模型列表已更新。";})),model);
        model.SelectionChanged+=(_,_)=>{if(model.SelectedItem is string id)Guard(()=>Save(p=>p with{Model=id}));};
        Page("哲喵与DeepSeek",Ui.Card(pet),Ui.Card(controls),Ui.Button("打开哲喵",()=>Guard(()=>{AppPaths.Launch("Zheli.Miao");return Task.CompletedTask;})),Ui.Text("密钥不写入配置、操作记录或备份；当前不支持语音。"));
    }
    private async Task ShowKnowledge()
    {
        await Load();var list=Ui.Stack(Ui.Text("授权文件夹",24,true),
            Ui.Text("哲喵在运行时每分钟检查变动，检索前重新核对内容。支持 TXT、Markdown、PDF、DOCX、XLSX、PPTX；扫描页暂不识别。原文件始终只读。"),
            Ui.Button("添加文件夹",()=>Guard(AddKnowledgeFolder)));
        foreach(var folder in _settings.Data.KnowledgeFolders)
        {
            var enabled=new ToggleSwitch{Header="允许本地读取与索引",IsOn=folder.Enabled};
            var privateFolder=new ToggleSwitch{Header="私密目录：禁止发送内容",IsOn=folder.Private};
            var cloud=new ToggleSwitch{Header="允许在逐次预览确认后发送选中片段",IsOn=folder.AllowCloud,IsEnabled=!folder.Private};
            async Task Update(Func<KnowledgeFolder,KnowledgeFolder> change)
            {await Save(p=>p with{KnowledgeFolders=p.KnowledgeFolders.Select(f=>f.Id==folder.Id?change(f):f).ToList()});}
            enabled.Toggled+=(_,_)=>Guard(()=>Update(f=>f with{Enabled=enabled.IsOn}));
            privateFolder.Toggled+=(_,_)=>Guard(async()=>{await Update(f=>f with{Private=privateFolder.IsOn,AllowCloud=privateFolder.IsOn?false:f.AllowCloud});await ShowKnowledge();});
            cloud.Toggled+=(_,_)=>Guard(()=>Update(f=>f with{AllowCloud=cloud.IsOn}));
            list.Children.Add(Ui.Card(Ui.Stack(Ui.Text(folder.Path,16,true),enabled,privateFolder,cloud,
                Ui.Button("移除授权",()=>Guard(async()=>
                {
                    if(!await Confirm("移除文件夹授权",$"{folder.Path}\n原文件不会删除。哲喵下次刷新会清理对应索引。若这是私密子目录，移除后其父目录的发送规则将重新适用。"))return;
                    await Save(p=>p with{KnowledgeFolders=p.KnowledgeFolders.Where(f=>f.Id!=folder.Id).ToList()});await ShowKnowledge();
                })))));
        }
        Page("文件知识库",list,Ui.Text("关闭目录读取会同时阻止其他授权读取该目录；移除该条目后父目录规则重新适用。私密子目录优先于父目录的云端授权。文件整理尚未开放。索引和既有本地会话不加密，清理索引不等于安全擦除磁盘。",12));
    }
    private async Task AddKnowledgeFolder()
    {
        var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder=await picker.PickSingleFolderAsync();if(folder==null)return;
        KnowledgeIndex.EnsureSafe(folder.Path,folder.Path);
        if(!await Confirm("授权本地读取",$"{folder.Path}\n允许哲喵在此目录及子目录中读取支持的文件并保存本地索引；默认禁止发送云端。"))return;
        await Save(p=>
        {
            if(p.KnowledgeFolders.Any(f=>Path.GetFullPath(f.Path).Equals(Path.GetFullPath(folder.Path),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("这个文件夹已经添加。");
            return p with{KnowledgeFolders=p.KnowledgeFolders.Append(new KnowledgeFolder(Guid.NewGuid().ToString(),folder.Path)).ToList()};
        });
        await ShowKnowledge();
    }
    private void ShowData()
    {
        Page("数据与备份",Ui.Text("每个数据所有者分别生成SQLite一致性快照。此版本尚未提供跨应用统一恢复。"),
            Ui.Card(Ui.Stack(Ui.Button("备份全局设置",()=>Guard(async()=>{var r=await Core.Call<System.Text.Json.JsonElement>("settings.backup");Status.Text="已备份："+r.GetProperty("path").GetString();})),
            Ui.Button("备份课表",()=>Guard(async()=>{var r=await _timetable.Call<System.Text.Json.JsonElement>("timetable.backup");Status.Text="已备份："+r.GetProperty("path").GetString();})))),
            Ui.Button("导出加密课表备份",()=>Guard(ExportTimetable)),Ui.Button("恢复加密课表备份",()=>Guard(RestoreTimetable)),
            Ui.Text("本机SQLite快照未加密；便携课表备份使用密码加密。恢复会替换当前课表，并先建立恢复前快照。"));
    }
    private async Task ExportTimetable()
    {
        var password=new PasswordBox{Header="备份密码（至少10个字符）"};var repeat=new PasswordBox{Header="再次输入"};
        if(!await Form("加密备份",Ui.Stack(Ui.Text("密码不保存，遗失后无法恢复。备份不包含DeepSeek密钥。"),password,repeat)))return;
        if(password.Password!=repeat.Password)throw new InvalidOperationException("两次密码不一致。");
        var picker=new Windows.Storage.Pickers.FileSavePicker{SuggestedFileName="Zheli-Timetable-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};
        picker.FileTypeChoices.Add("哲里加密备份",new List<string>{".zhelibackup"});
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSaveFileAsync();if(file==null)return;
        // The save picker creates a zero-byte placeholder. Do not overwrite a pre-existing backup.
        var properties=await file.GetBasicPropertiesAsync();if(properties.Size!=0)throw new IOException("文件已存在，请选择新的名称。");
        await file.DeleteAsync();
        await _timetable.Call<System.Text.Json.JsonElement>("timetable.exportBackup",new ExportBackup(file.Path,password.Password));
        password.Password="";repeat.Password="";Status.Text="加密备份已导出。";
    }
    private async Task RestoreTimetable()
    {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".zhelibackup");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();if(file==null)return;
        var header=PortableBackup.Header(file.Path);var password=new PasswordBox{Header="备份密码"};
        if(header.App!="Timetable")throw new InvalidOperationException("请选择课表备份。");
        if(!await Form("恢复前确认",Ui.Stack(Ui.Text($"备份时间：{header.Created.ToLocalTime():g}\n将替换当前课表。恢复前会自动创建保护快照；API密钥不受影响。"),password),"确认恢复"))return;
        var current=await _timetable.Call<Snapshot<TimetableState>>("timetable.read");
        await _timetable.Call<Receipt>("timetable.restoreBackup",new RestoreBackup(file.Path,password.Password,current.Revision));password.Password="";
        Status.Text="课表已恢复；请在课表窗口刷新。";
    }
    private void ShowStatus()=>Page("更新与诊断",Ui.Card(Ui.Stack(Ui.Text("开发版本 0.2.3",20,true),Ui.Text("三个独立应用 · 共享设计系统 · 独立数据所有权"),
        Ui.Button("检查核心连接",()=>Guard(async()=>{await Load();Status.Text="核心设置服务连接正常。";})))),
        Ui.Text("自动更新、签名安装包、通知调度、统一恢复仍未完成。不存在在线更新服务器，不会伪造“已是最新版”。"));
}
