using System.Text.RegularExpressions;

namespace Zheli.Domain;

public enum MiaoAction { QueryTimetable, SearchFiles, CancelSelected, ChangeSelected, UndoChange }

public sealed record MiaoIntent(MiaoAction Action, DateOnly? From = null, DateOnly? Through = null,
    bool FirstOnly = false, string? SearchTerm = null);

// Recognize only explicit local requests. Unknown or mixed requests remain normal chat.
public static class MiaoIntentParser
{
    private static readonly Regex FileSearch = new(
        @"^(?:请|帮我)?(?:搜索|查找|检索)(?:一下)?(?:本地)?(?:文件|资料|知识库)(?:中|里|中的|里的)?[\s：:]+(?<term>.+?)[？?]?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Date = new(@"(?<!\d)(?<year>20\d{2})-(?<month>\d{1,2})-(?<day>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MiaoIntent? Parse(string? input, DateOnly today)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text is "取消所选课程" or "所选课程停一次" or "这节课停一次") return new(MiaoAction.CancelSelected);
        if (text is "调整所选课程" or "调课所选课程") return new(MiaoAction.ChangeSelected);
        if (text is "撤销哲喵修改" or "撤销上次调课") return new(MiaoAction.UndoChange);

        var file = FileSearch.Match(text);
        if (file.Success)
        {
            var term = file.Groups["term"].Value.Trim();
            return term.Length is >= 1 and <= 80 ? new(MiaoAction.SearchFiles, SearchTerm: term) : null;
        }

        if (!Regex.IsMatch(text, @"课表|课程|上什么课|有课|第[一1]节课|首节课") ||
            Regex.IsMatch(text, @"文件|资料|天气|同时|顺便|以及|并且|然后|或者|取消|删除|调整|修改|调课|停课|移动|改到|挪到|和|如何|怎么|为什么|解释|准备|分析|建议|推荐|提醒|通知")) return null;

        DateOnly? from = null, through = null;
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var date = Date.Match(text);
        if (date.Success)
        {
            if (!DateOnly.TryParseExact(date.Value, "yyyy-M-d", out var explicitDate)) return null;
            from = through = explicitDate;
        }
        else if (text.Contains("下周", StringComparison.Ordinal))
        {
            from = monday.AddDays(7);
            through = from.Value.AddDays(6);
        }
        else if (text.Contains("本周", StringComparison.Ordinal) || text.Contains("这周", StringComparison.Ordinal))
        {
            from = monday;
            through = monday.AddDays(6);
        }
        else if (text.Contains("后天", StringComparison.Ordinal)) from = through = today.AddDays(2);
        else if (text.Contains("明天", StringComparison.Ordinal)) from = through = today.AddDays(1);
        else if (text.Contains("今天", StringComparison.Ordinal)) from = through = today;
        else if (text.Contains("昨天", StringComparison.Ordinal)) from = through = today.AddDays(-1);
        else if (Regex.IsMatch(text, @"(?:周|星期)[一二三四五六日天]"))
        {
            from = monday;
            through = monday.AddDays(6);
        }
        else return null;

        var weekday = Regex.Match(text, @"(?:周|星期)(?<day>[一二三四五六日天])");
        if (weekday.Success && date.Success) return null;
        if (weekday.Success)
        {
            var index = "一二三四五六日天".IndexOf(weekday.Groups["day"].Value[0]);
            if (index == 7) index = 6;
            // A weekday without an explicit week refers to this week.
            var start = text.Contains("下周", StringComparison.Ordinal) ? monday.AddDays(7) : monday;
            from = through = start.AddDays(index);
        }
        return new(MiaoAction.QueryTimetable, from, through,
            FirstOnly: Regex.IsMatch(text, @"第[一1]节课|首节课"));
    }
}
