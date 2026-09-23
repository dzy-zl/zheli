using System.Text.Json;
using Zheli.Bridge;
using Zheli.Contracts;
using Zheli.Domain;
using Zheli.Storage;

using var presence=AppPaths.HoldRuntimePresence();
var store = new DocumentStore<TimetableState>(Path.Combine(AppPaths.DataRoot,"Timetable","timetable.db"));
var core = new BridgeClient("core","Zheli.CoreHost");
var prepared=new PreparedChanges();
await BridgeServer.Run("timetable", Handle, CancellationToken.None);

async Task<Response> Handle(string caller, Request r)
{
    try
    {
        object result;
        switch(r.Method)
        {
            case "timetable.read" when caller is "Zheli.Timetable" or "Zheli.Settings": result = store.Read(); break;
            case "timetable.save" when caller == "Zheli.Timetable":
                var save = r.Args<SaveTimetable>();
                result = store.Write(r.Id,save.Revision,"修改课表",_ => save.State,
                    DocumentStore<TimetableState>.Hash(r.Arguments.GetRawText()), state =>
                    {
                        CalendarEngine.Validate(state);
                        if (!save.AllowConflicts)
                        {
                            var conflicts = state.Semesters.SelectMany(s => CalendarEngine.Conflicts(CalendarEngine.Expand(state,s.Id))).ToList();
                            if (conflicts.Count > 0)
                                throw new DomainException("发现课程冲突：" + string.Join("；",conflicts.Take(8).Select(c => $"{c.First.Date:MM-dd} {c.First.Name} / {c.Second.Name}")) + "。请检查并明确选择保留冲突。");
                        }
                    }); break;
            case "timetable.preferences" when caller == "Zheli.Settings":
                var pref = r.Args<TimetablePreferences>();
                result = store.Write(r.Id,pref.Revision,"课表显示设置",s => s with { ShowTeacher=pref.ShowTeacher,ShowWeekends=pref.ShowWeekends },
                    DocumentStore<TimetableState>.Hash(r.Arguments.GetRawText())); break;
            case "timetable.query" when caller == "Zheli.Miao":
                var settings = await core.Call<Snapshot<Preferences>>("settings.read");
                if (!settings.Data.AllowMiaoReadTimetable) return Protocol.Failure(r,"DENIED","请在哲里设置中允许哲喵本地查询课表。");
                var query = r.Args<QueryRange>();
                if (query.Through < query.From || query.Through.DayNumber-query.From.DayNumber > 31)
                    throw new DomainException("单次查询范围限制为32天。");
                var data = store.Read().Data;
                result = data.Semesters.SelectMany(s => CalendarEngine.Expand(data,s.Id))
                    .Where(o => o.Date >= query.From && o.Date <= query.Through)
                    .Select(o => new CourseSummary(o.Name,o.Room,o.Teacher,o.Date,o.Start,o.End,o.StartPeriod,o.EndPeriod,o.Status)).ToList(); break;
            case "timetable.prepare" when caller == "Zheli.Miao":
                await RequireMiaoWrite();
                var input=r.Args<PrepareChange>();var snapshot=store.Read();
                var arrangement=snapshot.Data.Arrangements.SingleOrDefault(a=>a.Id==input.Key.ArrangementId)??throw new DomainException("所选安排不存在。");
                var course=snapshot.Data.Courses.Single(c=>c.Id==arrangement.CourseId);
                var before=CalendarEngine.Expand(snapshot.Data,course.SemesterId).Single(o=>o.Key==input.Key);
                TimetableState proposed=input.Action switch
                {
                    "move" when input.Date!=null && input.StartPeriod!=null => Commands.Move(snapshot.Data,input.Key,input.Date.Value,input.StartPeriod.Value,input.Room),
                    "cancel" => Commands.Cancel(snapshot.Data,input.Key),
                    "restore" => Commands.RestoreOccurrence(snapshot.Data,input.Key),
                    _=>throw new DomainException("仅支持单次调课、停课和恢复；参数不完整。")
                };
                var after=CalendarEngine.Expand(proposed,course.SemesterId).Single(o=>o.Key==input.Key);
                var collisions=proposed.Semesters.SelectMany(s=>CalendarEngine.Conflicts(CalendarEngine.Expand(proposed,s.Id)))
                    .Select(c=>$"{c.First.Date:MM-dd} {c.First.Name} / {c.Second.Name}").ToList();
                var plan=prepared.Add(caller,snapshot.Revision,proposed,$"哲喵：{course.Name}单次{input.Action}");
                result=new ChangePreview(plan.Token,snapshot.Revision,Describe(before),Describe(after),collisions,plan.Expires);break;
            case "timetable.commit" when caller == "Zheli.Miao":
                await RequireMiaoWrite();var commit=r.Args<CommitChange>();var pending=prepared.Get(commit.Token,caller);
                result=store.Write(r.Id,pending.Revision,pending.Summary,_=>pending.State,
                    DocumentStore<TimetableState>.Hash("commit:"+commit.Token+":"+commit.AllowConflicts),s=>
                    {
                        CalendarEngine.Validate(s);
                        if(!commit.AllowConflicts && s.Semesters.Any(x=>CalendarEngine.Conflicts(CalendarEngine.Expand(s,x.Id)).Count>0))
                            throw new DomainException("存在课程冲突，尚未明确授权保留冲突。");
                    });break;
            case "timetable.miaoUndo" when caller == "Zheli.Miao":
                await RequireMiaoWrite();var miaoUndo=r.Args<UndoRequest>();
                if(!store.History().Any(x=>x.Id==miaoUndo.OperationId&&x.Summary.StartsWith("哲喵：",StringComparison.Ordinal)))
                    throw new DomainException("只能撤销哲喵发起的操作。");
                result=store.Undo(r.Id,miaoUndo.OperationId,miaoUndo.Revision,CalendarEngine.Validate);break;
            case "timetable.undo" when caller == "Zheli.Timetable":
                var undo = r.Args<UndoRequest>(); result = store.Undo(r.Id,undo.OperationId,undo.Revision,CalendarEngine.Validate); break;
            case "timetable.history" when caller is "Zheli.Timetable" or "Zheli.Settings": result = store.History(); break;
            case "timetable.backup" when caller is "Zheli.Timetable" or "Zheli.Settings":
                result = new { path = store.Backup(Path.Combine(AppPaths.DataRoot,"Backups","Timetable")) }; break;
            case "timetable.exportBackup" when caller == "Zheli.Settings":
                var export=r.Args<ExportBackup>();
                PortableBackup.Export(store,"Timetable",export.Path,export.Password,Path.Combine(AppPaths.DataRoot,"BackupStaging"));
                result=new{path=export.Path};break;
            case "timetable.restoreBackup" when caller == "Zheli.Settings":
                var restore=r.Args<RestoreBackup>();
                var restored=PortableBackup.ReadData<TimetableState>(restore.Path,restore.Password,"Timetable",Path.Combine(AppPaths.DataRoot,"BackupStaging"));
                CalendarEngine.Validate(restored);
                var safety=store.Backup(Path.Combine(AppPaths.DataRoot,"Backups","BeforeRestore"));
                result=store.Write(r.Id,restore.Revision,"恢复便携备份",_=>restored,
                    DocumentStore<TimetableState>.Hash("restore:"+JsonSerializer.Serialize(restored)),CalendarEngine.Validate);break;
            default: return Protocol.Failure(r,"DENIED","此应用没有该能力的权限，或能力未开放。");
        }
        return Protocol.Success(r,result);
    }
    catch(StoreException e) { return Protocol.Failure(r,e.Code,e.Message); }
    catch(BridgeException e) { return Protocol.Failure(r,e.Code,e.Message); }
    catch(Exception e) when(e is DomainException or ArgumentException or JsonException)
    { return Protocol.Failure(r,"VALIDATION",e.Message); }
    catch { return Protocol.Failure(r,"FAILED","操作未完成。请刷新核查数据，不要盲目重复写入。"); }
}
async Task RequireMiaoWrite()
{
    var permissions=(await core.Call<Snapshot<Preferences>>("settings.read")).Data;
    if(!permissions.AllowMiaoReadTimetable||!permissions.AllowMiaoWriteTimetable)
        throw new BridgeException("DENIED","请在哲里设置中分别允许哲喵查询与单次修改课表。");
}
string Describe(Occurrence o)=>$"{o.Name} · {o.Date:yyyy-MM-dd} 第{o.StartPeriod}—{o.EndPeriod}节 · {o.Room} · {o.Status}";
record TimetablePreferences(long Revision,bool ShowWeekends,bool ShowTeacher);
