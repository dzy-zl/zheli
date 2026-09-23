namespace Zheli.Domain;

public static class Commands
{
    public static TimetableState DeleteCourse(TimetableState state,string courseId)
    {
        var course=state.Courses.SingleOrDefault(c=>c.Id==courseId)??throw new DomainException("课程不存在。");
        var arrangements=state.Arrangements.Where(a=>a.CourseId==courseId).ToList();
        return Delete(state,course,arrangements);
    }
    public static TimetableState DeleteArrangement(TimetableState state,string arrangementId)
    {
        var arrangement=state.Arrangements.SingleOrDefault(a=>a.Id==arrangementId)??throw new DomainException("安排不存在。");
        return Delete(state,null,[arrangement]);
    }
    private static TimetableState Delete(TimetableState state,Course? course,List<Arrangement> arrangements)
    {
        var ids=arrangements.Select(a=>a.Id).ToHashSet();
        var bundle=new DeletedBundle(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,course,arrangements,state.Overrides.Where(o=>ids.Contains(o.Key.ArrangementId)).ToList());
        var next=state with{Courses=state.Courses.Where(c=>c.Id!=course?.Id).ToList(),Arrangements=state.Arrangements.Where(a=>!ids.Contains(a.Id)).ToList(),
            Overrides=state.Overrides.Where(o=>!ids.Contains(o.Key.ArrangementId)).ToList(),Trash=state.Trash.Append(bundle).ToList()};
        CalendarEngine.Validate(next);return next;
    }
    public static TimetableState RestoreDeleted(TimetableState state,string bundleId)
    {
        var bundle=state.Trash.SingleOrDefault(x=>x.Id==bundleId)??throw new DomainException("回收站项目不存在。");
        var next=state with{Courses=bundle.Course==null?state.Courses:state.Courses.Append(bundle.Course).ToList(),
            Arrangements=state.Arrangements.Concat(bundle.Arrangements).ToList(),Overrides=state.Overrides.Concat(bundle.Overrides).ToList(),Trash=state.Trash.Where(x=>x.Id!=bundleId).ToList()};
        CalendarEngine.Validate(next);return next;
    }
    public static TimetableState Revise(TimetableState state, string arrangementId, ArrangementRevision next)
    {
        var a = state.Arrangements.SingleOrDefault(x => x.Id == arrangementId) ?? throw new DomainException("安排不存在。");
        if (a.Temporary) throw new DomainException("临时课程请使用单次调整。");
        var earlier = a.Revisions.Where(x => x.FromWeek < next.FromWeek)
            .Select(x => x with { ThroughWeek = Math.Min(x.ThroughWeek, next.FromWeek - 1), Weeks = x.Weeks.Where(w => w < next.FromWeek).ToList() })
            .Where(x => x.Weeks.Count > 0).ToList();
        earlier.Add(next);
        var result = state with { Arrangements = state.Arrangements.Select(x => x.Id == a.Id ? x with { Revisions = earlier } : x).ToList() };
        CalendarEngine.Validate(result); // orphaned overrides are rejected, never silently removed
        return result;
    }

    public static TimetableState Move(TimetableState state, OccurrenceKey key, DateOnly target, int start, string? room = null)
    {
        var a = state.Arrangements.Single(x => x.Id == key.ArrangementId);
        var c = state.Courses.Single(x => x.Id == a.CourseId);
        var current = CalendarEngine.Expand(state, c.SemesterId).Single(x => x.Key == key);
        var change = new OccurrenceOverride(Guid.NewGuid().ToString("N"), key, false, target,
            start, start + current.PeriodCount - 1, room ?? current.Room, "单次调课");
        return PutOverride(state, change);
    }

    public static TimetableState Cancel(TimetableState state, OccurrenceKey key)
    {
        var existing = state.Overrides.SingleOrDefault(x => x.Key == key);
        return PutOverride(state, existing is null
            ? new(Guid.NewGuid().ToString("N"), key, true, null, null, null, null, "本次停课")
            : existing with { Cancelled = true });
    }

    public static TimetableState PutOverride(TimetableState state, OccurrenceOverride change)
    {
        var result = state with { Overrides = state.Overrides.Where(x => x.Key != change.Key).Append(change).ToList() };
        CalendarEngine.Validate(result); return result;
    }

    public static TimetableState RestoreOccurrence(TimetableState state, OccurrenceKey key) =>
        state with { Overrides = state.Overrides.Where(x => x.Key != key).ToList() };
}
