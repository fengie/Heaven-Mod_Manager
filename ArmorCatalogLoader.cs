using MhwModManager.Core;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using MhwModManager.Storage;

namespace MhwModManager.Mhw;

public static class ArmorCatalogLoader
{
    private static readonly string[] NewLineSeparators = ["\r\n", "\n"];

    public static async Task ImportAsync(ManagerDatabase db,string csv,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!File.Exists(csv))return;
        var bytes=await File.ReadAllBytesAsync(csv,ct);
        var stamp=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using(var check=await db.OpenAsync(ct))
        await using(var cmd=check.CreateCommand())
        {
            cmd.CommandText="SELECT value FROM settings WHERE key='armor_catalog_sha256'";
            if(StringComparer.OrdinalIgnoreCase.Equals((string?)await cmd.ExecuteScalarAsync(ct),stamp))return;
        }

        var lines=System.Text.Encoding.UTF8.GetString(bytes).Split(NewLineSeparators,StringSplitOptions.None);
        await db.InTransactionAsync<object?>(async(c,tx,token)=>
        {
            await using(var clear=c.CreateCommand()){clear.Transaction=tx;clear.CommandText="DELETE FROM armor_catalog";await clear.ExecuteNonQueryAsync(token);}
            await using var insert=c.CreateCommand();
            insert.Transaction=tx;
            insert.CommandText="INSERT INTO armor_catalog(series_id,name,model_id) VALUES($i,$n,$m)";
            var pi=insert.Parameters.Add("$i",SqliteType.Integer);var pn=insert.Parameters.Add("$n",SqliteType.Text);var pm=insert.Parameters.Add("$m",SqliteType.Text);insert.Prepare();
            foreach(var line in lines.Skip(1))
            {
                token.ThrowIfCancellationRequested();
                var m=System.Text.RegularExpressions.Regex.Match(line,"^(\\d+),\\\"(.*)\\\",([^,]+)$");
                if(!m.Success)continue;
                pi.Value=int.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
                pn.Value=m.Groups[2].Value.Replace("\"\"","\"");
                pm.Value=m.Groups[3].Value.Trim();
                await insert.ExecuteNonQueryAsync(token);
            }
            await using var setting=c.CreateCommand();setting.Transaction=tx;setting.CommandText="INSERT INTO settings(key,value) VALUES('armor_catalog_sha256',$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";setting.Parameters.AddWithValue("$v",stamp);await setting.ExecuteNonQueryAsync(token);
            return null;
        },ct);
    }
}
