using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Zheli.Domain;

namespace Zheli.Platform;

public sealed record KnowledgeHit(string RootId,string RootPath,string RelativePath,string Location,string Text,string Hash)
{
    public string FullPath=>Path.GetFullPath(Path.Combine(RootPath,RelativePath));
}
public sealed record IndexIssue(string File,string Message);
public sealed record IndexReport(int Updated,int Reused,int Removed,bool Limited,List<IndexIssue> Issues);
public sealed record KnowledgeResults(List<KnowledgeHit> Hits,IndexReport Index);

// Only the Miao process opens this database. Settings owns folder grants, never this index.
public sealed class KnowledgeIndex
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly DocumentTextExtractor _extractor=new();
    public KnowledgeIndex(string path)
    {
        _path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var db=Open();using var version=db.CreateCommand();version.CommandText="PRAGMA user_version";
        if(Convert.ToInt32(version.ExecuteScalar())>1)throw new InvalidDataException("知识索引来自更新版本。");
        Exec(db,"""
            CREATE TABLE IF NOT EXISTS files(root TEXT NOT NULL,relative TEXT NOT NULL,binding TEXT NOT NULL,
              bytes INTEGER NOT NULL,ticks INTEGER NOT NULL,hash TEXT NOT NULL,notice TEXT,PRIMARY KEY(root,relative));
            CREATE TABLE IF NOT EXISTS chunks(root TEXT NOT NULL,relative TEXT NOT NULL,ordinal INTEGER NOT NULL,
              location TEXT NOT NULL,content TEXT NOT NULL,PRIMARY KEY(root,relative,ordinal),
              FOREIGN KEY(root,relative) REFERENCES files(root,relative) ON DELETE CASCADE);
            PRAGMA user_version=1;
            """);
    }
    private SqliteConnection Open()
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=_path,DefaultTimeout=10,Pooling=false}.ToString());db.Open();
        Exec(db,"PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA secure_delete=ON;");return db;
    }
    private static string Binding(KnowledgeFolder root)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root.Path)+":"+DocumentTextExtractor.Version)));
    public async Task<IndexReport> Refresh(IReadOnlyList<KnowledgeFolder> folders,bool verifyContents,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{return await Task.Run(()=>RefreshCore(folders,verifyContents,ct),ct);}finally{_gate.Release();}
    }
    private IndexReport RefreshCore(IReadOnlyList<KnowledgeFolder> folders,bool verifyContents,CancellationToken ct)
    {
        KnowledgePolicy.Validate(folders);var active=folders.Where(f=>f.Enabled).ToDictionary(f=>f.Id);
        using var db=Open();var updated=0;var reused=0;var removed=0;var scanned=0;var limited=false;
        var issues=new List<IndexIssue>();void Issue(string file,string reason){if(issues.Count<100)issues.Add(new(file,reason));}
        var old=new List<(string root,string relative,string binding)>();
        using(var q=db.CreateCommand())
        {q.CommandText="SELECT root,relative,binding FROM files";using var r=q.ExecuteReader();while(r.Read())old.Add((r.GetString(0),r.GetString(1),r.GetString(2)));}
        foreach(var f in old)
            if(!active.TryGetValue(f.root,out var root)||Binding(root)!=f.binding){Delete(db,f.root,f.relative);removed++;}
        var seen=new HashSet<(string,string)>();
        foreach(var root in active.Values)
        {
            ct.ThrowIfCancellationRequested();var binding=Binding(root);
            try
            {
                if(!KnowledgePolicy.CanRead(folders,root.Id,root.Path))continue;
                EnsureSafe(root.Path,root.Path);
                var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=false,AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.Hidden|FileAttributes.System,MaxRecursionDepth=20};
                foreach(var file in Directory.EnumerateFiles(root.Path,"*",options))
                {
                    ct.ThrowIfCancellationRequested();if(++scanned>5000){limited=true;break;}
                    var relative=Path.GetRelativePath(root.Path,file);
                    if(!KnowledgePolicy.CanRead(folders,root.Id,file))continue;
                    if(!DocumentTextExtractor.Extensions.Contains(Path.GetExtension(file)))continue;
                    seen.Add((root.Id,relative));
                    try
                    {
                        EnsureSafe(root.Path,file);var info=new FileInfo(file);var bytes=info.Length;var ticks=info.LastWriteTimeUtc.Ticks;
                        if(bytes>DocumentTextExtractor.MaximumFileBytes)throw new InvalidDataException("文件超过32MB，未索引。");
                        var cached=Cached(db,root.Id,relative);
                        if(!verifyContents&&cached!=null&&cached.Value.bytes==bytes&&cached.Value.ticks==ticks)
                        {reused++;if(cached.Value.notice!=null)Issue(relative,cached.Value.notice);continue;}
                        var contents=ReadBounded(root.Path,file,ct);var hash=Convert.ToHexString(SHA256.HashData(contents));
                        if(cached?.hash==hash)
                        {
                            Exec(db,"UPDATE files SET bytes=$b,ticks=$t WHERE root=$r AND relative=$p",("$b",bytes),("$t",ticks),("$r",root.Id),("$p",relative));
                            reused++;if(cached.Value.notice!=null)Issue(relative,cached.Value.notice);continue;
                        }
                        var document=_extractor.Extract(contents,Path.GetExtension(file),ct);
                        if(info.Length!=new FileInfo(file).Length||ticks!=File.GetLastWriteTimeUtc(file).Ticks)throw new IOException("提取期间文件变化，请重试。");
                        using(var size=db.CreateCommand())
                        {
                            size.CommandText="SELECT COALESCE(SUM(length(content)),0) FROM chunks";
                            if(Convert.ToInt64(size.ExecuteScalar())+document.Fragments.Sum(x=>x.Text.Length)>40_000_000)throw new InvalidDataException("索引达到4000万字符容量限制。");
                        }
                        using var tx=db.BeginTransaction();
                        Exec(db,"DELETE FROM files WHERE root=$r AND relative=$p",tx,("$r",root.Id),("$p",relative));
                        Exec(db,"INSERT INTO files VALUES($r,$p,$b,$s,$t,$h,$n)",tx,("$r",root.Id),("$p",relative),("$b",binding),("$s",bytes),("$t",ticks),("$h",hash),("$n",(object?)document.Notice??DBNull.Value));
                        var ordinal=0;
                        foreach(var fragment in document.Fragments)
                            Exec(db,"INSERT INTO chunks VALUES($r,$p,$o,$l,$c)",tx,("$r",root.Id),("$p",relative),("$o",ordinal++),("$l",fragment.Location),("$c",fragment.Text));
                        tx.Commit();updated++;if(document.Notice!=null)Issue(relative,document.Notice);
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception e) when(e is not (OutOfMemoryException or StackOverflowException))
                    {Delete(db,root.Id,relative);Issue(relative,e is InvalidDataException or UnauthorizedAccessException?e.Message:"无法读取：可能损坏、加密、编码不支持或文件正在变化。");}
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
            {Issue(root.Path,"文件夹无法完整读取；未确认可读的旧索引将移除。");}
        }
        // Also prune inaccessible or over-limit old files. Incomplete scans must not expose stale excerpts.
        foreach(var f in old.Where(f=>active.ContainsKey(f.root)&&!seen.Contains((f.root,f.relative)))){Delete(db,f.root,f.relative);removed++;}
        return new(updated,reused,removed,limited,issues);
    }
    public async Task<KnowledgeResults> Search(IReadOnlyList<KnowledgeFolder> folders,string query,CancellationToken ct)
    {
        query=query.Trim();if(query.Length is <1 or >80)throw new ArgumentException("请输入1—80个字符的关键词。");
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(()=>
            {
                var report=RefreshCore(folders,true,ct);var hits=new List<KnowledgeHit>();var paths=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
                using var db=Open();
                foreach(var root in folders.Where(f=>f.Enabled))
                {
                    using var cmd=db.CreateCommand();cmd.CommandText="""
                        SELECT c.relative,c.location,c.content,f.hash FROM chunks c JOIN files f ON f.root=c.root AND f.relative=c.relative
                        WHERE c.root=$r AND f.binding=$b AND instr(lower(c.content),lower($q))>0 ORDER BY c.relative,c.ordinal LIMIT 60
                        """;
                    cmd.Parameters.AddWithValue("$r",root.Id);cmd.Parameters.AddWithValue("$b",Binding(root));cmd.Parameters.AddWithValue("$q",query);
                    using var reader=cmd.ExecuteReader();
                    while(reader.Read()&&hits.Count<30)
                    {
                        ct.ThrowIfCancellationRequested();var relative=reader.GetString(0);var location=reader.GetString(1);var content=reader.GetString(2);
                        var start=content.IndexOf(query,StringComparison.OrdinalIgnoreCase);var offset=Math.Max(0,start-120);var excerpt=content.Substring(offset,Math.Min(600,content.Length-offset));
                        var unique=Path.GetFullPath(Path.Combine(root.Path,relative))+"\n"+location+"\n"+excerpt;
                        if(paths.Add(unique))hits.Add(new(root.Id,Path.GetFullPath(root.Path),relative,location,excerpt,reader.GetString(3)));
                    }
                }
                return new KnowledgeResults(hits,report);
            },ct);
        }
        finally{_gate.Release();}
    }
    public static async Task ValidateForCloud(IReadOnlyList<KnowledgeFolder> folders,IReadOnlyList<KnowledgeHit> hits,CancellationToken ct)
    {
        if(hits.Count is <1 or >6)throw new InvalidOperationException("每次发送1—6个明确选择的片段。");
        await Task.Run(()=>
        {
            foreach(var hit in hits)
            {
                ct.ThrowIfCancellationRequested();var root=folders.SingleOrDefault(f=>f.Id==hit.RootId);
                if(root==null||!Path.GetFullPath(root.Path).Equals(hit.RootPath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||!KnowledgePolicy.CanSend(folders,hit.RootId,hit.FullPath))
                    throw new UnauthorizedAccessException("片段所属文件夹未允许云端发送，或位于私密目录。");
                var hash=Convert.ToHexString(SHA256.HashData(ReadBounded(root.Path,hit.FullPath,ct)));
                if(hash!=hit.Hash)throw new InvalidOperationException("来源文件已变化，请重新搜索并选择片段。");
            }
        },ct);
    }
    public static void EnsureSafe(string root,string candidate)
    {
        root=Path.GetFullPath(root);candidate=Path.GetFullPath(candidate);
        if(!KnowledgePolicy.Contains(root,candidate))throw new UnauthorizedAccessException("文件不在授权目录内。");
        // Check every ancestor, including ancestors above the authorized root.
        var current=candidate;
        while(!string.IsNullOrEmpty(current))
        {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("不读取符号链接或联接目录。");
            current=Path.GetDirectoryName(current);
        }
    }
    public static byte[] ReadBounded(string root,string file,CancellationToken ct)
    {
        EnsureSafe(root,file);using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>DocumentTextExtractor.MaximumFileBytes)throw new InvalidDataException("文件超过32MB限制。");
        using var buffer=new MemoryStream();var block=new byte[65536];int read;
        while((read=stream.Read(block))>0){ct.ThrowIfCancellationRequested();if(buffer.Length+read>DocumentTextExtractor.MaximumFileBytes)throw new InvalidDataException("文件读取大小超限。");buffer.Write(block,0,read);}
        return buffer.ToArray();
    }
    private static (long bytes,long ticks,string hash,string? notice)? Cached(SqliteConnection db,string root,string path)
    {
        using var cmd=db.CreateCommand();cmd.CommandText="SELECT bytes,ticks,hash,notice FROM files WHERE root=$r AND relative=$p";
        cmd.Parameters.AddWithValue("$r",root);cmd.Parameters.AddWithValue("$p",path);using var r=cmd.ExecuteReader();
        return r.Read()?(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3)):null;
    }
    private static void Delete(SqliteConnection db,string root,string path)=>Exec(db,"DELETE FROM files WHERE root=$r AND relative=$p",("$r",root),("$p",path));
    private static void Exec(SqliteConnection db,string sql,params (string,object)[] values)=>Exec(db,sql,null,values);
    private static void Exec(SqliteConnection db,string sql,SqliteTransaction? tx,params (string,object)[] values)
    {using var cmd=db.CreateCommand();cmd.CommandText=sql;cmd.Transaction=tx;foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value);cmd.ExecuteNonQuery();}
}
