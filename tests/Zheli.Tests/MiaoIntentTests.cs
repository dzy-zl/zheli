using Zheli.Domain;

internal static class MiaoIntentTests
{
    public static void Run(Action<string,Action> test)
    {
        var today=new DateOnly(2026,9,28); // Monday
        void Expect(string question,MiaoIntent? expected)=>test("miao intent: "+question,()=>
        {
            var actual=MiaoIntentParser.Parse(question,today);
            if(actual!=expected)throw new InvalidDataException($"expected {expected}, got {actual}");
        });
        Expect("明天第一节课是什么？",new(MiaoAction.QueryTimetable,new DateOnly(2026,9,29),new DateOnly(2026,9,29),true));
        Expect("明天有什么课？",new(MiaoAction.QueryTimetable,new DateOnly(2026,9,29),new DateOnly(2026,9,29)));
        Expect("下周三有什么课？",new(MiaoAction.QueryTimetable,new DateOnly(2026,10,7),new DateOnly(2026,10,7)));
        Expect("周日有课吗？",new(MiaoAction.QueryTimetable,new DateOnly(2026,10,4),new DateOnly(2026,10,4)));
        Expect("本周课程",new(MiaoAction.QueryTimetable,new DateOnly(2026,9,28),new DateOnly(2026,10,4)));
        Expect("2026-10-2有什么课？",new(MiaoAction.QueryTimetable,new DateOnly(2026,10,2),new DateOnly(2026,10,2)));
        Expect("搜索文件：高等数学",new(MiaoAction.SearchFiles,SearchTerm:"高等数学"));
        Expect("搜索文件："+new string('字',81),null);
        Expect("明天有什么课和天气？",null);
        Expect("明天课表文件在哪里？",null);
        Expect("这节课停一次",new(MiaoAction.CancelSelected));
        Expect("撤销哲喵修改",new(MiaoAction.UndoChange));
        Expect("明天有什么课？请顺便取消",null);
        Expect("取消明天课程",null);
        Expect("今天和明天有什么课？",null);
        Expect("明天的课程如何准备？",null);
    }
}
