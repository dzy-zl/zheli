using System.Text.Json;
using Zheli.Bridge;
using Zheli.Contracts;
using Zheli.Domain;
using Zheli.Storage;

using var presence=AppPaths.HoldRuntimePresence();
var store = new DocumentStore<Preferences>(Path.Combine(AppPaths.DataRoot,"Core","settings.db"));
await BridgeServer.Run("core", (caller,request) => Task.FromResult(Handle(caller,request)), CancellationToken.None);

Response Handle(string caller, Request r)
{
    try
    {
        object result;
        switch (r.Method)
        {
            case "settings.read": result = store.Read(); break;
            case "settings.save" when caller == "Zheli.Settings":
                var save = r.Args<SavePreferences>();
                result = store.Write(r.Id, save.Revision, "修改全局设置", _ => save.Preferences,
                    DocumentStore<Preferences>.Hash(r.Arguments.GetRawText()), p =>
                    {
                        KnowledgePolicy.Validate(p.KnowledgeFolders);
                        if (p.Theme is not ("Light" or "Dark" or "System") || p.GlassClarity is < 0 or > 1 ||
                            p.TextScale is < .9 or > 1.5 || !System.Text.RegularExpressions.Regex.IsMatch(p.Accent,"^#[0-9A-Fa-f]{6}$"))
                            throw new DomainException("外观参数无效。");
                    }); break;
            case "settings.undo" when caller == "Zheli.Settings":
                var undo = r.Args<UndoRequest>(); result = store.Undo(r.Id,undo.OperationId,undo.Revision); break;
            case "settings.history" when caller == "Zheli.Settings": result = store.History(); break;
            case "settings.backup" when caller == "Zheli.Settings":
                result = new { path = store.Backup(Path.Combine(AppPaths.DataRoot,"Backups","Core")) }; break;
            default: return Protocol.Failure(r,"DENIED","此应用没有该能力的权限，或能力尚未实现。");
        }
        return Protocol.Success(r,result);
    }
    catch (StoreException e) { return Protocol.Failure(r,e.Code,e.Message); }
    catch (Exception e) when (e is DomainException or ArgumentException or JsonException)
    { return Protocol.Failure(r,"VALIDATION",e.Message); }
    catch { return Protocol.Failure(r,"STORAGE","保存未完成，请检查磁盘和备份；不要重复提交不确定的写入。"); }
}
