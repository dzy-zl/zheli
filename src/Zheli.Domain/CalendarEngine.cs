namespace Zheli.Domain;

public static class CalendarEngine
{
    public static int WeekOf(Semester semester, DateOnly date) =>
        (int)Math.Floor((date.DayNumber - semester.WeekOneMonday.DayNumber) / 7d) + 1;
    public static DateOnly DateOf(Semester semester, int week, int day) =>
        semester.WeekOneMonday.AddDays((week - 1) * 7 + day - 1);

    public static Schedule ScheduleFor(TimetableState state, Semester semester, DateOnly date)
    {
        var week = WeekOf(semester, date);
        var use = semester.Schedules.Where(x => x.FromWeek <= week).MaxBy(x => x.FromWeek)
            ?? throw new DomainException("该日期没有可用作息。");
        return state.Schedules.Single(x => x.Id == use.ScheduleId);
    }

    public static IReadOnlyList<Occurrence> Expand(TimetableState state, string semesterId)
    {
        var semester = state.Semesters.SingleOrDefault(x => x.Id == semesterId)
            ?? throw new DomainException("学期不存在。");
        var result = new List<Occurrence>();
        foreach (var arrangement in state.Arrangements)
        {
            var course = state.Courses.Single(x => x.Id == arrangement.CourseId);
            if (course.SemesterId != semesterId) continue;
            foreach (var revision in arrangement.Revisions)
            foreach (var week in revision.Weeks.Distinct().Order())
            {
                if (week < revision.FromWeek || week > revision.ThroughWeek) continue;
                var date = DateOf(semester, week, revision.Day);
                if (date < semester.TeachingStart && !arrangement.Temporary) continue;
                var key = new OccurrenceKey(arrangement.Id, week);
                var change = state.Overrides.SingleOrDefault(x => x.Key == key);
                var target = change?.TargetDate ?? date;
                var start = change?.StartPeriod ?? revision.StartPeriod;
                var end = change?.EndPeriod ?? revision.EndPeriod;
                var schedule = ScheduleFor(state, semester, target);
                var first = schedule.Periods.SingleOrDefault(x => x.Number == start)
                    ?? throw new DomainException("课程引用了不存在的开始节次。");
                var last = schedule.Periods.SingleOrDefault(x => x.Number == end)
                    ?? throw new DomainException("课程引用了不存在的结束节次。");
                result.Add(new(key, course.Id, semester.Id, course.Name,
                    revision.Teacher ?? course.Teacher, course.Color, course.Notes,
                    change?.Room ?? revision.Room, target, start, end, first.Start, last.End,
                    date, revision.StartPeriod, revision.EndPeriod, revision.Room,
                    change?.Cancelled ?? false, change != null, arrangement.Temporary, change?.Reason ?? ""));
            }
        }
        return result.OrderBy(x => x.Date).ThenBy(x => x.Start).ToList();
    }

    public static IReadOnlyList<Conflict> Conflicts(IEnumerable<Occurrence> occurrences)
    {
        var result = new List<Conflict>();
        var active = occurrences.Where(x => !x.Cancelled).ToArray();
        for (var i = 0; i < active.Length; i++)
        for (var j = i + 1; j < active.Length; j++)
        {
            var a = active[i]; var b = active[j];
            if (a.Date == b.Date && a.Start < b.End && b.Start < a.End)
                result.Add(new(a, b));
        }
        return result;
    }

    public static List<int> ParseWeeks(string text, int maximum)
    {
        var result = new SortedSet<int>();
        foreach (var part in text.Replace('，', ',').Replace('—', '-').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Trim().Split('-');
            if (range.Length > 2 || !int.TryParse(range[0], out var from))
                throw new DomainException("周次格式应为 1-4,7,9-12。");
            var to = from;
            if (range.Length == 2 && !int.TryParse(range[1], out to)) throw new DomainException("周次格式错误。");
            if (from < 1 || to > maximum || to < from) throw new DomainException("周次超出学期范围。");
            for (var week = from; week <= to; week++) result.Add(week);
        }
        if (result.Count == 0) throw new DomainException("至少选择一个上课周。");
        return result.ToList();
    }

    public static void Validate(TimetableState state)
    {
        Unique(state.Semesters.Select(x => x.Id)); Unique(state.Schedules.Select(x => x.Id));
        Unique(state.Courses.Select(x => x.Id)); Unique(state.Arrangements.Select(x => x.Id));
        Unique(state.Overrides.Select(x => x.Id));
        if (state.CurrentSemesterId != null && state.Semesters.All(x => x.Id != state.CurrentSemesterId))
            throw new DomainException("当前学期不存在。");
        foreach (var schedule in state.Schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.Name) || schedule.Periods.Count is < 1 or > 30)
                throw new DomainException("作息需要名称和1至30个节次。");
            for (var i = 0; i < schedule.Periods.Count; i++)
            {
                var p = schedule.Periods[i];
                if (p.Number != i + 1 || p.End <= p.Start || (i > 0 && p.Start < schedule.Periods[i - 1].End))
                    throw new DomainException("节次必须连续编号、按时间排列，且不能重叠。");
            }
        }
        foreach (var semester in state.Semesters)
        {
            if (string.IsNullOrWhiteSpace(semester.Name) || semester.WeekOneMonday.DayOfWeek != DayOfWeek.Monday || semester.Weeks is < 1 or > 60)
                throw new DomainException("请填写学期名称、第1周周一及1至60的周数。");
            if (WeekOf(semester, semester.TeachingStart) < 1 || WeekOf(semester, semester.TeachingStart) > semester.Weeks)
                throw new DomainException("开课日期必须在学期内。");
            if (semester.Schedules.Count == 0 || semester.Schedules.Min(x => x.FromWeek) != 1 ||
                semester.Schedules.Select(x => x.FromWeek).Distinct().Count() != semester.Schedules.Count ||
                semester.Schedules.Any(x => x.FromWeek < 1 || x.FromWeek > semester.Weeks || state.Schedules.All(s => s.Id != x.ScheduleId)))
                throw new DomainException("作息时间线必须从第1周开始且每个生效周唯一。");
        }
        foreach (var c in state.Courses)
        {
            if (state.Semesters.All(x => x.Id != c.SemesterId) || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 200)
                throw new DomainException("课程名称为空、过长或学期不存在。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(c.Color, "^#[0-9A-Fa-f]{6}$"))
                throw new DomainException("课程颜色应为六位十六进制颜色。");
        }
        foreach (var a in state.Arrangements)
        {
            var c = state.Courses.SingleOrDefault(x => x.Id == a.CourseId) ?? throw new DomainException("安排所属课程不存在。");
            var semester = state.Semesters.Single(x => x.Id == c.SemesterId);
            var covered = new HashSet<int>();
            foreach (var r in a.Revisions.OrderBy(x => x.FromWeek))
            {
                if (r.FromWeek < 1 || r.ThroughWeek > semester.Weeks || r.FromWeek > r.ThroughWeek || r.Day is < 1 or > 7 ||
                    r.StartPeriod < 1 || r.EndPeriod < r.StartPeriod || r.Weeks.Count == 0 ||
                    r.Weeks.Any(w => w < r.FromWeek || w > r.ThroughWeek) || r.Weeks.Distinct().Count() != r.Weeks.Count)
                    throw new DomainException("安排的周次、星期或节次无效。");
                for (var w = r.FromWeek; w <= r.ThroughWeek; w++)
                    if (!covered.Add(w)) throw new DomainException("同一安排的版本生效范围不能重叠。");
                foreach (var w in r.Weeks)
                    if (r.EndPeriod > ScheduleFor(state, semester, DateOf(semester, w, r.Day)).Periods.Count)
                        throw new DomainException("安排超出所用作息的节次数量。");
            }
            if (a.Revisions.Count == 0 || (a.Temporary && a.Revisions.Sum(x => x.Weeks.Count) != 1))
                throw new DomainException("临时课程必须只有一次；安排不能为空。");
        }
        if (state.Overrides.Select(x => x.Key).Distinct().Count() != state.Overrides.Count)
            throw new DomainException("同一次课程只能有一个当前变动。");
        foreach (var o in state.Overrides)
        {
            var a = state.Arrangements.SingleOrDefault(x => x.Id == o.Key.ArrangementId) ?? throw new DomainException("变动所属安排不存在。");
            var r = a.Revisions.SingleOrDefault(x => x.Weeks.Contains(o.Key.SourceWeek)) ?? throw new DomainException("已有单次变动失去原安排，请先处理。");
            var c = state.Courses.Single(x => x.Id == a.CourseId);
            var s = state.Semesters.Single(x => x.Id == c.SemesterId);
            var original = DateOf(s, o.Key.SourceWeek, r.Day);
            if (original < s.TeachingStart && !a.Temporary) throw new DomainException("变动关联了开学前的无效课程。");
            var target = o.TargetDate ?? original;
            if (WeekOf(s, target) < 1 || WeekOf(s, target) > s.Weeks) throw new DomainException("调课目标必须在同一学期。");
            var from = o.StartPeriod ?? r.StartPeriod; var end = o.EndPeriod ?? r.EndPeriod;
            if (from < 1 || end < from || end > ScheduleFor(state, s, target).Periods.Count)
                throw new DomainException("调课节次超出范围。");
        }
    }

    private static void Unique(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        if (list.Any(string.IsNullOrWhiteSpace) || list.Count != list.Distinct().Count())
            throw new DomainException("业务编号重复或为空。");
    }
}
