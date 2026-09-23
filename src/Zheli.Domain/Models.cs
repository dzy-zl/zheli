namespace Zheli.Domain;

public sealed record Period(int Number, TimeOnly Start, TimeOnly End);
public sealed record Schedule(string Id, string Name, List<Period> Periods);
public sealed record ScheduleUse(int FromWeek, string ScheduleId);
public sealed record Semester(string Id, string Name, DateOnly WeekOneMonday,
    DateOnly TeachingStart, int Weeks, List<ScheduleUse> Schedules);
public sealed record Course(string Id, string SemesterId, string Name, string Teacher,
    string Color, string Notes);
// Revisions are effective intervals of the same stable arrangement. Past revisions remain intact.
public sealed record ArrangementRevision(int FromWeek, int ThroughWeek, int Day,
    int StartPeriod, int EndPeriod, List<int> Weeks, string Room, string? Teacher = null);
public sealed record Arrangement(string Id, string CourseId, bool Temporary,
    List<ArrangementRevision> Revisions);
public sealed record OccurrenceKey(string ArrangementId, int SourceWeek);
public sealed record OccurrenceOverride(string Id, OccurrenceKey Key, bool Cancelled,
    DateOnly? TargetDate, int? StartPeriod, int? EndPeriod, string? Room, string Reason);
public sealed record DeletedBundle(string Id,DateTimeOffset DeletedAt,Course? Course,
    List<Arrangement> Arrangements,List<OccurrenceOverride> Overrides);
public sealed record TimetableState
{
    public string? CurrentSemesterId { get; init; }
    public List<Semester> Semesters { get; init; } = [];
    public List<Schedule> Schedules { get; init; } = [];
    public List<Course> Courses { get; init; } = [];
    public List<Arrangement> Arrangements { get; init; } = [];
    public List<OccurrenceOverride> Overrides { get; init; } = [];
    public List<DeletedBundle> Trash { get; init; } = [];
    public bool ShowWeekends { get; init; } = true;
    public bool ShowTeacher { get; init; } = true;
}
public sealed record Occurrence(OccurrenceKey Key, string CourseId, string SemesterId,
    string Name, string Teacher, string Color, string Notes, string Room,
    DateOnly Date, int StartPeriod, int EndPeriod, TimeOnly Start, TimeOnly End,
    DateOnly OriginalDate, int OriginalStart, int OriginalEnd, string OriginalRoom,
    bool Cancelled, bool Changed, bool Temporary, string Reason)
{
    public string Status => Cancelled ? "本次停课" : Changed ? "已调整" : Temporary ? "临时增加" : "正常";
    public bool Moved => Date != OriginalDate || StartPeriod != OriginalStart || EndPeriod != OriginalEnd;
    public int PeriodCount => EndPeriod - StartPeriod + 1;
}
public sealed record Conflict(Occurrence First, Occurrence Second);
public sealed class DomainException(string message) : Exception(message);

public sealed record Preferences
{
    public string Theme { get; init; } = "System";
    public string Accent { get; init; } = "#7195B7";
    public double GlassClarity { get; init; } = 0.68;
    public bool ReduceTransparency { get; init; }
    public bool ReduceMotion { get; init; }
    public double TextScale { get; init; } = 1;
    public bool AllowMiaoReadTimetable { get; init; }
    public bool AllowMiaoWriteTimetable { get; init; }
    public bool AllowCloudTimetable { get; init; }
    public string Model { get; init; } = "";
    public bool ShowPet { get; init; } = true;
    public List<KnowledgeFolder> KnowledgeFolders { get; init; } = [];
}
public sealed record KnowledgeFolder(string Id,string Path,bool Enabled=true,bool AllowCloud=false,bool Private=false);
