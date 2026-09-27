using System.Globalization;
using MhwModManager.Core;
namespace MhwModManager.Storage;
public sealed record ProfileSummary(string Id,string Name,int EnabledMods,DateTimeOffset UpdatedAt);
public sealed class ProfileRepository(ManagerDatabase db)
{
    public async Task<IReadOnlyDictionary<string,(bool enabled,int priority)>> LoadAsync(string profileId,CancellationToken ct=default){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var map=new Dictionary<string,(bool,int)>(StringComparer.OrdinalIgnoreCase);await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT mod_id,enabled,priority FROM profile_mods WHERE profile_id=$p";cmd.Parameters.AddWithValue("$p",profileId);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))map[r.GetString(0)]=(r.GetInt64(1)!=0,r.GetInt32(2));return map;}
    public async Task<IReadOnlyList<ProfileSummary>> ListAsync(CancellationToken ct=default){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var list=new List<ProfileSummary>();await using var c=await db.OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT p.id,p.name,SUM(CASE WHEN pm.enabled=1 THEN 1 ELSE 0 END),p.updated_at FROM profiles p LEFT JOIN profile_mods pm ON pm.profile_id=p.id GROUP BY p.id ORDER BY p.name COLLATE NOCASE";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?0:r.GetInt32(2),DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture)));return list;}
    public async Task SaveCurrentAsync(string name,CancellationToken ct=default){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow.ToString("O");await using var c=await db.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);await using(var cmd=c.CreateCommand()){cmd.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;cmd.CommandText="INSERT INTO profiles(id,name,created_at,updated_at) VALUES($i,$n,$u,$u) ON CONFLICT(name) DO UPDATE SET updated_at=excluded.updated_at RETURNING id";cmd.Parameters.AddWithValue("$i",id);cmd.Parameters.AddWithValue("$n",name);cmd.Parameters.AddWithValue("$u",now);id=(string)(await cmd.ExecuteScalarAsync(ct))!;}await using(var del=c.CreateCommand()){del.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;del.CommandText="DELETE FROM profile_mods WHERE profile_id=$p";del.Parameters.AddWithValue("$p",id);await del.ExecuteNonQueryAsync(ct);}await using(var ins=c.CreateCommand()){ins.Transaction=(Microsoft.Data.Sqlite.SqliteTransaction)tx;ins.CommandText="INSERT INTO profile_mods(profile_id,mod_id,enabled,priority) SELECT $p,id,enabled,priority FROM mods";ins.Parameters.AddWithValue("$p",id);await ins.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);}
}
