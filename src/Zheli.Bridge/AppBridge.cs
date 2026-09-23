using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Zheli.Contracts;

namespace Zheli.Bridge;

public static class AppPaths
{
    // Presence only: never acquire ownership. Installers check whether any app holds a handle.
    public static Mutex HoldRuntimePresence() => new(false, @"Local\Zheli.Applications.Running");
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zheli");
    public static string InstallRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static string Executable(string app) => Path.Combine(InstallRoot, app, app + ".exe");
    public static string Pipe(string service)
    {
        var user = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User!.Value : Environment.UserName;
        return "zheli-v1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16] + "-" + service;
    }
    public static void Launch(string app, params string[] arguments)
    {
        var path = Executable(app);
        if (!File.Exists(path)) throw new FileNotFoundException($"未找到 {app}。请使用完整构建目录或先安装对应应用。");
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Process.Start(info);
    }
}
public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public sealed class BridgeClient(string service, string host)
{
    public async Task<T> Call<T>(string method, object? args = null, string? requestId = null, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var pipe = new NamedPipeClientStream(".", AppPaths.Pipe(service), PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(350, deadline.Token); }
        catch (TimeoutException)
        {
            // Only connection establishment is retried. Never automatically replay an uncertain write.
            AppPaths.Launch(host);
            await pipe.ConnectAsync(8000, deadline.Token);
        }
        PeerIdentity.VerifyServer(pipe, host);
        var request = Protocol.Make(method,args,requestId);
        await Wire.Write(pipe, request, deadline.Token);
        var response = await Wire.Read<Response>(pipe, deadline.Token);
        if (response.Id != request.Id) throw new InvalidDataException("响应编号不匹配。");
        if (!response.Ok) throw new BridgeException(response.Code ?? "ERROR", response.Error ?? "调用失败。");
        return response.Result!.Value.Deserialize<T>(Protocol.Json)!;
    }
}
public static class Wire
{
    public static async Task Write<T>(Stream stream, T data, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data,Protocol.Json);
        if (bytes.Length > Protocol.MaxMessageBytes) throw new InvalidDataException("消息过大。");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length),ct);
        await stream.WriteAsync(bytes,ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header,ct);
        var length = BitConverter.ToInt32(header);
        if (length < 1 || length > Protocol.MaxMessageBytes) throw new InvalidDataException("消息长度无效。");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes,ct);
        return JsonSerializer.Deserialize<T>(bytes,Protocol.Json) ?? throw new InvalidDataException("消息为空。");
    }
}
public static class BridgeServer
{
    public static async Task Run(string service, Func<string,Request,Task<Response>> handler, CancellationToken ct)
    {
        // One server loop serializes operations. A per-user singleton lock prevents competing owners.
        using var singleton = new Mutex(true, AppPaths.Pipe(service) + "-lock", out var created);
        if (!created) return;
        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(AppPaths.Pipe(service), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var caller = PeerIdentity.Client(pipe);
                var request = await Wire.Read<Request>(pipe,timeout.Token);
                var response = request.Protocol != 1 ? Protocol.Failure(request,"PROTOCOL","接口版本不兼容。") : await handler(caller,request);
                await Wire.Write(pipe,response,timeout.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
            { /* untrusted/malformed connection; do not echo paths or request content */ }
        }
    }
}
public static class PeerIdentity
{
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
    public static string Client(NamedPipeServerStream pipe)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var pid))
            throw new UnauthorizedAccessException();
        return Identify(pid);
    }
    public static void VerifyServer(NamedPipeClientStream pipe, string expected)
    {
        if (!OperatingSystem.IsWindows() || !GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var pid) || Identify(pid) != expected)
            throw new UnauthorizedAccessException("服务来源验证失败。");
    }
    private static string Identify(uint pid)
    {
        using var process = Process.GetProcessById((int)pid);
        var path = process.MainModule?.FileName ?? throw new UnauthorizedAccessException();
        var app = Path.GetFileNameWithoutExtension(path);
        string[] allowed = ["Zheli.Settings","Zheli.Timetable","Zheli.Miao","Zheli.PetHost","Zheli.CoreHost","Zheli.Timetable.Host"];
        if (!allowed.Contains(app) || !Path.GetFullPath(path).Equals(AppPaths.Executable(app),StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("调用方未注册。");
        return app;
    }
}
