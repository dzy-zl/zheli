namespace Zheli.Domain;

public static class KnowledgePolicy
{
    public static void Validate(IReadOnlyList<KnowledgeFolder> folders)
    {
        if(folders.Count>24)throw new DomainException("最多授权24个文件夹。");
        if(folders.Select(f=>f.Id).Distinct().Count()!=folders.Count)throw new DomainException("文件夹编号重复。");
        foreach(var folder in folders)
        {
            if(!Guid.TryParse(folder.Id,out _)||!System.IO.Path.IsPathFullyQualified(folder.Path))throw new DomainException("文件夹路径或编号无效。");
            if(folder.Private&&folder.AllowCloud)throw new DomainException("私密文件夹不能允许云端发送。");
        }
    }
    public static bool Contains(string root,string candidate)
    {
        var relative=System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(root),System.IO.Path.GetFullPath(candidate));
        return relative=="." || (!System.IO.Path.IsPathRooted(relative)&&relative!=".."&&!relative.StartsWith(".."+System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal));
    }
    public static bool CanSend(IReadOnlyList<KnowledgeFolder> folders,string rootId,string file)
    {
        var root=folders.SingleOrDefault(f=>f.Id==rootId);
        if(root==null||!root.AllowCloud||root.Private||!CanRead(folders,rootId,file))return false;
        // A private child folder stays private even when a parent folder allows cloud excerpts.
        return !folders.Any(f=>f.Private&&Contains(f.Path,file));
    }
    public static bool CanRead(IReadOnlyList<KnowledgeFolder> folders,string rootId,string file)
    {
        var root=folders.SingleOrDefault(f=>f.Id==rootId);
        return root!=null&&root.Enabled&&Contains(root.Path,file)&&!folders.Any(f=>!f.Enabled&&Contains(f.Path,file));
    }
}
