using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Zheli.Storage;

public sealed record BackupHeader(string App,int Format,string Salt,string Nonce,int Iterations,DateTimeOffset Created);
public static class PortableBackup
{
    private static readonly byte[] Magic="ZHELIBK1"u8.ToArray();
    public const int MaximumBytes=64*1024*1024;
    public static void Export<T>(DocumentStore<T> store,string app,string path,string password,string stagingDirectory) where T:new()
    {
        if(password.Length<10)throw new ArgumentException("备份密码至少10个字符。");
        if(File.Exists(path))throw new IOException("目标文件已存在，请使用新文件名。");
        var snapshot=store.Backup(stagingDirectory);
        try
        {
            if(new FileInfo(snapshot).Length>MaximumBytes)throw new InvalidDataException("当前便携备份上限为64MB。");
            var bytes=File.ReadAllBytes(snapshot);
            var salt=RandomNumberGenerator.GetBytes(32);var nonce=RandomNumberGenerator.GetBytes(12);
            var header=new BackupHeader(app,1,Convert.ToBase64String(salt),Convert.ToBase64String(nonce),600000,DateTimeOffset.UtcNow);
            var aad=JsonSerializer.SerializeToUtf8Bytes(header);var cipher=new byte[bytes.Length];var tag=new byte[16];
            var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,header.Iterations,HashAlgorithmName.SHA256,32);
            try{using var aes=new AesGcm(key,16);aes.Encrypt(nonce,bytes,cipher,tag,aad);}
            finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(bytes);}
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {stream.Write(Magic);stream.Write(BitConverter.GetBytes(aad.Length));stream.Write(aad);stream.Write(tag);stream.Write(cipher);stream.Flush(true);}
                File.Move(temporary,path,false);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        finally{File.Delete(snapshot);}
    }
    public static BackupHeader Header(string path)
    {
        using var stream=File.OpenRead(path);return ReadHeader(stream).header;
    }
    public static T ReadData<T>(string path,string password,string expectedApp,string stagingDirectory) where T:new()
    {
        byte[] plaintext;
        using(var stream=File.OpenRead(path))
        {
            if(stream.Length>MaximumBytes+8192)throw new InvalidDataException("备份文件过大。");
            var (header,aad)=ReadHeader(stream);
            if(header.App!=expectedApp)throw new InvalidDataException("此备份属于其他应用。");
            var salt=Convert.FromBase64String(header.Salt);var nonce=Convert.FromBase64String(header.Nonce);
            if(salt.Length!=32||nonce.Length!=12||header.Iterations!=600000)throw new InvalidDataException("备份加密参数无效。");
            var remaining=(int)(stream.Length-stream.Position);if(remaining<17)throw new InvalidDataException("备份内容不完整。");
            var tag=new byte[16];stream.ReadExactly(tag);var cipher=new byte[remaining-16];stream.ReadExactly(cipher);plaintext=new byte[cipher.Length];
            var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,header.Iterations,HashAlgorithmName.SHA256,32);
            try{using var aes=new AesGcm(key,16);aes.Decrypt(nonce,cipher,tag,plaintext,aad);}
            catch(CryptographicException){CryptographicOperations.ZeroMemory(plaintext);throw new InvalidDataException("密码错误或备份已损坏。");}
            finally{CryptographicOperations.ZeroMemory(key);}
        }
        Directory.CreateDirectory(stagingDirectory);var temp=Path.Combine(stagingDirectory,Guid.NewGuid().ToString("N")+".db");
        try
        {
            File.WriteAllBytes(temp,plaintext);
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temp,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
            using var check=db.CreateCommand();check.CommandText="PRAGMA integrity_check";
            if((string?)check.ExecuteScalar()!="ok")throw new InvalidDataException("备份数据库校验失败。");
            check.CommandText="PRAGMA user_version";if(Convert.ToInt32(check.ExecuteScalar())!=1)throw new InvalidDataException("备份数据库版本不受支持。");
            check.CommandText="SELECT payload FROM document WHERE id=1";
            return JsonSerializer.Deserialize<T>((string)check.ExecuteScalar()!,DocumentStore<T>.Json)??throw new InvalidDataException("备份数据为空。");
        }
        finally{CryptographicOperations.ZeroMemory(plaintext);if(File.Exists(temp))File.Delete(temp);}
    }
    private static (BackupHeader header,byte[] aad) ReadHeader(Stream stream)
    {
        var magic=new byte[8];stream.ReadExactly(magic);if(!magic.SequenceEqual(Magic))throw new InvalidDataException("不是哲里加密备份。");
        var size=new byte[4];stream.ReadExactly(size);var length=BitConverter.ToInt32(size);
        if(length<1||length>4096)throw new InvalidDataException("备份头长度无效。");
        var aad=new byte[length];stream.ReadExactly(aad);var header=JsonSerializer.Deserialize<BackupHeader>(aad)??throw new InvalidDataException("备份头损坏。");
        if(header.Format!=1)throw new InvalidDataException("备份格式版本不受支持。");return(header,aad);
    }
}
