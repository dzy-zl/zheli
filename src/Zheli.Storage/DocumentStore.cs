using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Zheli.Storage;

public sealed record Snapshot<T>(long Revision, T Data);
public sealed record Receipt(string OperationId, long Revision, bool Replayed = false);
public sealed record OperationInfo(string Id, string Summary, long Revision, DateTimeOffset Time, string? Undoes);
public sealed class StoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// Each process owns only its app's database. Snapshots are versioned aggregate JSON in SQLite,
// with transactional journal and optimistic concurrency. No cross-app direct DB access.
public sealed class DocumentStore<T> where T : new()
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    public DocumentStore(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var db = Open();
        using var version = db.CreateCommand(); version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema > 1) throw new StoreException("NEWER_SCHEMA", "数据来自更新的软件版本，不能使用旧版本打开。");
        using var tx = db.BeginTransaction();
        Execute(db, tx, """
            CREATE TABLE IF NOT EXISTS document(id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS operation(id TEXT PRIMARY KEY, request_id TEXT NOT NULL UNIQUE, request_hash TEXT NOT NULL,
              before_payload TEXT NOT NULL, after_payload TEXT NOT NULL, revision INTEGER NOT NULL,
              summary TEXT NOT NULL, created TEXT NOT NULL, undoes TEXT);
            PRAGMA user_version=1;
            """);
        Execute(db, tx, "INSERT OR IGNORE INTO document VALUES(1,0,$p)", ("$p", JsonSerializer.Serialize(new T(), Json)));
        tx.Commit();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 10 }.ToString());
        db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;";
        cmd.ExecuteNonQuery(); return db;
    }
    public Snapshot<T> Read()
    {
        using var db = Open(); return Read(db, null);
    }
    private static Snapshot<T> Read(SqliteConnection db, SqliteTransaction? tx)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT revision,payload FROM document WHERE id=1";
        using var reader = cmd.ExecuteReader(); reader.Read();
        return new(reader.GetInt64(0), JsonSerializer.Deserialize<T>(reader.GetString(1), Json) ?? throw new InvalidDataException("数据为空。"));
    }
    public Receipt Write(string requestId, long expectedRevision, string summary, Func<T,T> change,
        string fingerprint, Action<T>? validate = null)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        var replay = Replay(db, tx, requestId, fingerprint);
        if (replay != null) return replay;
        var old = Read(db, tx);
        if (old.Revision != expectedRevision) throw new StoreException("STALE_VERSION", "数据已被其他操作更新，请刷新后重试。");
        var next = change(old.Data); validate?.Invoke(next);
        var receipt = Commit(db, tx, requestId, fingerprint, old, next, summary, null);
        tx.Commit(); return receipt;
    }
    public Receipt Undo(string requestId, string operationId, long expectedRevision, Action<T>? validate = null)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        var hash = Hash("undo:" + operationId + ":" + expectedRevision);
        var replay = Replay(db, tx, requestId, hash); if (replay != null) return replay;
        var current = Read(db, tx);
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT before_payload,revision FROM operation WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", operationId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new StoreException("NOT_FOUND", "操作不存在。");
        var previous = JsonSerializer.Deserialize<T>(reader.GetString(0), Json)!;
        if (reader.GetInt64(1) != current.Revision || current.Revision != expectedRevision)
            throw new StoreException("UNDO_CONFLICT", "之后已有修改，不能直接覆盖；请查看差异后手动处理。");
        reader.Close(); validate?.Invoke(previous);
        var receipt = Commit(db, tx, requestId, hash, current, previous, "撤销操作", operationId);
        tx.Commit(); return receipt;
    }
    private static Receipt? Replay(SqliteConnection db, SqliteTransaction tx, string requestId, string hash)
    {
        if (!Guid.TryParse(requestId, out _)) throw new StoreException("BAD_ID", "请求编号必须是UUID。");
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT id,revision,request_hash FROM operation WHERE request_id=$r";
        cmd.Parameters.AddWithValue("$r", requestId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        if (r.GetString(2) != hash) throw new StoreException("IDEMPOTENCY_MISMATCH", "同一请求编号不能用于不同操作。");
        return new(r.GetString(0), r.GetInt64(1), true);
    }
    private static Receipt Commit(SqliteConnection db, SqliteTransaction tx, string requestId, string hash,
        Snapshot<T> old, T next, string summary, string? undoes)
    {
        var id = Guid.NewGuid().ToString(); var revision = old.Revision + 1;
        var payload = JsonSerializer.Serialize(next, Json);
        Execute(db, tx, "UPDATE document SET payload=$p,revision=$v WHERE id=1", ("$p", payload), ("$v", revision));
        Execute(db, tx, "INSERT INTO operation VALUES($id,$r,$h,$b,$a,$v,$s,$t,$u)",
            ("$id", id), ("$r", requestId), ("$h", hash), ("$b", JsonSerializer.Serialize(old.Data, Json)),
            ("$a", payload), ("$v", revision), ("$s", summary), ("$t", DateTimeOffset.UtcNow.ToString("O")), ("$u", (object?)undoes ?? DBNull.Value));
        return new(id, revision);
    }
    public List<OperationInfo> History()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,summary,revision,created,undoes FROM operation ORDER BY revision DESC LIMIT 100";
        using var r = cmd.ExecuteReader(); var list = new List<OperationInfo>();
        while (r.Read()) list.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),DateTimeOffset.Parse(r.GetString(3)),r.IsDBNull(4)?null:r.GetString(4)));
        return list;
    }
    public string Backup(string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"snapshot-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        using var source = Open(); using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=target, Pooling=false }.ToString());
        destination.Open(); source.BackupDatabase(destination);
        using var check = destination.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
        if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("备份完整性检查失败。");
        return target;
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Execute(SqliteConnection db, SqliteTransaction tx, string sql, params (string,object)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key,value) in values) cmd.Parameters.AddWithValue(key,value);
        cmd.ExecuteNonQuery();
    }
}
