using System.Text.Json;
namespace Zheli.Storage;

public sealed class DraftStore<T>(string path) where T:class
{
    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(value,DocumentStore<object>.Json));
        File.Move(temp,path,true);
    }
    public T? Load()=>File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),DocumentStore<object>.Json):null;
    public void Clear(){if(File.Exists(path))File.Delete(path);}
}
