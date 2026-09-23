namespace Zheli.Miao;
public sealed record Message(string Role,string Text,DateTimeOffset Time,bool LocalOnly=false);
public sealed record Conversation(string Id,string Title,List<Message> Messages,DateTimeOffset? DeletedAt=null);
public sealed record MiaoState
{
    public List<Conversation> Conversations{get;init;}=[];
}
