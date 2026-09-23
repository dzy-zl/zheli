using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zheli.Domain;
using Zheli.Storage;
using Zheli.Platform;
using Zheli.Bridge;
using Zheli.Contracts;

var passed=0;var failed=0;
void Test(string name,Action action)
{
    try{action();Console.WriteLine("PASS "+name);passed++;}
    catch(Exception e){Console.WriteLine("FAIL "+name+" :: "+e.Message);failed++;}
}
void Eq<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"expected {expected}, got {actual}");}
void Reject(Action action){try{action();}catch(Exception e)when(e is DomainException or StoreException or InvalidDataException or ArgumentException){return;}throw new Exception("expected rejection");}
TimetableState Fixture()
{
    var periods=Enumerable.Range(1,10).Select(n=>new Period(n,new TimeOnly(8,0).AddMinutes((n-1)*55),new TimeOnly(8,45).AddMinutes((n-1)*55))).ToList();
    return new(){CurrentSemesterId="s",Schedules=[new("summer","夏季",periods)],Semesters=[new("s","学期",new(2026,9,7),new(2026,9,9),18,[new(1,"summer")])],
        Courses=[new("math","s","高等数学","教师","#85A8C9","")],Arrangements=[new("a","math",false,[new(1,16,1,3,4,Enumerable.Range(1,16).ToList(),"302")])]};
}
List<Occurrence> Expand(TimetableState s)=>CalendarEngine.Expand(s,"s").ToList();
Test("baseline valid",()=>CalendarEngine.Validate(Fixture()));
Test("week anchor Monday",()=>Eq(1,CalendarEngine.WeekOf(Fixture().Semesters[0],new(2026,9,9))));
Test("date before first Monday is week zero",()=>Eq(0,CalendarEngine.WeekOf(Fixture().Semesters[0],new(2026,9,6))));
Test("pre-teaching recurring excluded",()=>Eq(15,Expand(Fixture()).Count));
Test("manual pre-teaching temporary allowed",()=>{var s=Fixture();s=s with{Arrangements=[new("a","math",true,[new(1,1,1,3,4,[1],"302")])]};CalendarEngine.Validate(s);Eq(1,Expand(s).Count);});
Test("odd weeks only",()=>{var s=Fixture();s=s with{Arrangements=[new("a","math",false,[new(1,16,3,3,4,Enumerable.Range(1,16).Where(w=>w%2==1).ToList(),"302")])]};Eq(8,Expand(s).Count);});
Test("noncontinuous weeks",()=>Eq("1,2,3,4,7,9,10,11,12",string.Join(',',CalendarEngine.ParseWeeks("1-4,7,9-12",18))));
Test("reject descending range",()=>Reject(()=>CalendarEngine.ParseWeeks("5-2",18)));
Test("reject week beyond semester",()=>Reject(()=>CalendarEngine.ParseWeeks("19",18)));
Test("reject empty weeks",()=>Reject(()=>CalendarEngine.ParseWeeks("",18)));
Test("cross-week move preserves two periods",()=>{var s=Commands.Move(Fixture(),new("a",3),new(2026,10,2),6);var o=Expand(s).Single(x=>x.Key.SourceWeek==3);Eq(7,o.EndPeriod);Eq(new DateOnly(2026,10,2),o.Date);Eq(new DateOnly(2026,9,21),o.OriginalDate);Eq(15,Expand(s).Count);});
Test("out-of-period move rejected",()=>Reject(()=>Commands.Move(Fixture(),new("a",3),new(2026,10,2),10)));
Test("out-of-semester move rejected",()=>Reject(()=>Commands.Move(Fixture(),new("a",3),new(2027,12,2),6)));
Test("cancelled occupies no time",()=>{var s=Commands.Cancel(Fixture(),new("a",3));Eq(1,Expand(s).Count(x=>x.Cancelled));Eq(14,Expand(s).Count(x=>!x.Cancelled));});
Test("restore override",()=>{var s=Commands.Cancel(Fixture(),new("a",3));s=Commands.RestoreOccurrence(s,new("a",3));Eq(0,s.Overrides.Count);});
Test("revision preserves prior weeks",()=>{var s=Commands.Revise(Fixture(),"a",new(6,16,4,3,4,Enumerable.Range(6,11).ToList(),"105"));var rows=Expand(s);Eq(DayOfWeek.Monday,rows.Single(x=>x.Key.SourceWeek==5).Date.DayOfWeek);Eq(DayOfWeek.Thursday,rows.Single(x=>x.Key.SourceWeek==6).Date.DayOfWeek);});
Test("revision retains explicit future move",()=>{var s=Commands.Move(Fixture(),new("a",8),new(2026,10,30),6);s=Commands.Revise(s,"a",new(6,16,4,3,4,Enumerable.Range(6,11).ToList(),"105"));Eq(new DateOnly(2026,10,30),Expand(s).Single(x=>x.Key.SourceWeek==8).Date);});
Test("orphaned exception blocks edit",()=>{var s=Commands.Cancel(Fixture(),new("a",8));Reject(()=>Commands.Revise(s,"a",new(6,16,4,3,4,[6,7,9,10],"105")));});
Test("season schedule keeps period IDs",()=>{var s=Fixture();var winter=s.Schedules[0] with{Id="winter",Name="冬季",Periods=s.Schedules[0].Periods.Select(p=>p with{Start=p.Start.AddMinutes(10),End=p.End.AddMinutes(10)}).ToList()};s=s with{Schedules=s.Schedules.Append(winter).ToList(),Semesters=[s.Semesters[0] with{Schedules=[new(1,"summer"),new(9,"winter")]}]};CalendarEngine.Validate(s);Eq(3,Expand(s).Single(x=>x.Key.SourceWeek==9).StartPeriod);Eq(new TimeOnly(10,0),Expand(s).Single(x=>x.Key.SourceWeek==9).Start);});
Test("same period conflict detected",()=>{var s=Fixture();s=s with{Arrangements=s.Arrangements.Append(new("b","math",true,[new(3,3,1,4,5,[3],"105")])).ToList()};Eq(1,CalendarEngine.Conflicts(Expand(s)).Count);});
Test("adjacent lessons do not conflict",()=>{var s=Fixture();s=s with{Arrangements=s.Arrangements.Append(new("b","math",true,[new(3,3,1,5,6,[3],"105")])).ToList()};Eq(0,CalendarEngine.Conflicts(Expand(s)).Count);});
Test("cancel clears conflict",()=>{var s=Fixture();s=s with{Arrangements=s.Arrangements.Append(new("b","math",true,[new(3,3,1,3,4,[3],"105")])).ToList()};s=Commands.Cancel(s,new("a",3));Eq(0,CalendarEngine.Conflicts(Expand(s)).Count);});
Test("duplicate IDs rejected",()=>{var s=Fixture();Reject(()=>CalendarEngine.Validate(s with{Courses=s.Courses.Concat(s.Courses).ToList()}));});
Test("non-Monday anchor rejected",()=>{var s=Fixture();Reject(()=>CalendarEngine.Validate(s with{Semesters=[s.Semesters[0] with{WeekOneMonday=new(2026,9,8)}]}));});
Test("overlapping schedule rejected",()=>{var s=Fixture();s.Schedules[0].Periods[1]=new(2,new(8,20),new(9,0));Reject(()=>CalendarEngine.Validate(s));});
Test("invalid course color rejected",()=>{var s=Fixture();Reject(()=>CalendarEngine.Validate(s with{Courses=[s.Courses[0] with{Color="invalid"}]}));});
Test("delete course preserves recoverable bundle",()=>{var s=Commands.Cancel(Fixture(),new("a",3));s=Commands.DeleteCourse(s,"math");Eq(0,s.Courses.Count);Eq(0,s.Overrides.Count);Eq(1,s.Trash.Count);s=Commands.RestoreDeleted(s,s.Trash[0].Id);Eq(1,s.Courses.Count);Eq(1,s.Overrides.Count);});
Test("delete last arrangement retains course",()=>{var s=Commands.DeleteArrangement(Fixture(),"a");Eq(1,s.Courses.Count);Eq(0,s.Arrangements.Count);});
Test("prepared change checks owner",()=>{var p=new PreparedChanges();var plan=p.Add("Miao",1,Fixture(),"move");Reject(()=>p.Get(plan.Token,"untrusted"));});
Test("prepared change expires",()=>{var now=DateTimeOffset.UtcNow;var p=new PreparedChanges(()=>now);var plan=p.Add("Miao",1,Fixture(),"move");now=now.AddMinutes(4);Reject(()=>p.Get(plan.Token,"Miao"));});
Test("prepared change retains captured revision",()=>{var p=new PreparedChanges();var plan=p.Add("Miao",7,Fixture(),"move");Eq(7L,p.Get(plan.Token,"Miao").Revision);});

var testRoot=Path.Combine(Path.GetTempPath(),"zheli-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
DocumentStore<TimetableState> Store()=>new(Path.Combine(testRoot,Guid.NewGuid()+".db"));
Test("SQLite persistence",()=>{var store=Store();var s=Fixture();store.Write(Guid.NewGuid().ToString(),0,"seed",_=>s,"seed",CalendarEngine.Validate);Eq(1L,store.Read().Revision);Eq("高等数学",store.Read().Data.Courses[0].Name);});
Test("transaction rollback on validation",()=>{var store=Store();Reject(()=>store.Write(Guid.NewGuid().ToString(),0,"bad",_=>Fixture(),"bad",_=>throw new DomainException("no")));Eq(0L,store.Read().Revision);Eq(0,store.History().Count);});
Test("optimistic concurrency",()=>{var store=Store();store.Write(Guid.NewGuid().ToString(),0,"first",_=>Fixture(),"1");Reject(()=>store.Write(Guid.NewGuid().ToString(),0,"stale",_=>new(),"2"));Eq(1L,store.Read().Revision);});
Test("prepared modification cannot overwrite intervening save",()=>{var store=Store();store.Write(Guid.NewGuid().ToString(),0,"seed",_=>Fixture(),"seed");var snapshot=store.Read();var queue=new PreparedChanges();var plan=queue.Add("Miao",snapshot.Revision,Commands.Cancel(snapshot.Data,new("a",3)),"cancel");store.Write(Guid.NewGuid().ToString(),1,"display",s=>s with{ShowTeacher=false},"display");Reject(()=>store.Write(Guid.NewGuid().ToString(),plan.Revision,plan.Summary,_=>plan.State,"commit",CalendarEngine.Validate));Eq(false,store.Read().Data.ShowTeacher);Eq(0,store.Read().Data.Overrides.Count);});
Test("idempotency returns original receipt",()=>{var store=Store();var id=Guid.NewGuid().ToString();var first=store.Write(id,0,"first",_=>Fixture(),"1");var second=store.Write(id,0,"first",_=>throw new Exception("must not execute"),"1");Eq(first.OperationId,second.OperationId);Eq(true,second.Replayed);Eq(1L,store.Read().Revision);});
Test("same id with different payload rejected",()=>{var store=Store();var id=Guid.NewGuid().ToString();store.Write(id,0,"first",_=>Fixture(),"1");Reject(()=>store.Write(id,1,"different",_=>new(),"2"));});
Test("undo restores prior state",()=>{var store=Store();var r=store.Write(Guid.NewGuid().ToString(),0,"first",_=>Fixture(),"1");store.Undo(Guid.NewGuid().ToString(),r.OperationId,r.Revision);Eq(0,store.Read().Data.Courses.Count);Eq(2L,store.Read().Revision);});
Test("undo rejects subsequent changes",()=>{var store=Store();var r=store.Write(Guid.NewGuid().ToString(),0,"first",_=>Fixture(),"1");store.Write(Guid.NewGuid().ToString(),1,"second",s=>s with{ShowTeacher=false},"2");Reject(()=>store.Undo(Guid.NewGuid().ToString(),r.OperationId,2));Eq(false,store.Read().Data.ShowTeacher);});
Test("SQLite backup reopens",()=>{var store=Store();store.Write(Guid.NewGuid().ToString(),0,"first",_=>Fixture(),"1");var backup=store.Backup(Path.Combine(testRoot,"backups"));var copy=new DocumentStore<TimetableState>(backup);Eq(1,copy.Read().Data.Courses.Count);});
Test("encrypted backup roundtrip",()=>{var store=Store();store.Write(Guid.NewGuid().ToString(),0,"first",_=>Fixture(),"1");var path=Path.Combine(testRoot,"encrypted.zhelibackup");PortableBackup.Export(store,"Timetable",path,"test-password-2026",testRoot);var data=PortableBackup.ReadData<TimetableState>(path,"test-password-2026","Timetable",testRoot);Eq("高等数学",data.Courses[0].Name);});
Test("encrypted backup rejects wrong password",()=>{var path=Path.Combine(testRoot,"encrypted.zhelibackup");Reject(()=>PortableBackup.ReadData<TimetableState>(path,"wrong-password","Timetable",testRoot));});
Test("encrypted backup rejects wrong app",()=>{var path=Path.Combine(testRoot,"encrypted.zhelibackup");Reject(()=>PortableBackup.ReadData<TimetableState>(path,"test-password-2026","Miao",testRoot));});
Test("encrypted backup refuses overwriting existing file",()=>{var path=Path.Combine(testRoot,"encrypted.zhelibackup");var before=File.ReadAllBytes(path);try{PortableBackup.Export(Store(),"Timetable",path,"test-password-2026",testRoot);throw new Exception("expected refusal");}catch(IOException){}Eq(true,before.SequenceEqual(File.ReadAllBytes(path)));});
Test("portable backup cleans unencrypted staging snapshot",()=>{var directory=Path.Combine(testRoot,"staging");PortableBackup.Export(Store(),"Timetable",Path.Combine(testRoot,"clean.zhelibackup"),"test-password-2026",directory);Eq(0,Directory.GetFiles(directory).Length);});
Test("encrypted backup tamper rejected",()=>{var bytes=File.ReadAllBytes(Path.Combine(testRoot,"encrypted.zhelibackup"));bytes[^1]^=0x80;var path=Path.Combine(testRoot,"tampered.zhelibackup");File.WriteAllBytes(path,bytes);Reject(()=>PortableBackup.ReadData<TimetableState>(path,"test-password-2026","Timetable",testRoot));});
Test("draft survives reopening",()=>{var path=Path.Combine(testRoot,"draft.json");new DraftStore<Course>(path).Save(Fixture().Courses[0]);Eq("高等数学",new DraftStore<Course>(path).Load()!.Name);});
Test("unknown future schema rejected",()=>{var path=Path.Combine(testRoot,"future.db");using(var c=new SqliteConnection($"Data Source={path}")){c.Open();using var command=c.CreateCommand();command.CommandText="PRAGMA user_version=99";command.ExecuteNonQuery();}Reject(()=>new DocumentStore<TimetableState>(path));});
Test("wire message roundtrip",()=>{using var stream=new MemoryStream();var request=Protocol.Make("test");Wire.Write(stream,request,CancellationToken.None).GetAwaiter().GetResult();stream.Position=0;Eq(request.Id,Wire.Read<Request>(stream,CancellationToken.None).GetAwaiter().GetResult().Id);});
Test("wire rejects oversized frame",()=>{using var stream=new MemoryStream(BitConverter.GetBytes(Protocol.MaxMessageBytes+1));Reject(()=>Wire.Read<Request>(stream,CancellationToken.None).GetAwaiter().GetResult());});
Test("local search only authorized txt/md",()=>{var folder=Path.Combine(testRoot,"knowledge");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"notes.md"),"第一行\n高等数学复习");File.WriteAllText(Path.Combine(folder,"skip.json"),"高等数学");var result=new LocalTextSearch().Search(folder,"高等数学",CancellationToken.None).GetAwaiter().GetResult();Eq(1,result.Count);Eq(2,result[0].Line);});
if(!OperatingSystem.IsWindows())Test("local search skips symlink escape",()=>{var folder=Path.Combine(testRoot,"linked");Directory.CreateDirectory(folder);var outside=Path.Combine(testRoot,"outside.txt");File.WriteAllText(outside,"秘密内容");File.CreateSymbolicLink(Path.Combine(folder,"link.txt"),outside);Eq(0,new LocalTextSearch().Search(folder,"秘密",CancellationToken.None).GetAwaiter().GetResult().Count);});
KnowledgeTests.Run(Test,testRoot);
PetDesktopTests.Run(Test,testRoot);
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
Console.WriteLine("Windows UI, named-pipe identity, credential vault and live DeepSeek are not covered by this cross-platform runner.");
Environment.ExitCode=failed==0?0:1;
