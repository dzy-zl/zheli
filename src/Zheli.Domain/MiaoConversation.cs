namespace Zheli.Domain;

// Shared by the small desktop panel and the full conversation window.
// Source is optional so conversations saved by older versions remain readable.
public sealed record Message(string Role,string Text,DateTimeOffset Time,bool LocalOnly=false);
public sealed record Conversation(string Id,string Title,List<Message> Messages,DateTimeOffset? DeletedAt=null,string Source="main");
public sealed record MiaoState
{
    public List<Conversation> Conversations { get; init; }=[];
}
