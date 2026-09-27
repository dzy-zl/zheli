using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zheli.Storage;

namespace Zheli.Platform;

public enum FileActionKind { Rename, Move, Delete, Edit }
public enum FileActionStatus { Prepared, Complete, Undone, Expired, NeedsReview }
public sealed record FileAction(string Id,FileActionKind Kind,string Source,string? Target,string Backup,string Sha256,
    DateTimeOffset Created,DateTimeOffset Expires,FileActionStatus Status,string? RestoredTo=null,string? ResultSha256=null);
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
    public Task<FileAction> Execute(FileActionKind kind,string source,string? target,CancellationToken ct=default)
    {
        if(kind is not (FileActionKind.Rename or FileActionKind.Move or FileActionKind.Delete))throw new ArgumentException("请使用受支持的文件操作入口。");
        return ExecuteCore(kind,source,target,null,null,ct);
    }
    public (string Text,string Sha256) ReadEditableText(string source)
    {
        source=FilePath(source);
        if(Path.GetExtension(source).ToLowerInvariant() is not (".txt" or ".md"))throw new IOException("仅支持 UTF-8 的 TXT 和 Markdown 文件。");
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("暂不处理符号链接或重解析点。");
        if(new FileInfo(source).Length>512*1024)throw new IOException("文本文件超过 512 KiB 编辑限制。");
        var bytes=File.ReadAllBytes(source);
        if(bytes.Length>512*1024)throw new IOException("文本文件超过 512 KiB 编辑限制。");
        var bom=bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        var content=new UTF8Encoding(false,true).GetString(bytes,bom?3:0,bytes.Length-(bom?3:0));
        if(content.Contains('\0'))throw new IOException("文本包含空字符，无法作为 UTF-8 文本编辑。");
        return(content,Convert.ToHexString(SHA256.HashData(bytes)));
    }
    public Task<FileAction> EditText(string source,string expectedSha256,string replacement,CancellationToken ct=default)
    {
        var old=ReadEditableText(source);
        if(old.Sha256!=expectedSha256)throw new IOException("文件已被其他程序修改；请重新打开后再编辑。");
        if(replacement.Contains('\0'))throw new IOException("编辑内容不能包含空字符。");
        if(replacement.Length>512*1024)throw new IOException("编辑后的文本超过 512 KiB 限制。");
        var bom=File.ReadAllBytes(source).AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        var content=new UTF8Encoding(false,true).GetBytes(replacement);
        if(content.Length>512*1024)throw new IOException("编辑后的文本超过 512 KiB 限制。");
        var bytes=bom?Encoding.UTF8.GetPreamble().Concat(content).ToArray():content;
        return ExecuteCore(FileActionKind.Edit,source,null,bytes,expectedSha256,ct);
    }
    private async Task<FileAction> ExecuteCore(FileActionKind kind,string source,string? target,byte[]? replacement,string? expectedSha,CancellationToken ct)
    {
        source=FilePath(source);
        if((kind is FileActionKind.Delete or FileActionKind.Edit)&&target!=null)throw new ArgumentException("该操作不需要目标路径。");
        if(kind is FileActionKind.Rename or FileActionKind.Move)
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
            if(expectedSha!=null&&hash!=expectedSha)throw new IOException("文件已被其他程序修改；请重新打开后再编辑。");
        }
        catch
        {
            if(File.Exists(temp))File.Delete(temp);
            if(File.Exists(backup))File.Delete(backup);
            Directory.Delete(directory);
            throw;
        }
        var now=DateTimeOffset.UtcNow;
        var prepared=new FileAction(id,kind,source,target,backup,hash,now,now.AddDays(30),FileActionStatus.Prepared,
            ResultSha256:replacement==null?null:Convert.ToHexString(SHA256.HashData(replacement)));
        try{Update(s=>s with{Actions=s.Actions.Prepend(prepared).ToList()},"准备文件操作");}
        catch{Directory.Delete(directory,true);throw;}
        try
        {
            ct.ThrowIfCancellationRequested();
            if(kind==FileActionKind.Delete)File.Delete(source);
            else if(kind==FileActionKind.Edit)
            {
                var staged=Path.Combine(Path.GetDirectoryName(source)!,"."+Path.GetFileName(source)+".zheli-"+id+".part");
                try
                {
                    await File.WriteAllBytesAsync(staged,replacement!,ct);
                    if(await Hash(source,ct)!=hash)throw new IOException("编辑前原文件发生变化；未替换内容。");
                    File.Replace(staged,source,null);
                }
                finally{if(File.Exists(staged))File.Delete(staged);}
            }
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
        if(action.Kind==FileActionKind.Edit)
        {
            if(File.Exists(action.Source)&&action.ResultSha256!=null&&await Hash(action.Source,ct)==action.ResultSha256)
            {
                var staged=await StageBackup(action,action.Source,ct);
                try{File.Replace(staged,action.Source,null);}
                finally{if(File.Exists(staged))File.Delete(staged);}
                return UpdateOne(id,a=>a with{Status=FileActionStatus.Undone,RestoredTo=action.Source},"撤销文本编辑");
            }
            var alternative=UniqueRestorePath(action.Source);
            await RestoreBackup(action,alternative,ct);
            return UpdateOne(id,a=>a with{Status=FileActionStatus.Undone,RestoredTo=alternative},"恢复编辑前版本");
        }
        var restored=UniqueRestorePath(action.Source);
        var canMoveTarget=action.Kind!=FileActionKind.Delete&&action.Target!=null&&File.Exists(action.Target)
            &&restored==action.Source&&await Hash(action.Target,ct)==action.Sha256;
        ct.ThrowIfCancellationRequested();
        if(canMoveTarget)File.Move(action.Target!,restored);
        else await RestoreBackup(action,restored,ct);
        // When the target has changed, leave it untouched and restore the old bytes separately.
        return UpdateOne(id,a=>a with{Status=FileActionStatus.Undone,RestoredTo=restored},"撤销文件操作");
    }
    // A crash can leave a prepared or uncertain action. Restore the verified
    // recovery bytes alongside existing files, without guessing which file is current.
    public async Task<FileAction> RestoreCopy(string id,CancellationToken ct=default)
    {
        var action=_store.Read().Data.Actions.SingleOrDefault(x=>x.Id==id)??throw new FileNotFoundException("操作记录不存在。");
        if(action.Status is not (FileActionStatus.Prepared or FileActionStatus.NeedsReview)||DateTimeOffset.UtcNow>=action.Expires)
            throw new InvalidOperationException("这条操作已无法恢复副本。");
        if(!File.Exists(action.Backup)||await Hash(action.Backup,ct)!=action.Sha256)
            throw new IOException("恢复副本缺失或校验失败；未修改当前文件。");
        var restored=UniqueRestorePath(action.Source);
        ct.ThrowIfCancellationRequested();
        await RestoreBackup(action,restored,ct);
        return UpdateOne(id,a=>a with{Status=FileActionStatus.Undone,RestoredTo=restored},"从中断操作恢复副本");
    }
    public int Expire(DateTimeOffset now)
    {
        var expired=_store.Read().Data.Actions.Where(a=>a.Expires<=now&&a.Status!=FileActionStatus.Expired).ToList();
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
    private static async Task<string> StageBackup(FileAction action,string destination,CancellationToken ct)
    {
        // Stage next to the destination; a partial copy is never published as
        // the restored file. The recovery original remains available on failure.
        var temp=Path.Combine(Path.GetDirectoryName(destination)!,"."+Path.GetFileName(destination)+".zheli-"+Guid.NewGuid().ToString("N")+".part");
        try
        {
            if(await CopyAndHash(action.Backup,temp,ct)!=action.Sha256)throw new IOException("恢复副本在复制时发生变化。");
            return temp;
        }
        catch{if(File.Exists(temp))File.Delete(temp);throw;}
    }
    private static async Task RestoreBackup(FileAction action,string destination,CancellationToken ct)
    {
        var temp=await StageBackup(action,destination,ct);
        try{File.Move(temp,destination);}
        finally{if(File.Exists(temp))File.Delete(temp);}
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
