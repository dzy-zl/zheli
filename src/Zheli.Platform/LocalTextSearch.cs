namespace Zheli.Platform;

public sealed record TextHit(string RelativePath,int Line,string Text);
public sealed class LocalTextSearch
{
    public Task<List<TextHit>> Search(string authorizedRoot,string query,CancellationToken ct)=>Task.Run(()=>
    {
        if(string.IsNullOrWhiteSpace(query))throw new ArgumentException("关键词不能为空。");
        var root=Path.GetFullPath(authorizedRoot);
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("不允许把链接目录作为授权根目录。");
        // Explicit user-selected folder only; never follow junctions/symlinks or hidden/system entries.
        var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint|FileAttributes.Hidden|FileAttributes.System,MaxRecursionDepth=20};
        var results=new List<TextHit>();var scanned=0;
        foreach(var path in Directory.EnumerateFiles(root,"*",options))
        {
            ct.ThrowIfCancellationRequested();if(++scanned>5000||results.Count>=30)break;
            if(Path.GetExtension(path).ToLowerInvariant() is not (".txt" or ".md"))continue;
            var info=new FileInfo(path);if(info.Length>2*1024*1024)continue;
            // Reject any reparse-point ancestor again just before opening.
            var relative=Path.GetRelativePath(root,path);var current=root;var safe=true;
            foreach(var part in relative.Split(Path.DirectorySeparatorChar))
            {current=Path.Combine(current,part);if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0){safe=false;break;}}
            if(!safe)continue;
            try
            {
                var number=0;
                foreach(var line in File.ReadLines(path))
                {
                    ct.ThrowIfCancellationRequested();number++;
                    if(line.Contains(query,StringComparison.OrdinalIgnoreCase))
                    {results.Add(new(relative,number,line[..Math.Min(500,line.Length)]));if(results.Count>=30)break;}
                }
            }
            catch(IOException){ }catch(UnauthorizedAccessException){ }
        }
        return results;
    },ct);
}
