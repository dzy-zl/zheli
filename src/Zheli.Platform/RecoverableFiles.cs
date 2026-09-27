using System.Security.Cryptography;
using System.Text.Json;
using Zheli.Storage;

namespace Zheli.Platform;

public enum FileActionKind { Rename, Move, Delete }
public enum FileActionStatus { Prepared, Complete, Undone, Expired, NeedsReview }
public sealed record FileAction(string Id,FileActionKind Kind,string Source,string? Target,string Backup,string Sha256,
    DateTimeOffset Created,DateTimeOffset Expires,FileActionStatus Status,string? RestoredTo=null);
public sealed record FileActionState
{
    public List<FileAction> Actions { get; init; }=[];
}

// Copies recovery bytes BEFORE changing the original file. A prepared record is
// retained if execution is interrupted; no uncertain operation is auto-replayed.
public sealed class RecoverableFiles
{
    private readonly string _root;
    private readonly DocumentStore<FileActionState> _store;
    public RecoverableFiles(string root)
    {
        _root=Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _store=new DocumentStore<FileActionState>(Path.Combine(_root,"file-actions.db"));
    }
    public IReadOnlyList<FileAction> History()=>_store.Read().Data.Actions.OrderByDescending(x=>x.Created).ToList();
    public async Task<FileAction> Execute(FileActionKind kind,string source,string? target,CancellationToken ct=default)
    {
        source=FilePath(source);
        if(kind==FileActionKind.Delete && target!=null)throw new ArgumentException("删除文件不需要目标路径。");
        if(kind!=FileActionKind.Delete)
        {
            if(string.IsNullOrWhiteSpace(target))throw new ArgumentException("缺少目标路径。");
            target=FilePath(target);
            if(string.Equals(source,target,StringComparison.OrdinalIgnoreCase))throw new IOException("目标路径与原文件相同。");
            if(!Directory.Exists(Path.GetDirectoryName(target)))throw new DirectoryNotFoundException("目标文件夹不存在。");
            if(File.Exists(target)||Directory.Exists(target))throw new IOException("目标位置已有同名文件；未执行操作。");
        }
        if(!File.Exists(source))throw new FileNotFoundException("找不到原文件。",source);
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("暂不处理符号链接或重解析点。");
        var initial=new FileInfo(source);
        if(initial.Length>new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace-16*1024*1024)
            throw new IOException("恢复副本所需磁盘空间不足；未执行操作。");
        var id=Guid.NewGuid().ToString("N");
        var directory=Path.Combine(_root,id);Directory.CreateDirectory(directory);
        var backup=Path.Combine(directory,"original.bin");var temp=backup+".part";
        string hash;
        try
        {
            hash=await CopyAndHash(source,temp,ct);
            File.Move(temp,backup);
            // The source could have changed after the copy lock was released.
            var current=new FileInfo(source);
            if(current.Length!=initial.Length||current.LastWriteTimeUtc!=initial.LastWriteTimeUtc||await Hash(source,ct)!=hash)
                throw new IOException("备份时原文件发生变化；未执行操作。");
        }
        catch
        {
            if(File.Exists(temp))File.Delete(temp);
            if(File.Exists(backup))File.Delete(backup);
            Directory.Delete(directory);
            throw;
        }
        var now=DateTimeOffset.UtcNow;
        var prepared=new FileAction(id,kind,source,target,backup,hash,now,now.AddDays(30),FileActionStatus.Prepared);
        try{Update(s=>s with{Actions=s.Actions.Prepend(prepared).ToList()},"准备文件操作");}
        catch{Directory.Delete(directory,true);throw;}
        try
        {
            ct.ThrowIfCancellationRequested();
            if(kind==FileActionKind.Delete)File.Delete(source);
            else File.Move(source,target!);
            return UpdateOne(id,a=>a with{Status=FileActionStatus.Complete},"完成文件操作");
        }
        catch
        {
            // The file may already have moved when an I/O error is reported.
            // Keep the recovery bytes and require manual review of this entry.
            UpdateOne(id,a=>a with{Status=FileActionStatus.NeedsReview},"文件操作状态需检查");
            throw;
        }
    }
    public async Task<FileAction> Undo(string id,CancellationToken ct=default)
    {
        var action=_store.Read().Data.Actions.SingleOrDefault(x=>x.Id==id)??throw new FileNotFoundException("操作记录不存在。");
        if(action.Status!=FileActionStatus.Complete||DateTimeOffset.UtcNow>=action.Expires)throw new InvalidOperationException("这条操作已无法自动撤销。");
        if(!File.Exists(action.Backup)||await Hash(action.Backup,ct)!=action.Sha256)
            throw new IOException("恢复副本缺失或校验失败；未修改当前文件。");
        var restored=UniqueRestorePath(action.Source);
        var canMoveTarget=action.Kind!=FileActionKind.Delete&&action.Target!=null&&File.Exists(action.Target)
            &&restored==action.Source&&await Hash(action.Target,ct)==action.Sha256;
        ct.ThrowIfCancellationRequested();
        if(canMoveTarget)File.Move(action.Target!,restored);
        else File.Copy(action.Backup,restored,false);
        // When the target has changed, leave it untouched and restore the old bytes separately.
        return UpdateOne(id,a=>a with{Status=FileActionStatus.Undone,RestoredTo=restored},"撤销文件操作");
    }
    public int Expire(DateTimeOffset now)
    {
        var expired=_store.Read().Data.Actions.Where(a=>a.Expires<=now&&a.Status is FileActionStatus.Complete or FileActionStatus.Undone).ToList();
        foreach(var action in expired)
        {
            // Expiration only removes our copy. The user's files remain in place.
            if(File.Exists(action.Backup))File.Delete(action.Backup);
            var dir=Path.GetDirectoryName(action.Backup)!;
            if(Directory.Exists(dir))Directory.Delete(dir);
            UpdateOne(action.Id,a=>a with{Status=FileActionStatus.Expired},"恢复期限届满");
        }
        return expired.Count;
    }
    private string FilePath(string path)
    {
        var full=Path.GetFullPath(path);
        var prefix=_root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||string.Equals(full,_root,StringComparison.OrdinalIgnoreCase))
            throw new IOException("恢复数据目录不能作为操作对象。");
        return full;
    }
    private static string UniqueRestorePath(string source)
    {
        if(!File.Exists(source)&&!Directory.Exists(source))return source;
        var folder=Path.GetDirectoryName(source)!;var name=Path.GetFileNameWithoutExtension(source);var extension=Path.GetExtension(source);
        for(var i=1;i<10000;i++)
        {
            var path=Path.Combine(folder,$"{name}_恢复_{i:D2}{extension}");
            if(!File.Exists(path)&&!Directory.Exists(path))return path;
        }
        throw new IOException("没有可用的恢复文件名。");
    }
    private FileAction UpdateOne(string id,Func<FileAction,FileAction> change,string summary)
    {
        FileAction? updated=null;
        Update(s=>s with{Actions=s.Actions.Select(a=>a.Id==id?updated=change(a):a).ToList()},summary);
        return updated??throw new FileNotFoundException("操作记录不存在。");
    }
    private void Update(Func<FileActionState,FileActionState> change,string summary)
    {
        var snapshot=_store.Read();var next=change(snapshot.Data);
        _store.Write(Guid.NewGuid().ToString(),snapshot.Revision,summary,_=>next,DocumentStore<FileActionState>.Hash(JsonSerializer.Serialize(next)));
    }
    private static async Task<string> CopyAndHash(string source,string destination,CancellationToken ct)
    {
        using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
        var buffer=new byte[81920];int count;
        while((count=await input.ReadAsync(buffer,ct))>0)
        {
            await output.WriteAsync(buffer.AsMemory(0,count),ct);sha.AppendData(buffer,0,count);
        }
        await output.FlushAsync(ct);output.Flush(true);
        return Convert.ToHexString(sha.GetHashAndReset());
    }
    private static async Task<string> Hash(string path,CancellationToken ct)
    {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.Asynchronous|FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));
    }
}
