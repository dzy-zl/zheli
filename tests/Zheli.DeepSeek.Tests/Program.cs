using System.Net;
using System.Text;
using System.Text.Json;
using Zheli.Platform;

if(args.Contains("--live"))
{
    // Explicit opt-in only. Redirected stdin is supported for a transient CI secret.
    // Never accept secrets in command arguments, files or result logs.
    string? key=null;
    if(args.Contains("--stdin-key")){Console.Error.WriteLine("Waiting for transient key on stdin (not echoed).");key=Console.ReadLine();}
    Func<string?> credentials=args.Contains("--stdin-key")?()=>key:CredentialVault.Get;
    try
    {
        using var client=new DeepSeekClient(credentials);
        var models=await client.Models();Console.WriteLine("PASS live model discovery; count="+models.Count);
        var model=models.FirstOrDefault(m=>m=="deepseek-chat")??models.First();
        Console.WriteLine("Selected model: "+model);
        var answer=await client.Complete(model,[new("system","你是哲喵。只依据合成测试资料回答，不执行任何操作。"),new("user","合成测试：明天第一节课是高等数学，教室A301。请只回答课程名称和教室。")],CancellationToken.None,128);
        if(!answer.Contains("高等数学")||!answer.Contains("A301"))throw new InvalidDataException();
        Console.WriteLine("PASS live synthetic timetable question (answer matched; body omitted)");
        answer=await client.Complete(model,[new("system","你是哲喵。引用给出的合成资料时标注[文件1]。"),new("user","[文件1] test.md · 第1行：图书馆闭馆时间是21:30。请回答闭馆时间并标注来源。")],CancellationToken.None,128);
        if(!answer.Contains("21:30")||!answer.Contains("[文件1]"))throw new InvalidDataException();
        Console.WriteLine("PASS live synthetic file citation (answer matched; body omitted)");
        Console.WriteLine("RESULT: live checks passed; no personal files or real timetable read");
    }
    catch(DeepSeekException e){Console.WriteLine($"FAIL live: {e.Failure}; HTTP {(int?)e.Status}");Environment.ExitCode=1;}
    catch(Exception e){Console.WriteLine("FAIL live: "+e.GetType().Name);Environment.ExitCode=1;}
    finally{key=null;}
    return;
}

var passed=0;var failed=0;
async Task Test(string name,Func<Task> action)
{
    try{await action();Console.WriteLine("PASS "+name);passed++;}
    catch(Exception e){Console.WriteLine("FAIL "+name+" :: "+e.GetType().Name);failed++;}
}
void Check(bool condition){if(!condition)throw new InvalidDataException();}
async Task Expect(DeepSeekFailure failure,Func<Task> action)
{
    try{await action();}catch(DeepSeekException e){Check(e.Failure==failure);Check(!e.Message.Contains("remote-secret"));return;}
    throw new InvalidDataException();
}
HttpResponseMessage Response(string json,int status=200)=>new((HttpStatusCode)status){Content=new StringContent(json,Encoding.UTF8,"application/json")};
DeepSeekClient Client(string json,int status=200)=>new(()=>"synthetic-test-token",new FakeHandler((_,_)=>Task.FromResult(Response(json,status))));
Task<string> Chat(DeepSeekClient client)=>client.Complete("test-model",[new("user","你好")],CancellationToken.None);
const string valid="{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"你好\"}}]}";
await Test("request endpoint, authorization and bounded output",async()=>
{
    using var client=new DeepSeekClient(()=>" synthetic-test-token ",new FakeHandler(async(r,ct)=>
    {
        Check(r.RequestUri!.AbsoluteUri=="https://api.deepseek.com/chat/completions");
        Check(r.Headers.Authorization?.Parameter=="synthetic-test-token");
        using var json=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));
        Check(json.RootElement.GetProperty("max_tokens").GetInt32()==2048);
        Check(!json.RootElement.GetProperty("stream").GetBoolean());
        return Response(valid);
    }));Check(await Chat(client)=="你好");
});
await Test("model list filters and deduplicates",async()=>{using var c=Client("{\"data\":[{\"id\":\"a\"},{\"id\":\"a\"},{\"id\":null},{}]}");Check((await c.Models()).SequenceEqual(new[]{"a"}));});
await Test("empty model list",async()=>{using var c=Client("{\"data\":[]}");await Expect(DeepSeekFailure.ModelUnavailable,async()=>await c.Models());});
foreach(var (status,failure) in new[]{(401,DeepSeekFailure.Authentication),(402,DeepSeekFailure.Balance),(429,DeepSeekFailure.RateLimit),(400,DeepSeekFailure.InvalidRequest),(422,DeepSeekFailure.InvalidRequest),(404,DeepSeekFailure.ModelUnavailable),(500,DeepSeekFailure.Service),(503,DeepSeekFailure.Service),(302,DeepSeekFailure.Service)})
    await Test("HTTP "+status+" safely classified",async()=>{using var c=Client("remote-secret",status);await Expect(failure,async()=>await Chat(c));});
foreach(var malformed in new[]{"not-json","[]","{}","{\"choices\":[]}","{\"choices\":[null]}","{\"choices\":[{\"message\":{\"content\":null}}]}"})
    await Test("malformed or empty response rejected",async()=>{using var c=Client(malformed);await Expect(DeepSeekFailure.InvalidResponse,async()=>await Chat(c));});
await Test("output truncation disclosed",async()=>{using var c=Client(valid.Replace("stop","length"));Check((await Chat(c)).Contains("可能不完整"));});
await Test("filtered response classified",async()=>{using var c=Client(valid.Replace("stop","content_filter"));await Expect(DeepSeekFailure.Filtered,async()=>await Chat(c));});
await Test("oversized response rejected",async()=>{using var c=Client(new string('x',4*1024*1024+1));await Expect(DeepSeekFailure.InvalidResponse,async()=>await Chat(c));});
await Test("connection details never disclosed",async()=>{using var c=new DeepSeekClient(()=>"synthetic",new FakeHandler((_,_)=>throw new HttpRequestException("remote-secret")));await Expect(DeepSeekFailure.Network,async()=>await Chat(c));});
await Test("timeout distinguished from cancellation",async()=>{using var c=new DeepSeekClient(()=>"synthetic",new FakeHandler(async(_,ct)=>{await Task.Delay(10000,ct);return Response(valid);}),TimeSpan.FromMilliseconds(30));await Expect(DeepSeekFailure.Timeout,async()=>await Chat(c));});
await Test("deadline includes stalled response body",async()=>
{
    using var c=new DeepSeekClient(()=>"synthetic",new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())})),TimeSpan.FromMilliseconds(30));
    await Expect(DeepSeekFailure.Timeout,async()=>await Chat(c));
});
await Test("user cancellation preserved",async()=>
{
    using var c=new DeepSeekClient(()=>"synthetic",new FakeHandler(async(_,ct)=>{await Task.Delay(10000,ct);return Response(valid);}));
    using var cancel=new CancellationTokenSource(30);
    try{await c.Models(cancel.Token);}catch(OperationCanceledException){return;}throw new InvalidDataException();
});
await Test("invalid credential rejected before dispatch",async()=>
{
    using var c=new DeepSeekClient(()=>"bad\r\nheader",new FakeHandler((_,_)=>throw new InvalidDataException()));
    try{await c.Models();}catch(InvalidOperationException){return;}throw new InvalidDataException();
});
await Test("invalid role rejected before dispatch",async()=>
{
    using var c=Client(valid);try{await c.Complete("model",[new("tool","x")],CancellationToken.None);}catch(ArgumentException){return;}throw new InvalidDataException();
});
await Test("failure makes one request without retry",async()=>
{
    int calls=0;using var c=new DeepSeekClient(()=>"synthetic",new FakeHandler((_,_)=>{calls++;return Task.FromResult(Response("remote-secret",503));}));
    await Expect(DeepSeekFailure.Service,async()=>await Chat(c));Check(calls==1);
});
Console.WriteLine($"RESULT: {passed} passed, {failed} failed; offline HTTP transport tests");
Environment.ExitCode=failed==0?0:1;

sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);
}

sealed class StalledStream:Stream
{
    public override bool CanRead=>true;
    public override bool CanSeek=>false;
    public override bool CanWrite=>false;
    public override long Length=>throw new NotSupportedException();
    public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){await Task.Delay(10000,cancellationToken);return 0;}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
