using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Zheli.Platform;

public sealed record ChatMessage(string Role,string Content);
public enum DeepSeekFailure { Authentication, Balance, RateLimit, InvalidRequest, ModelUnavailable, Service, Network, Timeout, InvalidResponse, Filtered }
public sealed class DeepSeekException(DeepSeekFailure failure,string message,HttpStatusCode? status=null) : Exception(message)
{
    public DeepSeekFailure Failure { get; }=failure;
    public HttpStatusCode? Status { get; }=status;
}
public sealed class DeepSeekClient : IDisposable
{
    private const int MaxResponseBytes=4*1024*1024;
    private readonly HttpClient _http;
    private readonly Func<string?> _credentials;
    public DeepSeekClient() : this(CredentialVault.Get) { }
    // Injection tests the production client without requiring a Windows credential vault.
    // The production endpoint is fixed and redirects are disabled.
    public DeepSeekClient(Func<string?> credentials,HttpMessageHandler? transport=null,TimeSpan? timeout=null)
    {
        _credentials=credentials??throw new ArgumentNullException(nameof(credentials));
        _http=new HttpClient(transport??new HttpClientHandler {AllowAutoRedirect=false})
        {BaseAddress=new Uri("https://api.deepseek.com/"),Timeout=timeout??TimeSpan.FromSeconds(90)};
    }
    private HttpRequestMessage Request(HttpMethod method,string path)
    {
        var key=_credentials()?.Trim();
        if(string.IsNullOrEmpty(key))throw new InvalidOperationException("请先在哲里设置中配置DeepSeek密钥。");
        if(key.Length>2048||key.Any(c=>c<=32||c>=127))throw new InvalidOperationException("密钥格式无效，请重新粘贴完整密钥。");
        var request=new HttpRequestMessage(method,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        return request;
    }
    public async Task<List<string>> Models(CancellationToken ct=default)
    {
        using var request=Request(HttpMethod.Get,"models");
        using var json=await Send(request,ct);
        if(!json.RootElement.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array)throw InvalidResponse();
        var models=data.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String)
            .Select(x=>x.GetProperty("id").GetString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if(models.Count==0)throw new DeepSeekException(DeepSeekFailure.ModelUnavailable,"连接已建立，但未取得可用模型。请稍后重新获取。");
        return models;
    }
    public async Task<string> Complete(string model,IReadOnlyList<ChatMessage> messages,CancellationToken ct,int maxTokens=2048)
    {
        if(string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("请先在哲里设置中获取并选择模型。");
        if(maxTokens is <1 or >8192)throw new ArgumentOutOfRangeException(nameof(maxTokens));
        if(messages.Count is <1 or >100||messages.Any(m=>m.Role is not ("system" or "user" or "assistant")||string.IsNullOrWhiteSpace(m.Content))||messages.Sum(m=>(long)m.Content.Length)>512000)
            throw new ArgumentException("对话内容为空、过长或角色无效，请缩短内容或新建会话。");
        using var request=Request(HttpMethod.Post,"chat/completions");
        request.Content=JsonContent.Create(new { model,messages=messages.Select(m=>new {role=m.Role,content=m.Content}),stream=false,max_tokens=maxTokens });
        using var doc=await Send(request,ct);
        if(!doc.RootElement.TryGetProperty("choices",out var choices)||choices.ValueKind!=JsonValueKind.Array||choices.GetArrayLength()==0)throw InvalidResponse();
        var first=choices[0];
        if(first.ValueKind!=JsonValueKind.Object)throw InvalidResponse();
        var reason=first.TryGetProperty("finish_reason",out var finish)&&finish.ValueKind==JsonValueKind.String?finish.GetString():null;
        if(reason=="content_filter")throw new DeepSeekException(DeepSeekFailure.Filtered,"服务未返回可显示的回答，请调整问题后重试。");
        if(!first.TryGetProperty("message",out var message)||message.ValueKind!=JsonValueKind.Object||!message.TryGetProperty("content",out var content)||content.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(content.GetString()))throw InvalidResponse();
        var answer=content.GetString()!;
        return reason=="length"?answer+"\n\n（回答达到本次输出上限，内容可能不完整。可缩小问题范围后继续提问。）":answer;
    }
    private async Task<JsonDocument> Send(HttpRequestMessage request,CancellationToken ct)
    {
        // One deadline covers both response headers and body.
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_http.Timeout);
        try
        {
            using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            Check(response);
            if(response.Content.Headers.ContentLength>MaxResponseBytes)throw InvalidResponse();
            using var body=await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await body.ReadAsync(buffer,deadline.Token))>0)
            {
                if(bytes.Length+count>MaxResponseBytes)throw InvalidResponse();
                bytes.Write(buffer,0,count);
            }
            var json=JsonDocument.Parse(bytes.ToArray());
            if(json.RootElement.ValueKind!=JsonValueKind.Object){json.Dispose();throw InvalidResponse();}
            return json;
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested)
        {throw new DeepSeekException(DeepSeekFailure.Timeout,"DeepSeek响应超时。可以稍后手动重试；不会自动重复发送。已发送的请求可能仍由服务处理并计费。");}
        catch(HttpRequestException)
        {throw new DeepSeekException(DeepSeekFailure.Network,"无法连接DeepSeek，请检查网络、代理或系统时间后重试。");}
        catch(IOException)
        {throw new DeepSeekException(DeepSeekFailure.Network,"读取DeepSeek响应时连接中断，请稍后手动重试。");}
        catch(JsonException){throw InvalidResponse();}
    }
    private static DeepSeekException InvalidResponse()=>new(DeepSeekFailure.InvalidResponse,"DeepSeek返回了空白、过大或无法解析的回答。请稍后重试。");
    private static void Check(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode)return;
        var (failure,message)=(int)response.StatusCode switch
        {
            401=>(DeepSeekFailure.Authentication,"密钥未通过验证，请在哲里设置中更新密钥。"),
            402=>(DeepSeekFailure.Balance,"DeepSeek账户余额不足，请检查账户余额后重试。"),
            429=>(DeepSeekFailure.RateLimit,"请求过于频繁，请稍后手动重试。"),
            400 or 422=>(DeepSeekFailure.InvalidRequest,"请求参数被服务拒绝，请缩短对话或重新选择模型。"),
            404=>(DeepSeekFailure.ModelUnavailable,"模型或接口不可用，请在哲里设置中重新获取模型。"),
            >=500=>(DeepSeekFailure.Service,"DeepSeek服务暂时不可用，请稍后手动重试。"),
            _=>(DeepSeekFailure.Service,"DeepSeek未完成请求，请检查设置后重试。")
        };
        // Never copy remote error bodies, headers or credentials into UI/logs.
        throw new DeepSeekException(failure,$"{message}（HTTP {(int)response.StatusCode}）",response.StatusCode);
    }
    public void Dispose()=>_http.Dispose();
}
