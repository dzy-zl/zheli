namespace Zheli.Domain;

public sealed record PreparedTimetableChange(string Token,string Owner,long Revision,
    TimetableState State,DateTimeOffset Expires,string Summary);

public sealed class PreparedChanges
{
    private readonly Dictionary<string,PreparedTimetableChange> _pending=[];
    private readonly Func<DateTimeOffset> _clock;
    public PreparedChanges(Func<DateTimeOffset>? clock=null)=>_clock=clock??(()=>DateTimeOffset.UtcNow);
    public PreparedTimetableChange Add(string owner,long revision,TimetableState state,string summary)
    {
        foreach(var token in _pending.Where(p=>p.Value.Expires<=_clock()).Select(p=>p.Key).ToList())_pending.Remove(token);
        if(_pending.Count>=100)throw new DomainException("待确认操作过多，请稍后重试。");
        var plan=new PreparedTimetableChange(Guid.NewGuid().ToString("N"),owner,revision,state,_clock().AddMinutes(3),summary);
        _pending.Add(plan.Token,plan);return plan;
    }
    public PreparedTimetableChange Get(string token,string owner)
    {
        if(!_pending.TryGetValue(token,out var plan)||plan.Owner!=owner)throw new DomainException("确认方案不存在或不属于此应用。");
        if(plan.Expires<=_clock())throw new DomainException("确认方案已过期，请重新检查课程。");
        return plan;
    }
}
