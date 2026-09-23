using System.Text.Json;
using Zheli.Domain;
namespace Zheli.Contracts;

public sealed record Request(string Id, string Method, JsonElement Arguments, int Protocol = 1);
public sealed record Response(string Id, bool Ok, JsonElement? Result, string? Code = null, string? Error = null);
public sealed record SaveTimetable(long Revision, TimetableState State, bool AllowConflicts = false);
public sealed record SavePreferences(long Revision, Preferences Preferences);
public sealed record UndoRequest(long Revision, string OperationId);
public sealed record QueryRange(DateOnly From, DateOnly Through);
public sealed record CourseSummary(string Name, string Room, string Teacher, DateOnly Date,
    TimeOnly Start, TimeOnly End, int StartPeriod, int EndPeriod, string Status);
public sealed record ChatContext(string SemesterId, int Week, OccurrenceKey? Selected);
public sealed record PrepareChange(OccurrenceKey Key,string Action,DateOnly? Date=null,int? StartPeriod=null,string? Room=null);
public sealed record ChangePreview(string Token,long Revision,string Before,string After,List<string> Conflicts,DateTimeOffset Expires);
public sealed record CommitChange(string Token,bool AllowConflicts=false);
public sealed record ExportBackup(string Path,string Password);
public sealed record RestoreBackup(string Path,string Password,long Revision);
public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public static Request Make(string method, object? args = null, string? id = null) =>
        new(id ?? Guid.NewGuid().ToString(), method, JsonSerializer.SerializeToElement(args ?? new { }, Json));
    public static T Args<T>(this Request r) => r.Arguments.Deserialize<T>(Json) ?? throw new ArgumentException("缺少参数。");
    public static Response Success(Request r, object value) => new(r.Id, true, JsonSerializer.SerializeToElement(value, Json));
    public static Response Failure(Request r, string code, string error) => new(r.Id, false, null, code, error);
}
