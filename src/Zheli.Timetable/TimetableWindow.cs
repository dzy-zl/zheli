using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Zheli.Bridge;
using Zheli.Contracts;
using Zheli.DesignSystem;
using Zheli.Domain;
using Zheli.Storage;

namespace Zheli.Timetable;

public sealed class TimetableWindow:ShellWindow
{
    private readonly BridgeClient _api=new("timetable","Zheli.Timetable.Host");
    private Snapshot<TimetableState> _snapshot=new(0,new());
    private TimetableState Data=>_snapshot.Data;
    private Semester? Semester=>Data.Semesters.SingleOrDefault(x=>x.Id==Data.CurrentSemesterId);
    private int _week=1;
    private bool _loaded,_editing;
    private string _page="week";
    private Receipt? _last;
    private Occurrence? _selected;
    private readonly TextBlock _weekLabel=Ui.Text("尚未创建学期",16,true);
    private readonly TextBox _search=Ui.Input("","搜索课程、教师、教室、备注");
    private readonly Grid _layout=new(){ColumnSpacing=12};
    private readonly ContentControl _main=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly ContentControl _detail=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly ColumnDefinition _detailColumn=new(){Width=new GridLength(0)};
    private readonly DispatcherTimer _clock=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly DraftStore<CourseDraft> _drafts=new(Path.Combine(AppPaths.DataRoot,"Timetable","course-draft.json"));
    private readonly DispatcherTimer _draftTimer=new(){Interval=TimeSpan.FromSeconds(3)};
    private Func<CourseDraft>? _captureDraft;
    private readonly StackPanel _searchResults=new(){Spacing=8};
    private Action? _redrawWeek;
    private FrameworkElement? _courseSurface;

    public TimetableWindow():base("Timetable","哲里课表")
    {
        _layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});_layout.ColumnDefinitions.Add(_detailColumn);
        _layout.Children.Add(_main);Grid.SetColumn(_detail,1);_layout.Children.Add(_detail);Workspace.Content=_layout;
        Nav("周课表",()=>Guard(async()=>{if(await CloseDetail()){_page="week";Render();}}));
        Nav("课程",()=>Guard(async()=>{if(await CloseDetail()){_page="courses";Render();}}));
        Nav("临时变动",()=>Guard(async()=>{if(await CloseDetail()){_page="changes";Render();}}));
        Nav("学期与作息",()=>Guard(async()=>{if(await CloseDetail()){_page="semester";Render();}}));
        Nav("回收站",()=>Guard(async()=>{if(await CloseDetail()){_page="trash";Render();}}));
        Nav("恢复录课草稿",()=>Guard(RestoreDraft));
        AddGlobalNavigation();
        Toolbar.Children.Add(Ui.Button("‹",()=>Guard(()=>ChangeWeek(-1))));Toolbar.Children.Add(_weekLabel);
        Toolbar.Children.Add(Ui.Button("›",()=>Guard(()=>ChangeWeek(1))));
        Toolbar.Children.Add(Ui.Button("本周",()=>Guard(()=>GoToday())));
        Toolbar.Children.Add(Ui.Button("＋ 添加课程",()=>Guard(()=>EditCourse(null,1,1))));
        Toolbar.Children.Add(Ui.Button("搜索",()=>Guard(OpenSearch)));
        Toolbar.Children.Add(Ui.Button("哲喵",()=>Guard(OpenMiao)));
        Toolbar.Children.Add(Ui.Button("撤销",()=>Guard(Undo)));
        Toolbar.Children.Add(Ui.Button("刷新",()=>Guard(Refresh)));
        _search.Text="";_search.PlaceholderText="搜索课程、教师、教室、备注";
        _search.TextChanged+=(_,_)=>{if(_page=="search")UpdateSearchResults();};
        _layout.SizeChanged+=(_,_)=>{if(_detail.Content!=null)ResizeDetail();};
        _clock.Tick+=(_,_)=>{if(_loaded&&_page=="week"&&!_editing)_redrawWeek?.Invoke();};_clock.Start();
        _draftTimer.Tick+=(_,_)=>{if(_editing&&_captureDraft!=null){try{_drafts.Save(_captureDraft());}catch(Exception e){Status.Text="草稿保存失败："+e.Message;}}};_draftTimer.Start();
        Closed+=(_,_)=>{_clock.Stop();_draftTimer.Stop();if(_editing&&_captureDraft!=null){try{_drafts.Save(_captureDraft());}catch{}}};
        Activated+=(_,_)=>{if(_loaded&&!_editing)Guard(Refresh);};
        Guard(Refresh);
    }
    private async Task Refresh()
    {
        _snapshot=await _api.Call<Snapshot<TimetableState>>("timetable.read");
        if(!_loaded&&Semester!=null)_week=Math.Clamp(CalendarEngine.WeekOf(Semester,DateOnly.FromDateTime(DateTime.Now)),1,Semester.Weeks);
        _loaded=true;
        if(Semester!=null)_week=Math.Clamp(_week,1,Semester.Weeks);
        Render();
    }
    private async Task<bool> Save(TimetableState state,string message)
    {
        CalendarEngine.Validate(state);
        var conflicts=state.Semesters.SelectMany(s=>CalendarEngine.Conflicts(CalendarEngine.Expand(state,s.Id))).ToList();
        if(conflicts.Count>0 && !await Confirm("保存存在冲突的安排？",string.Join("\n",conflicts.Take(8).Select(c=>$"{c.First.Date:MM-dd} {c.First.Name} / {c.Second.Name}"))+"\n确认将保留这些冲突，不会移动其他课程。"))return false;
        _last=await _api.Call<Receipt>("timetable.save",new SaveTimetable(_snapshot.Revision,state,conflicts.Count>0));
        _snapshot=new(_last.Revision,state);Status.Text=message+" · 已保存 · 可撤销";return true;
    }
    private async Task Undo()
    {
        if(_last==null){Status.Text="本窗口尚无可撤销操作。";return;}
        if(!await CloseDetail())return;
        await _api.Call<Receipt>("timetable.undo",new UndoRequest(_snapshot.Revision,_last.OperationId));_last=null;
        await Refresh();Status.Text="已撤销。";
    }
    private async Task ChangeWeek(int delta)
    {
        if(Semester==null||!await CloseDetail())return;
        _week=Math.Clamp(_week+delta,1,Semester.Weeks);_page="week";Render();
        if(AnimationsAllowed&&_courseSurface!=null)
        {
            var visual=Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_courseSurface);visual.StopAnimation("Offset");
            var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.InsertKeyFrame(0,new Vector3(delta>0?18:-18,0,0));animation.InsertKeyFrame(1,Vector3.Zero);animation.Duration=TimeSpan.FromMilliseconds(320);visual.StartAnimation("Offset",animation);
        }
    }
    private async Task GoToday(){if(Semester!=null&&await CloseDetail()){_week=Math.Clamp(CalendarEngine.WeekOf(Semester,DateOnly.FromDateTime(DateTime.Now)),1,Semester.Weeks);_page="week";Render();}}
    private List<Occurrence> All()=>Semester==null?[]:CalendarEngine.Expand(Data,Semester.Id).ToList();
    private void Render()
    {
        _redrawWeek=null;_courseSurface=null;
        _weekLabel.Text=Semester==null?"创建你的第一份课表":$"第 {_week} 周  ·  {CalendarEngine.DateOf(Semester,_week,1):M/d}–{CalendarEngine.DateOf(Semester,_week,7):M/d}";
        if(Semester==null){_main.Content=Ui.Card(Ui.Stack(Ui.Text("从一个学期开始",24,true),Ui.Text("没有演示课程写入你的数据库。先设置第1周周一和作息，再手动录入课程。"),Ui.Button("创建学期",()=>Guard(CreateSemester))));return;}
        switch(_page){case "courses":RenderCourses();break;case "changes":RenderChanges();break;case "semester":RenderSemesters();break;case "search":RenderSearch();break;case "trash":RenderTrash();break;default:RenderWeek();break;}
    }
    protected override void AppearanceChanged(){if(_loaded&&!_editing)Render();}
    private void RenderWeek()
    {
        if(Semester==null)return;
        var days=Data.ShowWeekends?7:5;var monday=CalendarEngine.DateOf(Semester,_week,1);var today=DateOnly.FromDateTime(DateTime.Now);
        var schedule=CalendarEngine.ScheduleFor(Data,Semester,monday);
        var occurrences=All();
        var root=new Grid{ColumnSpacing=8,RowSpacing=6};root.ColumnDefinitions.Add(new(){Width=new GridLength(90)});root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new(){Height=new GridLength(56)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        root.Children.Add(Ui.Text("节次 / 时间",12));
        var header=new Grid();var cells=new Grid();var labels=new StackPanel();
        var headerScroll=new ScrollViewer{Content=header,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var labelScroll=new ScrollViewer{Content=labels,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var scroll=new ScrollViewer{Content=cells,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetColumn(headerScroll,1);root.Children.Add(headerScroll);Grid.SetRow(labelScroll,1);root.Children.Add(labelScroll);Grid.SetColumn(scroll,1);Grid.SetRow(scroll,1);root.Children.Add(scroll);
        scroll.ViewChanged+=(_,_)=>{headerScroll.ChangeView(scroll.HorizontalOffset,null,null,true);labelScroll.ChangeView(null,scroll.VerticalOffset,null,true);};
        for(var d=0;d<days;d++)
        {
            var date=monday.AddDays(d);header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});cells.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var h=Ui.Card(Ui.Stack(Ui.Text(new[]{"周一","周二","周三","周四","周五","周六","周日"}[d],14,true),Ui.Text($"{date:M/d}"+(date==today?" · 今天":""),12)));h.Padding=new Thickness(8,2,8,2);
            if(date==today)h.Background=Ui.Brush(Appearance.Accent,50);Grid.SetColumn(h,d);header.Children.Add(h);
        }
        var heights=new List<double>();
        void Draw()
        {
            cells.Children.Clear();cells.RowDefinitions.Clear();labels.Children.Clear();heights.Clear();
            var width=Math.Max(days*142,scroll.ViewportWidth);cells.Width=width;header.Width=width;
            var gapTotal=schedule.Periods.Zip(schedule.Periods.Skip(1)).Count(pair=>pair.Second.Start-pair.First.End>TimeSpan.FromMinutes(45))*12;
            var rowHeight=Math.Max(54*Appearance.TextScale,(scroll.ViewportHeight-gapTotal-4)/schedule.Periods.Count);
            for(var p=0;p<schedule.Periods.Count;p++)
            {
                var period=schedule.Periods[p];var gap=p<schedule.Periods.Count-1&&schedule.Periods[p+1].Start-period.End>TimeSpan.FromMinutes(45)?12:0;
                heights.Add(rowHeight+gap);cells.RowDefinitions.Add(new(){Height=new GridLength(rowHeight+gap)});
                var label=Ui.Stack(Ui.Text($"第{period.Number}节",14,true),Ui.Text($"{period.Start:HH:mm}\n{period.End:HH:mm}",12));label.Spacing=2;label.Height=rowHeight+gap;labels.Children.Add(label);
                for(var d=0;d<days;d++)
                {
                    var day=d+1;var number=p+1;var date=monday.AddDays(d);
                    var button=Ui.Button("",()=>Guard(()=>EditCourse(null,day,number)));
                    button.HorizontalAlignment=HorizontalAlignment.Stretch;button.VerticalAlignment=VerticalAlignment.Stretch;button.Margin=new Thickness(2,2,2,2+gap);button.Padding=new Thickness(0);button.BorderThickness=new Thickness(0);
                    button.Background=date==today?Ui.Brush(Appearance.Accent,16):Ui.Brush("8995A3",6);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,$"{date:M月d日}第{number}节，添加课程");
                    ToolTipService.SetToolTip(button,date<Semester.TeachingStart?"未开学；仍可添加临时课程":"添加课程");Grid.SetColumn(button,d);Grid.SetRow(button,p);cells.Children.Add(button);
                }
            }
            for(var d=0;d<days;d++)
            {
                var date=monday.AddDays(d);
                var dayItems=occurrences.Where(o=>o.Date==date||o.Moved&&o.OriginalDate==date).SelectMany(o=>
                    o.Moved&&o.OriginalDate==date&&o.Date==date?new[]{(o,false),(o,true)}:new[]{(o,o.Date!=date)}).ToList();
                // Greedy interval lanes include original-location markers, so no card obscures another.
                var lanes=new List<int>();var positioned=new List<(Occurrence o,bool ghost,int lane,int start,int end)>();
                foreach(var (o,ghost) in dayItems.OrderBy(x=>x.Item2?x.o.OriginalStart:x.o.StartPeriod))
                {
                    var start=ghost?o.OriginalStart:o.StartPeriod;var end=ghost?o.OriginalEnd:o.EndPeriod;
                    var lane=lanes.FindIndex(last=>last<start);if(lane<0){lane=lanes.Count;lanes.Add(end);}else lanes[lane]=end;
                    positioned.Add((o,ghost,lane,start,end));
                }
                foreach(var item in positioned)
                {
                    var o=item.o;var content=Ui.Stack(Ui.Text(o.Name,14,true),Ui.Text(item.ghost?$"已调至 {o.Date:M/d} 第{o.StartPeriod}—{o.EndPeriod}节":o.Room,12));content.Spacing=3;
                    if(!item.ghost&&Data.ShowTeacher&&!string.IsNullOrWhiteSpace(o.Teacher))content.Children.Add(Ui.Text(o.Teacher,12));
                    var now=TimeOnly.FromDateTime(DateTime.Now);
                    var status=item.ghost?"原安排":o.Date==today&&!o.Cancelled&&now>=o.Start&&now<o.End?"上课中":
                        o.Date==today&&!o.Cancelled&&now<o.Start&&(o.Start-now).TotalMinutes<=30?"即将开始":o.Status;
                    if(status!="正常")content.Children.Add(Ui.Text(status,12,true));
                    var button=Ui.Button("",()=>Guard(()=>ShowDetails(o)));button.Content=content;
                    button.HorizontalAlignment=HorizontalAlignment.Left;button.VerticalAlignment=VerticalAlignment.Stretch;
                    var laneWidth=width/days/Math.Max(1,lanes.Count);button.Width=Math.Max(24,laneWidth-8);button.Margin=new Thickness(item.lane*laneWidth+4,4,4,4);
                    button.Background=Ui.Brush(o.Color,Root.ActualTheme==ElementTheme.Dark?(byte)75:(byte)65);
                    button.BorderThickness=new Thickness(1);button.BorderBrush=Ui.Brush(o.Color,item.ghost?(byte)110:(byte)45);button.Opacity=(item.ghost||o.Cancelled)?0.65:1;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,$"{o.Name} {o.Room} {status}");
                    Grid.SetColumn(button,d);Grid.SetRow(button,item.start-1);Grid.SetRowSpan(button,item.end-item.start+1);cells.Children.Add(button);
                }
            }
            var currentDay=today.DayNumber-monday.DayNumber;
            if(currentDay>=0&&currentDay<days)
            {
                var now=TimeOnly.FromDateTime(DateTime.Now);
                var index=schedule.Periods.FindIndex(p=>now>=p.Start&&now<=p.End);
                if(index>=0)
                {
                    var p=schedule.Periods[index];var ratio=(now-p.Start).TotalMinutes/(p.End-p.Start).TotalMinutes;
                    var line=new Border{Height=2,Background=Ui.Brush(Appearance.Accent),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,Math.Clamp(ratio*rowHeight,0,rowHeight-2),0,0),IsHitTestVisible=false};
                    Grid.SetColumn(line,currentDay);Grid.SetRow(line,index);cells.Children.Add(line);
                }
            }
        }
        scroll.SizeChanged+=(_,_)=>Draw();_main.Content=root;_redrawWeek=Draw;_courseSurface=cells;Draw();
        var todayClasses=occurrences.Where(o=>o.Date==today&&!o.Cancelled).ToList();
        Status.Text=$"{Semester.Name} · 今天 {todayClasses.Count} 次课，共 {todayClasses.Sum(o=>o.PeriodCount)} 节 · {CalendarEngine.Conflicts(occurrences.Where(o=>o.Date>=monday&&o.Date<=monday.AddDays(6))).Count} 处冲突";
    }
    private void RenderCourses()
    {
        var stack=Ui.Stack(Ui.Text("课程",24,true));
        foreach(var c in Data.Courses.Where(c=>c.SemesterId==Semester!.Id))
        {
            var arrangements=Data.Arrangements.Where(a=>a.CourseId==c.Id).ToList();
            stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text(c.Name,18,true),Ui.Text($"{c.Teacher} · {arrangements.Count} 条安排"),Ui.Row(
                Ui.Button("编辑课程信息",()=>Guard(()=>EditCourseInfo(c))),Ui.Button("添加安排",()=>Guard(()=>EditCourse(c,1,1))),
                Ui.Button("删除课程",()=>Guard(async()=>{if(await Confirm("删除完整课程？",$"{c.Name}包含{arrangements.Count}条安排及相关临时变动。将移入回收站，可以恢复。")&&await Save(Commands.DeleteCourse(Data,c.Id),"课程已移入回收站"))Render();}))))));
        }
        _main.Content=Ui.Scroll(stack);
    }
    private void RenderChanges()
    {
        var stack=Ui.Stack(Ui.Text("临时变动",24,true));
        foreach(var o in All().Where(o=>o.Changed||o.Temporary))stack.Children.Add(Ui.Button($"{o.Name} · {o.Date:M/d} · {o.Status}",()=>Guard(()=>ShowDetails(o))));
        _main.Content=Ui.Scroll(stack);
    }
    private void RenderTrash()
    {
        var stack=Ui.Stack(Ui.Text("回收站",24,true),Ui.Text("恢复时重新检查引用和课程冲突。此版本不会自动永久清除回收站。"));
        foreach(var bundle in Data.Trash.OrderByDescending(x=>x.DeletedAt))
            stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text(bundle.Course?.Name??"已删除安排",18,true),Ui.Text($"{bundle.DeletedAt.ToLocalTime():g} · {bundle.Arrangements.Count}条安排"),
                Ui.Button("恢复",()=>Guard(async()=>{if(await Save(Commands.RestoreDeleted(Data,bundle.Id),"已恢复"))Render();})))));
        _main.Content=Ui.Scroll(stack);
    }
    private void RenderSearch()
    {
        // Reuse the input without trying to attach it to two parents.
        if(_search.Parent is Panel old)old.Children.Remove(_search);
        if(_searchResults.Parent is Panel oldResults)oldResults.Children.Remove(_searchResults);
        var stack=Ui.Stack(Ui.Text("本地搜索",24,true),_search,_searchResults);
        _main.Content=Ui.Scroll(stack);UpdateSearchResults();
    }
    private void UpdateSearchResults()
    {
        _searchResults.Children.Clear();var query=_search.Text.Trim();
        foreach(var o in All().Where(o=>query.Length>0&&$"{o.Name} {o.Teacher} {o.Room} {o.Notes}".Contains(query,StringComparison.OrdinalIgnoreCase)).Take(100))
            _searchResults.Children.Add(Ui.Button($"{o.Name} · {o.Date:M/d} 第{o.StartPeriod}—{o.EndPeriod}节 · {o.Room} · {o.Status}",()=>Guard(async()=>{if(!await CloseDetail())return;_week=CalendarEngine.WeekOf(Semester!,o.Date);_page="week";Render();await ShowDetails(o);})));
        if(query.Length==0)_searchResults.Children.Add(Ui.Text("输入课程名称、教师、教室或备注。仅搜索当前学期，不调用AI。"));
        else if(_searchResults.Children.Count==0)_searchResults.Children.Add(Ui.Text("没有找到匹配课程。"));
    }
    private void RenderSemesters()
    {
        var stack=Ui.Stack(Ui.Text("学期与作息",24,true),Ui.Button("新建学期",()=>Guard(CreateSemester)));
        foreach(var s in Data.Semesters)stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text(s.Name,18,true),Ui.Text($"第1周周一 {s.WeekOneMonday:yyyy-MM-dd} · 开课 {s.TeachingStart:yyyy-MM-dd} · {s.Weeks} 周"),Ui.Button("查看此学期",()=>Guard(async()=>{if(await Save(Data with{CurrentSemesterId=s.Id},"已切换学期")){_week=1;_page="week";Render();}})))));
        foreach(var use in Semester!.Schedules.OrderBy(x=>x.FromWeek))
        {
            var s=Data.Schedules.Single(x=>x.Id==use.ScheduleId);
            stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"第{use.FromWeek}周起 · {s.Name}",18,true),Ui.Text(string.Join("\n",s.Periods.Select(p=>$"第{p.Number}节  {p.Start:HH:mm}—{p.End:HH:mm}"))))));
        }
        stack.Children.Add(Ui.Button("从指定周启用新作息",()=>Guard(AddSchedule)));_main.Content=Ui.Scroll(stack);
    }
    private void ShowPanel(UIElement content,bool editing)
    {
        _editing=editing;_detail.Content=Glass(Ui.Scroll(content));ResizeDetail();Fade(_detail);
    }
    private void ResizeDetail()
    {
        if(_detail.Content==null)return;
        _detailColumn.Width=new GridLength(_layout.ActualWidth<1050?Math.Min(480,Math.Max(320,_layout.ActualWidth)):440);
        if(_layout.ActualWidth<850){Grid.SetColumn(_detail,0);Grid.SetColumnSpan(_detail,2);_detail.HorizontalAlignment=HorizontalAlignment.Right;_detail.Width=Math.Min(480,_layout.ActualWidth);_detailColumn.Width=new GridLength(0);}
        else {Grid.SetColumn(_detail,1);Grid.SetColumnSpan(_detail,1);_detail.Width=double.NaN;_detail.HorizontalAlignment=HorizontalAlignment.Stretch;}
    }
    private async Task<bool> CloseDetail()
    {
        if(_editing)
        {
            if(_captureDraft!=null)_drafts.Save(_captureDraft());
            if(!await Confirm("关闭编辑面板？",_captureDraft!=null?"录课内容已保存为草稿，可通过左侧“恢复录课草稿”继续；正式课程尚未修改。":"尚未保存的修改将被放弃，正式课程不变。"))return false;
        }
        _captureDraft=null;
        _detail.Content=null;_detailColumn.Width=new GridLength(0);_editing=false;_selected=null;return true;
    }
    private async Task ShowDetails(Occurrence o)
    {
        if(!await CloseDetail())return;_selected=o;
        var panel=Ui.Stack(Ui.Row(Ui.Text(o.Name,24,true),Ui.Button("关闭",()=>Guard(async()=>{await CloseDetail();}))),Ui.Text(o.Status,16,true),
            Ui.Text($"{o.Date:yyyy年M月d日} · 第{o.StartPeriod}—{o.EndPeriod}节\n{o.Start:HH:mm}—{o.End:HH:mm}\n{o.Room}\n{o.Teacher}"),Ui.Text(o.Notes),
            Ui.Text($"原安排：{o.OriginalDate:M/d} 第{o.OriginalStart}—{o.OriginalEnd}节 · {o.OriginalRoom}",12),
            Ui.Button("调整本次课程",()=>Guard(()=>MoveOccurrence(o))),
            Ui.Button("修改本次教室",()=>Guard(()=>ChangeRoom(o))),
            Ui.Button("本次停课",()=>Guard(async()=>{if(await Confirm("本次停课",$"只影响{o.Date:M/d}的{o.Name}，其他周次不变。")&&await Save(Commands.Cancel(Data,o.Key),"已停课")){await CloseDetail();Render();}})),
            Ui.Button("恢复原安排",()=>Guard(async()=>{if(await Save(Commands.RestoreOccurrence(Data,o.Key),"已恢复原安排")){await CloseDetail();Render();}})),
            Ui.Button("从指定周修改重复安排",()=>Guard(()=>ReviseArrangement(o))),
            Ui.Button("删除这条安排",()=>Guard(async()=>{if(await Confirm("删除整条安排？","将移除这条安排在所有周次的课程及关联变动，可以从回收站恢复。")&&await Save(Commands.DeleteArrangement(Data,o.Key.ArrangementId),"安排已移入回收站")){await CloseDetail();Render();}})),
            Ui.Button("编辑课程信息",()=>Guard(()=>EditCourseInfo(Data.Courses.Single(c=>c.Id==o.CourseId)))));
        ShowPanel(panel,false);
    }
    private async Task EditCourse(Course? existing,int day,int period,CourseDraft? draft=null)
    {
        if(Semester==null){await CreateSemester();return;}if(!await CloseDetail())return;
        string[] palette=["#85A8C9","#A694C4","#8AAC9B","#C3A17D","#B892A4","#85AEB5"];
        var name=Ui.Input("课程名称",existing?.Name??"");var teacher=Ui.Input("默认教师",existing?.Teacher??"");var room=Ui.Input("本安排教室");var color=Ui.Input("课程颜色",existing?.Color??palette[Data.Courses.Count%palette.Length]);
        var notes=Ui.Input("课程备注",existing?.Notes??"");notes.AcceptsReturn=true;
        var weekday=Ui.Input("星期（1=周一，7=周日）",day.ToString());var from=Ui.Input("开始节次",period.ToString());var to=Ui.Input("结束节次",period.ToString());
        var weeks=Ui.Input("上课周次，例如 1-4,7,9-12",_week.ToString());
        var parity=new ComboBox{Header="单双周筛选",ItemsSource=new[]{"全部所选周","单周","双周"},SelectedIndex=0};
        var temporary=new CheckBox{Content="仅这一次（临时增加）",IsChecked=true};
        if(draft!=null)
        {
            name.Text=draft.Name;teacher.Text=draft.Teacher;color.Text=draft.Color;notes.Text=draft.Notes;room.Text=draft.Room;
            weekday.Text=draft.Day;from.Text=draft.From;to.Text=draft.To;weeks.Text=draft.Weeks;parity.SelectedIndex=draft.Parity;temporary.IsChecked=draft.Temporary;
        }
        _captureDraft=()=>new(Semester.Id,existing?.Id,name.Text,teacher.Text,color.Text,notes.Text,room.Text,weekday.Text,from.Text,to.Text,weeks.Text,parity.SelectedIndex,temporary.IsChecked==true);
        var error=Ui.Text("");
        async Task Commit(bool more)
        {
            try
            {
                var chosen=CalendarEngine.ParseWeeks(weeks.Text,Semester.Weeks).Where(w=>parity.SelectedIndex==0||w%2==(parity.SelectedIndex==1?1:0)).ToList();
                if(chosen.Count==0)throw new DomainException("筛选后没有上课周次。");
                var id=existing?.Id??Guid.NewGuid().ToString("N");
                var course=existing??new Course(id,Semester.Id,name.Text.Trim(),teacher.Text.Trim(),color.Text.Trim(),notes.Text);
                var revision=new ArrangementRevision(chosen.Min(),chosen.Max(),Number(weekday),Number(from),Number(to),chosen,room.Text.Trim());
                var a=new Arrangement(Guid.NewGuid().ToString("N"),id,temporary.IsChecked==true,[revision]);
                var next=Data with{Courses=existing==null?Data.Courses.Append(course).ToList():Data.Courses,Arrangements=Data.Arrangements.Append(a).ToList()};
                if(!await Save(next,"课程已添加"))return;
                _drafts.Clear();_editing=false;await CloseDetail();Render();
                if(more)await EditCourse(course,day,period);
            }
            catch(Exception ex){error.Text=ex.Message;}
        }
        ShowPanel(Ui.Stack(Ui.Text(existing==null?"添加课程":"添加上课安排",24,true),Ui.Text($"{Semester.Name} · 默认仅第{_week}周",12),
            name,teacher,color,weekday,Ui.Row(from,to),weeks,parity,temporary,room,notes,error,
            Ui.Button("保存课程",()=>Guard(()=>Commit(false))),Ui.Button("保存并继续添加安排",()=>Guard(()=>Commit(true))),Ui.Button("取消",()=>Guard(async()=>{await CloseDetail();}))),true);
        if(existing!=null){name.IsReadOnly=true;teacher.IsReadOnly=true;color.IsReadOnly=true;notes.IsReadOnly=true;}
        name.Focus(FocusState.Programmatic);
    }
    private async Task RestoreDraft()
    {
        var draft=_drafts.Load()??throw new DomainException("没有待恢复的录课草稿。");
        if(Semester?.Id!=draft.SemesterId)throw new DomainException("请先切换到草稿所属学期，再恢复草稿。");
        var course=draft.CourseId==null?null:Data.Courses.SingleOrDefault(c=>c.Id==draft.CourseId)??throw new DomainException("草稿所属课程已删除，请先恢复课程。");
        await EditCourse(course,1,1,draft);
    }
    private static int Number(TextBox input)=>int.TryParse(input.Text,out var n)?n:throw new DomainException($"{input.Header}需要填写整数。");
    private static DateOnly Date(TextBox input)=>DateOnly.TryParseExact(input.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:throw new DomainException("日期格式为 yyyy-MM-dd。");
    private async Task EditCourseInfo(Course course)
    {
        if(!await CloseDetail())return;
        var name=Ui.Input("名称",course.Name);var teacher=Ui.Input("默认教师",course.Teacher);var color=Ui.Input("颜色",course.Color);var notes=Ui.Input("备注",course.Notes);
        ShowPanel(Ui.Stack(Ui.Text("编辑课程信息",24,true),Ui.Text("会影响所有安排共享的名称、默认教师、颜色和课程备注。"),name,teacher,color,notes,
            Ui.Button("保存",()=>Guard(async()=>{var next=Data with{Courses=Data.Courses.Select(c=>c.Id==course.Id?c with{Name=name.Text.Trim(),Teacher=teacher.Text.Trim(),Color=color.Text.Trim(),Notes=notes.Text}:c).ToList()};if(await Save(next,"课程信息已更新")){_editing=false;await CloseDetail();Render();}})),Ui.Button("取消",()=>Guard(async()=>{await CloseDetail();}))),true);
    }
    private async Task MoveOccurrence(Occurrence o)
    {
        if(!await CloseDetail())return;var date=Ui.Input("目标日期（同学期）",o.Date.ToString("yyyy-MM-dd"));var start=Ui.Input("开始节次",o.StartPeriod.ToString());
        ShowPanel(Ui.Stack(Ui.Text("调整本次课程",24,true),Ui.Text($"保留{o.PeriodCount}节长度，仅修改这一次。"),date,start,
            Ui.Button("检查并保存",()=>Guard(async()=>{var next=Commands.Move(Data,o.Key,Date(date),Number(start));if(await Save(next,"本次课程已调整")){_editing=false;await CloseDetail();Render();}})),Ui.Button("取消",()=>Guard(async()=>{await CloseDetail();}))),true);
    }
    private async Task ChangeRoom(Occurrence o)
    {
        var room=Ui.Input("新教室",o.Room);if(!await Form("仅修改本次教室",room))return;
        var old=Data.Overrides.SingleOrDefault(x=>x.Key==o.Key)??new OccurrenceOverride(Guid.NewGuid().ToString("N"),o.Key,false,null,null,null,null,"");
        if(await Save(Commands.PutOverride(Data,old with{Room=room.Text,Reason="本次换教室"}),"教室已修改")){await CloseDetail();Render();}
    }
    private async Task ReviseArrangement(Occurrence o)
    {
        var a=Data.Arrangements.Single(x=>x.Id==o.Key.ArrangementId);if(a.Temporary)throw new DomainException("临时课程请使用调整本次课程。");
        if(!await CloseDetail())return;
        var current=Math.Clamp(CalendarEngine.WeekOf(Semester!,DateOnly.FromDateTime(DateTime.Now)),1,Semester!.Weeks);
        var effective=Ui.Input("从第几周起生效",current.ToString());var day=Ui.Input("新星期（1—7）",((int)(o.Date.DayOfWeek+6)%7+1).ToString());
        var start=Ui.Input("开始节次",o.StartPeriod.ToString());var end=Ui.Input("结束节次",o.EndPeriod.ToString());var room=Ui.Input("教室",o.Room);var weeks=Ui.Input("新安排的实际周次",$"{current}-{Semester.Weeks}");
        var changes=Data.Overrides.Where(x=>x.Key.ArrangementId==a.Id).ToList();
        ShowPanel(Ui.Stack(Ui.Text("修改重复安排",24,true),Ui.Text("此前周次保留历史；以下单次变动默认保留：\n"+string.Join("\n",changes.Select(x=>$"第{x.Key.SourceWeek}周 · {x.Reason}"))),effective,day,Ui.Row(start,end),room,weeks,
            Ui.Button("预览影响并保存",()=>Guard(async()=>{var next=Commands.Revise(Data,a.Id,new(Number(effective),Semester.Weeks,Number(day),Number(start),Number(end),CalendarEngine.ParseWeeks(weeks.Text,Semester.Weeks),room.Text));
                if(await Confirm("确认修改未来安排",$"从第{Number(effective)}周起生效，保留{changes.Count}项单次变动；此前历史不变。")&&await Save(next,"重复安排已更新")){_editing=false;await CloseDetail();Render();}})),Ui.Button("取消",()=>Guard(async()=>{await CloseDetail();}))),true);
    }
    private static string DefaultPeriods=>"08:00-08:45\n08:55-09:40\n10:00-10:45\n10:55-11:40\n13:30-14:15\n14:25-15:10\n15:30-16:15\n16:25-17:10\n18:30-19:15\n19:25-20:10";
    private static List<Period> ParsePeriods(string input)
    {
        var result=new List<Period>();
        foreach(var row in input.Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            var pair=row.Trim().Split('-');if(pair.Length!=2||!TimeOnly.TryParseExact(pair[0].Trim(),"HH:mm",out var from)||!TimeOnly.TryParseExact(pair[1].Trim(),"HH:mm",out var to))throw new DomainException("每行作息格式为 08:00-08:45。");
            result.Add(new(result.Count+1,from,to));
        }
        return result;
    }
    private async Task CreateSemester()
    {
        var today=DateOnly.FromDateTime(DateTime.Now);var monday=today.AddDays(-((int)today.DayOfWeek+6)%7);
        var name=Ui.Input("学期名称",$"{today.Year}—{today.Year+1}学年 第一学期");var first=Ui.Input("第1周周一",monday.ToString("yyyy-MM-dd"));var teaching=Ui.Input("正式开课日期",today.ToString("yyyy-MM-dd"));var weeks=Ui.Input("学期周数","18");
        var periods=Ui.Input("基础作息（每行一节，可编辑）",DefaultPeriods);periods.AcceptsReturn=true;periods.MinHeight=170;
        if(!await Form("创建学期",Ui.Stack(name,first,teaching,weeks,periods)))return;
        var schedule=new Schedule(Guid.NewGuid().ToString("N"),"初始作息",ParsePeriods(periods.Text));
        var semester=new Semester(Guid.NewGuid().ToString("N"),name.Text.Trim(),Date(first),Date(teaching),Number(weeks),[new(1,schedule.Id)]);
        if(await Save(Data with{CurrentSemesterId=semester.Id,Semesters=Data.Semesters.Append(semester).ToList(),Schedules=Data.Schedules.Append(schedule).ToList()},"学期已创建")){_week=1;_page="week";Render();}
    }
    private async Task AddSchedule()
    {
        var name=Ui.Input("作息名称","冬季作息");var week=Ui.Input("从第几周起生效",_week.ToString());var periods=Ui.Input("每行一个节次",DefaultPeriods);periods.AcceptsReturn=true;
        if(!await Form("新增作息版本",Ui.Stack(Ui.Text("不改写原作息；被引用的节次必须全部存在。"),name,week,periods)))return;
        var schedule=new Schedule(Guid.NewGuid().ToString("N"),name.Text,ParsePeriods(periods.Text));var from=Number(week);
        if(Semester!.Schedules.Any(x=>x.FromWeek==from))throw new DomainException("该周已有作息版本，请选择其他生效周；不覆盖历史作息。");
        var semester=Semester with{Schedules=Semester.Schedules.Append(new(from,schedule.Id)).ToList()};
        if(await Save(Data with{Schedules=Data.Schedules.Append(schedule).ToList(),Semesters=Data.Semesters.Select(x=>x.Id==semester.Id?semester:x).ToList()},"作息版本已添加"))Render();
    }
    private Task OpenMiao()
    {
        var context=Semester==null?null:new ChatContext(Semester.Id,_week,_selected?.Key);
        AppPaths.Launch("Zheli.Miao",context==null?"":"--context",context==null?"":Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(context,Protocol.Json)));
        return Task.CompletedTask;
    }
    private async Task OpenSearch()
    {
        if(!await CloseDetail())return;
        _page="search";Render();_search.Focus(FocusState.Programmatic);
    }
    protected override void OnKey(object sender,KeyRoutedEventArgs e)
    {
        var ctrl=Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt=Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if(e.Key==VirtualKey.Escape){e.Handled=true;Guard(async()=>{await CloseDetail();});}
        else if(ctrl&&e.Key==VirtualKey.F){e.Handled=true;Guard(OpenSearch);}
        else if(ctrl&&e.Key==VirtualKey.N){e.Handled=true;Guard(()=>EditCourse(null,1,1));}
        else if(alt&&!_editing&&e.Key is VirtualKey.Left or VirtualKey.Right){e.Handled=true;Guard(()=>ChangeWeek(e.Key==VirtualKey.Left?-1:1));}
    }
}
public sealed record CourseDraft(string SemesterId,string? CourseId,string Name,string Teacher,string Color,string Notes,
    string Room,string Day,string From,string To,string Weeks,int Parity,bool Temporary);
