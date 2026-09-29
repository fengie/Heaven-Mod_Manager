using System.Globalization;
using Microsoft.Data.Sqlite;
using MhwModManager.Core;

namespace MhwModManager.Storage;

/// <summary>
/// Connection-per-operation SQLite facade. WAL is intentionally used without SQLite shared-cache;
/// shared-cache changes locking semantics and works against the WAL concurrency model.
/// </summary>
public sealed class ManagerDatabase(string databasePath)
{
    private const int BusyTimeoutSeconds = 10;
    public string DatabasePath { get; } = databasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Default,
        Pooling = true,
        DefaultTimeout = BusyTimeoutSeconds
    }.ToString();

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var c = await OpenRawAsync(ct);
        await ExecAsync(c, "PRAGMA journal_mode=WAL;", ct);
        await ExecAsync(c, "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000; PRAGMA wal_autocheckpoint=1000; PRAGMA journal_size_limit=67108864;", ct);
        await ExecAsync(c, Schema.Sql, ct);

        var version = await GetSchemaVersionAsync(c, ct);
        if (version < 2)
            await RebuildArmorIndexAsync(c, ct);

        if (version < 5)
            await SetSchemaVersionAsync(c, 5, ct);
        else
            await ExecAsync(c, "INSERT OR IGNORE INTO schema_info(key,value) VALUES('version','5');", ct);
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var c = await OpenRawAsync(ct);
        try
        {
            await ExecAsync(c, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;", ct);
            return c;
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var c = new SqliteConnection(ConnectionString);
        await c.OpenAsync(ct);
        return c;
    }

    private static async Task ExecAsync(SqliteConnection c, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection c, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key='version'";
        var value = await cmd.ExecuteScalarAsync(ct) as string;
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static async Task SetSchemaVersionAsync(SqliteConnection c, int version, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO schema_info(key,value) VALUES('version',$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$v", version.ToString(CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RebuildArmorIndexAsync(SqliteConnection c, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await ExecAsync(c, "DELETE FROM mod_file_armor;", ct, tx);

        var rows = new List<(string modId, string path)>();
        await using (var read = c.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT mod_id,path FROM mod_files";
            await using var r = await read.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rows.Add((r.GetString(0), r.GetString(1)));
        }

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT OR IGNORE INTO mod_file_armor(mod_id,path,model_id,component) VALUES($m,$p,$model,$component)";
        var pMod = insert.Parameters.Add("$m", SqliteType.Text);
        var pPath = insert.Parameters.Add("$p", SqliteType.Text);
        var pModel = insert.Parameters.Add("$model", SqliteType.Text);
        var pComponent = insert.Parameters.Add("$component", SqliteType.Text);
        insert.Prepare();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!PathRules.TryGetArmorComponent(row.path, out var modelId, out var component)) continue;
            pMod.Value = row.modId;
            pPath.Value = row.path;
            pModel.Value = modelId;
            pComponent.Value = component;
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task RebuildArmorIndexAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await RebuildArmorIndexAsync(c, ct);
    }

    public async Task<IReadOnlyList<ModDescriptor>> GetModsAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var list = new List<ModDescriptor>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT m.id,m.name,m.display_name,m.source_path,m.enabled,m.priority,m.family_id,m.category,m.source_url,m.nexus_mod_id,m.nexus_file_id,
               p.nexus_mod_uuid,p.nexus_version_id,p.nexus_previous_version_id,p.nexus_category,p.nexus_version,p.uploaded_at,
               CASE WHEN s.older_mod_id IS NULL THEN 0 ELSE 1 END,s.newer_mod_id,p.confidence_score,p.provenance_source,
               (SELECT value FROM settings WHERE key='preview:'||m.id),
               COALESCE((SELECT required FROM mod_revalidation rv WHERE rv.mod_id=m.id),0),
               fm.role
        FROM mods m
        LEFT JOIN mod_provenance p ON p.mod_id=m.id
        LEFT JOIN mod_supersession s ON s.older_mod_id=m.id
        LEFT JOIN mod_family_members fm ON fm.mod_id=m.id AND fm.family_id=m.family_id
        ORDER BY m.priority,m.name COLLATE NOCASE
        """;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0, r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? NexusFileCategory.Unknown : Enum.Parse<NexusFileCategory>(r.GetString(14), true),
                r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? null : DateTimeOffset.Parse(r.GetString(16), CultureInfo.InvariantCulture),
                r.GetInt64(17) != 0, r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? 0 : r.GetInt32(19),
                r.IsDBNull(20) ? ProvenanceSource.Unknown : Enum.Parse<ProvenanceSource>(r.GetString(20), true),
                r.IsDBNull(21) ? null : r.GetString(21), r.GetInt64(22) != 0, r.IsDBNull(23) ? null : r.GetString(23)));
        }
        return list;
    }

    public async Task SetEnabledAndPriorityAsync(IReadOnlyDictionary<string,(bool enabled,int priority)> state, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE mods SET enabled=$e,priority=$p,updated_at=$u WHERE id=$id";
        var pEnabled = cmd.Parameters.Add("$e", SqliteType.Integer);
        var pPriority = cmd.Parameters.Add("$p", SqliteType.Integer);
        var pUpdated = cmd.Parameters.Add("$u", SqliteType.Text);
        var pId = cmd.Parameters.Add("$id", SqliteType.Text);
        cmd.Prepare();
        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var (id,v) in state)
        {
            ct.ThrowIfCancellationRequested();
            pEnabled.Value = v.enabled ? 1 : 0;
            pPriority.Value = v.priority;
            pUpdated.Value = now;
            pId.Value = id;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task UpsertModAsync(ModDescriptor mod, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO mods(id,name,display_name,source_path,enabled,priority,family_id,category,source_url,nexus_mod_id,nexus_file_id,imported_at,updated_at)
        VALUES($id,$n,$d,$s,$e,$p,$f,$c,$u,$nm,$nf,$now,$now)
        ON CONFLICT(id) DO UPDATE SET name=excluded.name,display_name=excluded.display_name,source_path=excluded.source_path,enabled=excluded.enabled,priority=excluded.priority,family_id=excluded.family_id,category=COALESCE(excluded.category,mods.category),source_url=COALESCE(excluded.source_url,mods.source_url),nexus_mod_id=COALESCE(excluded.nexus_mod_id,mods.nexus_mod_id),nexus_file_id=COALESCE(excluded.nexus_file_id,mods.nexus_file_id),updated_at=excluded.updated_at;
        """;
        cmd.Parameters.AddWithValue("$id",mod.Id);
        cmd.Parameters.AddWithValue("$n",mod.Name);
        cmd.Parameters.AddWithValue("$d",mod.DisplayName);
        cmd.Parameters.AddWithValue("$s",mod.SourcePath);
        cmd.Parameters.AddWithValue("$e",mod.Enabled?1:0);
        cmd.Parameters.AddWithValue("$p",mod.Priority);
        cmd.Parameters.AddWithValue("$f",(object?)mod.FamilyId??DBNull.Value);
        cmd.Parameters.AddWithValue("$c",(object?)mod.Category??DBNull.Value);
        cmd.Parameters.AddWithValue("$u",(object?)mod.SourceUrl??DBNull.Value);
        cmd.Parameters.AddWithValue("$nm",(object?)mod.NexusModId??DBNull.Value);
        cmd.Parameters.AddWithValue("$nf",(object?)mod.NexusFileId??DBNull.Value);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReplaceModFilesAsync(string modId, IReadOnlyList<ModFileDescriptor> files, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM mod_files WHERE mod_id=$id";
            del.Parameters.AddWithValue("$id",modId);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using var insert = c.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO mod_files(mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class) VALUES($m,$p,$b,$f,$l,$u,$c)";
        var pMod = insert.Parameters.Add("$m", SqliteType.Text);
        var pPath = insert.Parameters.Add("$p", SqliteType.Text);
        var pBlob = insert.Parameters.Add("$b", SqliteType.Text);
        var pFast = insert.Parameters.Add("$f", SqliteType.Text);
        var pLen = insert.Parameters.Add("$l", SqliteType.Integer);
        var pUpdated = insert.Parameters.Add("$u", SqliteType.Text);
        var pClass = insert.Parameters.Add("$c", SqliteType.Text);
        insert.Prepare();

        await using var blob = c.CreateCommand();
        blob.Transaction = tx;
        blob.CommandText = "INSERT OR IGNORE INTO blobs(sha256,size,created_at,verified_at) VALUES($h,$s,$u,$u)";
        var bHash = blob.Parameters.Add("$h", SqliteType.Text);
        var bSize = blob.Parameters.Add("$s", SqliteType.Integer);
        var bUtc = blob.Parameters.Add("$u", SqliteType.Text);
        blob.Prepare();

        await using var armor = c.CreateCommand();
        armor.Transaction = tx;
        armor.CommandText = "INSERT INTO mod_file_armor(mod_id,path,model_id,component) VALUES($m,$p,$model,$component)";
        var aMod = armor.Parameters.Add("$m", SqliteType.Text);
        var aPath = armor.Parameters.Add("$p", SqliteType.Text);
        var aModel = armor.Parameters.Add("$model", SqliteType.Text);
        var aComponent = armor.Parameters.Add("$component", SqliteType.Text);
        armor.Prepare();

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var capturedAt = DateTimeOffset.UtcNow.ToString("O");
            bHash.Value = f.BlobSha256;
            bSize.Value = f.Length;
            bUtc.Value = capturedAt;
            await blob.ExecuteNonQueryAsync(ct);

            pMod.Value = modId;
            pPath.Value = f.Path;
            pBlob.Value = f.BlobSha256;
            pFast.Value = (object?)f.FastHash ?? DBNull.Value;
            pLen.Value = f.Length;
            pUpdated.Value = f.LastWriteUtc.ToString("O");
            pClass.Value = f.FileClass.ToString();
            await insert.ExecuteNonQueryAsync(ct);

            if (PathRules.TryGetArmorComponent(f.Path, out var modelId, out var component))
            {
                aMod.Value = modId;
                aPath.Value = f.Path;
                aModel.Value = modelId;
                aComponent.Value = component;
                await armor.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ModFileDescriptor>> GetModFilesAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var files = new List<ModFileDescriptor>();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT mod_id,path,blob_sha256,fast_hash,length,last_write_utc,file_class FROM mod_files";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            files.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetInt64(4),DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture),Enum.Parse<FileClass>(r.GetString(6))));
        return files;
    }

    public async Task SetFamilyIdAsync(string modId,string? familyId,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE mods SET family_id=$f,updated_at=$u WHERE id=$m";
        cmd.Parameters.AddWithValue("$f",(object?)familyId??DBNull.Value);cmd.Parameters.AddWithValue("$u",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$m",modId);await cmd.ExecuteNonQueryAsync(ct);
    }


    /// <summary>
    /// Persists an explicit user-authored logical family. The selected main package remains the
    /// family root; every supplied child is recorded as an Optional member and receives an explicit
    /// main->optional overlay rule. No source files are moved or modified.
    /// </summary>
    public async Task<string> ChainManualFamilyAsync(
        string mainModId,
        IReadOnlyCollection<string> mainMemberIds,
        IReadOnlyList<IReadOnlyList<string>> optionalGroups,
        string familyName,
        CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"main={mainModId}; mainMembers={mainMemberIds.Count}; optionalGroups={optionalGroups.Count}");
        if(string.IsNullOrWhiteSpace(mainModId))throw new ArgumentException("A main mod is required.",nameof(mainModId));
        var mains=mainMemberIds.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var groups=optionalGroups
            .Select(g=>(IReadOnlyList<string>)g.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            .Where(g=>g.Count>0)
            .ToArray();
        var optionals=groups.SelectMany(x=>x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(mains.Length==0||!mains.Contains(mainModId,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("The selected main mod must be part of the main member set.",nameof(mainMemberIds));
        if(optionals.Length==0)throw new ArgumentException("At least one optional child is required.",nameof(optionalGroups));
        if(mains.Intersect(optionals,StringComparer.OrdinalIgnoreCase).Any())throw new ArgumentException("A package cannot be both main and optional.",nameof(optionalGroups));
        if(optionals.Length!=groups.Sum(g=>g.Count))throw new ArgumentException("An optional package cannot appear in more than one chained group.",nameof(optionalGroups));

        await using var c=await OpenAsync(ct);
        await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        var all=mains.Concat(optionals).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var priorFamily=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        var priorRole=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
        await using(var read=c.CreateCommand())
        {
            read.Transaction=tx;
            var names=new string[all.Length];
            for(var i=0;i<all.Length;i++){names[i]="$m"+i.ToString(CultureInfo.InvariantCulture);read.Parameters.AddWithValue(names[i],all[i]);}
            read.CommandText=$"SELECT m.id,m.family_id,fm.role FROM mods m LEFT JOIN mod_family_members fm ON fm.mod_id=m.id AND fm.family_id=m.family_id WHERE m.id IN ({string.Join(',',names)})";
            await using var reader=await read.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                priorFamily[reader.GetString(0)]=reader.IsDBNull(1)?null:reader.GetString(1);
                priorRole[reader.GetString(0)]=reader.IsDBNull(2)?null:reader.GetString(2);
            }
        }
        if(!priorFamily.TryGetValue(mainModId,out var existingFamily))throw new InvalidOperationException($"Main mod '{mainModId}' no longer exists.");
        var familyId=!string.IsNullOrWhiteSpace(existingFamily)&&existingFamily.StartsWith("manual:",StringComparison.OrdinalIgnoreCase)
            ?existingFamily
            :"manual:"+mainModId.ToLowerInvariant();
        var now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);

        // Remove stale pairwise relationships only BETWEEN the logical groups the user is now
        // chaining. Relationships inside an already-composite option are preserved. If the selected
        // conflict itself is an internal same-family choice, its singleton options are different
        // chain groups, so this explicit action correctly replaces the old relationship.
        var orderedGroups=new List<IReadOnlyList<string>>{mains};orderedGroups.AddRange(groups);
        var chainGroupByMod=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for(var groupIndex=0;groupIndex<orderedGroups.Count;groupIndex++)foreach(var id in orderedGroups[groupIndex])chainGroupByMod[id]=groupIndex;
        for(var i=0;i<all.Length;i++)for(var j=i+1;j<all.Length;j++)
        {
            var a=all[i];var b=all[j];
            if(chainGroupByMod[a]==chainGroupByMod[b])continue;
            await using var del=c.CreateCommand();del.Transaction=tx;
            del.CommandText="DELETE FROM conflict_rules WHERE scope=$scope AND ((left_mod_id=$a AND right_mod_id=$b) OR (left_mod_id=$b AND right_mod_id=$a))";
            del.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());del.Parameters.AddWithValue("$a",a);del.Parameters.AddWithValue("$b",b);
            await del.ExecuteNonQueryAsync(ct);
        }

        await using(var fam=c.CreateCommand())
        {
            fam.Transaction=tx;fam.CommandText="INSERT INTO mod_families(id,name,created_at) VALUES($id,$n,$t) ON CONFLICT(id) DO UPDATE SET name=excluded.name";
            fam.Parameters.AddWithValue("$id",familyId);fam.Parameters.AddWithValue("$n",string.IsNullOrWhiteSpace(familyName)?mainModId:familyName);fam.Parameters.AddWithValue("$t",now);
            await fam.ExecuteNonQueryAsync(ct);
        }

        foreach(var id in all)
        {
            await using(var remove=c.CreateCommand())
            {
                remove.Transaction=tx;remove.CommandText="DELETE FROM mod_family_members WHERE mod_id=$m";remove.Parameters.AddWithValue("$m",id);await remove.ExecuteNonQueryAsync(ct);
            }
            await using(var update=c.CreateCommand())
            {
                update.Transaction=tx;update.CommandText="UPDATE mods SET family_id=$f,updated_at=$u WHERE id=$m";
                update.Parameters.AddWithValue("$f",familyId);update.Parameters.AddWithValue("$u",now);update.Parameters.AddWithValue("$m",id);await update.ExecuteNonQueryAsync(ct);
            }
            await using(var member=c.CreateCommand())
            {
                member.Transaction=tx;member.CommandText="INSERT INTO mod_family_members(family_id,mod_id,role,choice_group) VALUES($f,$m,$r,NULL)";
                member.Parameters.AddWithValue("$f",familyId);member.Parameters.AddWithValue("$m",id);
                var oldRole=priorRole.GetValueOrDefault(id);
                var role=StringComparer.OrdinalIgnoreCase.Equals(id,mainModId)?"Main"
                    :mains.Contains(id,StringComparer.OrdinalIgnoreCase)?oldRole??"Component"
                    :StringComparer.OrdinalIgnoreCase.Equals(oldRole,"Main")||string.IsNullOrWhiteSpace(oldRole)?"Optional":"Component";
                member.Parameters.AddWithValue("$r",role);
                await member.ExecuteNonQueryAsync(ct);
            }
        }

        // Persist a total cross-group order, not merely main->child stars. If Main, Optional A and
        // Optional B all carry the same shared base file, A->B ensures the two optional layers do
        // not reappear as a second conflict. We write the transitive closure so resolution remains
        // deterministic even when an intermediate optional group does not provide a particular path.
        var proposedEdges=new List<(string lower,string higher)>();
        for(var i=0;i<orderedGroups.Count-1;i++)for(var j=i+1;j<orderedGroups.Count;j++)
        foreach(var lower in orderedGroups[i])foreach(var higher in orderedGroups[j])proposedEdges.Add((lower,higher));

        // Fail before commit if the new explicit chain would create a precedence cycle through some
        // existing outside relationship. The whole transaction then rolls back without partially
        // rewriting family metadata.
        var graphRules=new List<ConflictRule>();
        await using(var graph=c.CreateCommand())
        {
            graph.Transaction=tx;graph.CommandText="SELECT id,left_mod_id,right_mod_id,winner_mod_id,explicit,created_at FROM conflict_rules WHERE kind=$kind AND scope=$scope AND left_mod_id IS NOT NULL AND right_mod_id IS NOT NULL AND winner_mod_id IS NOT NULL";
            graph.Parameters.AddWithValue("$kind",RuleKind.Overlay.ToString());graph.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());
            await using var reader=await graph.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))graphRules.Add(new(reader.GetString(0),RuleKind.Overlay,RuleScope.ModPair,reader.GetString(1),reader.GetString(2),reader.GetString(3),null,"existing overlay",reader.GetInt64(4)!=0,DateTimeOffset.Parse(reader.GetString(5),CultureInfo.InvariantCulture)));
        }
        graphRules.AddRange(proposedEdges.Select((edge,index)=>new ConflictRule($"manual-chain-proposed-{index}",RuleKind.Overlay,RuleScope.ModPair,edge.lower,edge.higher,edge.higher,null,"manual chain validation",true,DateTimeOffset.UtcNow)));
        var cycle=RuleGraph.FindCycle(graphRules);
        if(cycle is not null)throw new InvalidOperationException($"Manual family chain would create a precedence cycle: {string.Join(" -> ",cycle)}");

        foreach(var (lower,higher) in proposedEdges)
        {
            await using var rule=c.CreateCommand();rule.Transaction=tx;
            rule.CommandText="""
            INSERT INTO conflict_rules(id,kind,scope,left_mod_id,right_mod_id,winner_mod_id,path_pattern,reason,explicit,created_at)
            VALUES($id,$kind,$scope,$left,$right,$winner,NULL,$reason,1,$created)
            """;
            rule.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));rule.Parameters.AddWithValue("$kind",RuleKind.Overlay.ToString());rule.Parameters.AddWithValue("$scope",RuleScope.ModPair.ToString());
            rule.Parameters.AddWithValue("$left",lower);rule.Parameters.AddWithValue("$right",higher);rule.Parameters.AddWithValue("$winner",higher);
            rule.Parameters.AddWithValue("$reason",$"Manual family chain: '{higher}' is a later optional/component layer above '{lower}' in family '{familyId}'.");rule.Parameters.AddWithValue("$created",now);
            await rule.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        MasterDebugLog.Write("FAMILY-MANUAL",$"CHAIN family={familyId}; main={mainModId}; mainMembers=[{string.Join(" | ",mains)}]; optionalMembers=[{string.Join(" | ",optionals)}]");
        return familyId;
    }

    public async Task UpsertProvenanceAsync(ModProvenance provenance, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO mod_provenance(mod_id,nexus_game_mod_id,nexus_mod_uuid,nexus_file_id,nexus_version_id,nexus_previous_version_id,nexus_category,nexus_file_name,nexus_version,uploaded_at,provenance_source,confidence_score,source_archive_name,metadata_json,updated_at)
        VALUES($m,$gm,$mu,$f,$v,$pv,$cat,$fn,$ver,$up,$src,$score,$archive,$json,$now)
        ON CONFLICT(mod_id) DO UPDATE SET nexus_game_mod_id=excluded.nexus_game_mod_id,nexus_mod_uuid=excluded.nexus_mod_uuid,nexus_file_id=excluded.nexus_file_id,nexus_version_id=excluded.nexus_version_id,nexus_previous_version_id=excluded.nexus_previous_version_id,nexus_category=excluded.nexus_category,nexus_file_name=excluded.nexus_file_name,nexus_version=excluded.nexus_version,uploaded_at=excluded.uploaded_at,provenance_source=excluded.provenance_source,confidence_score=excluded.confidence_score,source_archive_name=excluded.source_archive_name,metadata_json=excluded.metadata_json,updated_at=excluded.updated_at
        WHERE excluded.confidence_score >= mod_provenance.confidence_score
        """;
        cmd.Parameters.AddWithValue("$m",provenance.ModId);
        cmd.Parameters.AddWithValue("$gm",(object?)provenance.NexusGameModId??DBNull.Value);
        cmd.Parameters.AddWithValue("$mu",(object?)provenance.NexusModUuid??DBNull.Value);
        cmd.Parameters.AddWithValue("$f",(object?)provenance.NexusFileId??DBNull.Value);
        cmd.Parameters.AddWithValue("$v",(object?)provenance.NexusVersionId??DBNull.Value);
        cmd.Parameters.AddWithValue("$pv",(object?)provenance.NexusPreviousVersionId??DBNull.Value);
        cmd.Parameters.AddWithValue("$cat",provenance.NexusCategory.ToString());
        cmd.Parameters.AddWithValue("$fn",(object?)provenance.NexusFileName??DBNull.Value);
        cmd.Parameters.AddWithValue("$ver",(object?)provenance.NexusVersion??DBNull.Value);
        cmd.Parameters.AddWithValue("$up",(object?)provenance.UploadedAt?.ToString("O",CultureInfo.InvariantCulture)??DBNull.Value);
        cmd.Parameters.AddWithValue("$src",provenance.Source.ToString());
        cmd.Parameters.AddWithValue("$score",provenance.ConfidenceScore);
        cmd.Parameters.AddWithValue("$archive",(object?)provenance.SourceArchiveName??DBNull.Value);
        cmd.Parameters.AddWithValue("$json",(object?)provenance.MetadataJson??DBNull.Value);
        cmd.Parameters.AddWithValue("$now",provenance.UpdatedAt.ToString("O",CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string,ModProvenance>> GetProvenanceAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var result=new Dictionary<string,ModProvenance>(StringComparer.OrdinalIgnoreCase);
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT mod_id,nexus_game_mod_id,nexus_mod_uuid,nexus_file_id,nexus_version_id,nexus_previous_version_id,nexus_category,nexus_file_name,nexus_version,uploaded_at,provenance_source,confidence_score,source_archive_name,metadata_json,updated_at FROM mod_provenance";
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
        {
            var item=new ModProvenance(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),
                Enum.Parse<NexusFileCategory>(r.GetString(6),true),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(8)?null:r.GetString(8),r.IsDBNull(9)?null:DateTimeOffset.Parse(r.GetString(9),CultureInfo.InvariantCulture),
                Enum.Parse<ProvenanceSource>(r.GetString(10),true),r.GetInt32(11),r.IsDBNull(12)?null:r.GetString(12),r.IsDBNull(13)?null:r.GetString(13),DateTimeOffset.Parse(r.GetString(14),CultureInfo.InvariantCulture));
            result[item.ModId]=item;
        }
        return result;
    }

    public async Task ReplaceSupersessionAsync(IReadOnlyList<(string older,string newer,int score,string reason)> links,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await ExecAsync(c,"DELETE FROM mod_supersession",ct,tx);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO mod_supersession(older_mod_id,newer_mod_id,confidence_score,reason,created_at) VALUES($o,$n,$s,$r,$t)";
        var po=cmd.Parameters.Add("$o",SqliteType.Text);var pn=cmd.Parameters.Add("$n",SqliteType.Text);var ps=cmd.Parameters.Add("$s",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var pt=cmd.Parameters.Add("$t",SqliteType.Text);cmd.Prepare();
        foreach(var link in links){po.Value=link.older;pn.Value=link.newer;ps.Value=link.score;pr.Value=link.reason;pt.Value=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task RecordResolverAuditAsync(IEnumerable<ResolverAudit> audits,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO resolver_audit(time,path,winner_mod_id,score,reason_code,explanation,evidence) VALUES($t,$p,$w,$s,$r,$x,$e)";
        var pt=cmd.Parameters.Add("$t",SqliteType.Text);var pp=cmd.Parameters.Add("$p",SqliteType.Text);var pw=cmd.Parameters.Add("$w",SqliteType.Text);var ps=cmd.Parameters.Add("$s",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var px=cmd.Parameters.Add("$x",SqliteType.Text);var pe=cmd.Parameters.Add("$e",SqliteType.Text);cmd.Prepare();
        foreach(var a in audits){pt.Value=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);pp.Value=a.Path;pw.Value=(object?)a.WinnerModId??DBNull.Value;ps.Value=a.Score;pr.Value=a.ReasonCode;px.Value=a.Explanation;pe.Value=a.Evidence;await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task<GameBuildFingerprint?> GetGameBuildFingerprintAsync(CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT exe_path,length,last_write_utc,sha256 FROM game_build_state WHERE id=1";await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        return new(r.GetString(0),r.GetInt64(1),DateTimeOffset.Parse(r.GetString(2),CultureInfo.InvariantCulture),r.GetString(3));
    }

    public async Task SetGameBuildFingerprintAsync(GameBuildFingerprint fingerprint,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO game_build_state(id,exe_path,length,last_write_utc,sha256,observed_at) VALUES(1,$p,$l,$u,$s,$o) ON CONFLICT(id) DO UPDATE SET exe_path=excluded.exe_path,length=excluded.length,last_write_utc=excluded.last_write_utc,sha256=excluded.sha256,observed_at=excluded.observed_at";
        cmd.Parameters.AddWithValue("$p",fingerprint.ExecutablePath);cmd.Parameters.AddWithValue("$l",fingerprint.Length);cmd.Parameters.AddWithValue("$u",fingerprint.LastWriteUtc.ToString("O",CultureInfo.InvariantCulture));cmd.Parameters.AddWithValue("$s",fingerprint.Sha256);cmd.Parameters.AddWithValue("$o",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkRevalidationAsync(IEnumerable<string> modIds,string reason,bool required,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c=await OpenAsync(ct);await using var tx=(SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO mod_revalidation(mod_id,required,reason,detected_at) VALUES($m,$q,$r,$t) ON CONFLICT(mod_id) DO UPDATE SET required=excluded.required,reason=excluded.reason,detected_at=excluded.detected_at";
        var pm=cmd.Parameters.Add("$m",SqliteType.Text);var pq=cmd.Parameters.Add("$q",SqliteType.Integer);var pr=cmd.Parameters.Add("$r",SqliteType.Text);var pt=cmd.Parameters.Add("$t",SqliteType.Text);cmd.Prepare();
        foreach(var id in modIds){pm.Value=id;pq.Value=required?1:0;pr.Value=reason;pt.Value=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);await cmd.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct);
    }

    public async Task<string?> GetSettingAsync(string key,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"key={key}");
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);return (string?)await cmd.ExecuteScalarAsync(ct);}

    public async Task SetSettingAsync(string key,string value,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"key={key}; value=<redacted-if-sensitive>");
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$k",key);cmd.Parameters.AddWithValue("$v",value);await cmd.ExecuteNonQueryAsync(ct);}
    public async Task<IReadOnlyDictionary<string,string>> GetSettingsByPrefixAsync(string prefix,CancellationToken ct=default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"prefix={prefix}");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        await using var c=await OpenAsync(ct);await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT key,value FROM settings WHERE substr(key,1,length($p))=$p";
        cmd.Parameters.AddWithValue("$p",prefix);
        await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result[r.GetString(0)]=r.GetString(1);
        return result;
    }

    public async Task RetireModAsync(string modId, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"modId={modId}");
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID is required.", nameof(modId));

        await InTransactionAsync<object?>(async (c, tx, token) =>
        {
            await using (var manifest = c.CreateCommand())
            {
                manifest.Transaction = tx;
                manifest.CommandText = "SELECT EXISTS(SELECT 1 FROM deployment_manifest WHERE provider_mod_id=$m)";
                manifest.Parameters.AddWithValue("$m", modId);
                var isDeployed = Convert.ToInt64(await manifest.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0;
                if (isDeployed)
                    throw new InvalidOperationException($"Cannot retire mod '{modId}' while it still owns files in the current deployment manifest.");
            }

            await using (var rules = c.CreateCommand())
            {
                rules.Transaction = tx;
                rules.CommandText = "DELETE FROM conflict_rules WHERE left_mod_id=$m OR right_mod_id=$m OR winner_mod_id=$m";
                rules.Parameters.AddWithValue("$m", modId);
                await rules.ExecuteNonQueryAsync(token);
            }

            await using (var providers = c.CreateCommand())
            {
                providers.Transaction = tx;
                providers.CommandText = "DELETE FROM resource_providers WHERE mod_id=$m";
                providers.Parameters.AddWithValue("$m", modId);
                await providers.ExecuteNonQueryAsync(token);
            }

            await using (var settings = c.CreateCommand())
            {
                settings.Transaction = tx;
                settings.CommandText = """
                    DELETE FROM settings
                    WHERE key IN (
                        'preview:' || $m,
                        'visuals:' || $m,
                        'update:' || $m,
                        'visual-public-last:' || $m
                    )
                    """;
                settings.Parameters.AddWithValue("$m", modId);
                await settings.ExecuteNonQueryAsync(token);
            }

            await using (var delete = c.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM mods WHERE id=$m";
                delete.Parameters.AddWithValue("$m", modId);
                if (await delete.ExecuteNonQueryAsync(token) != 1)
                    throw new KeyNotFoundException($"Unknown mod '{modId}'.");
            }

            return null;
        }, ct);
    }

    public async Task ExecuteAsync(string sql, IReadOnlyDictionary<string,object?>? args = null, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"sql={CompactSql(sql)}; parameterCount={args?.Count ?? 0}");
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (args is not null) foreach (var kv in args) cmd.Parameters.AddWithValue(kv.Key,kv.Value??DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T> InTransactionAsync<T>(Func<SqliteConnection,SqliteTransaction,CancellationToken,Task<T>> body, CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod("sqlite-transaction");
        await using var c = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        MasterDebugLog.Write("DB-TX", "BEGIN SQLite transaction");
        try
        {
            var result = await body(c,tx,ct);
            await tx.CommitAsync(ct);
            MasterDebugLog.Write("DB-TX", "COMMIT SQLite transaction");
            return result;
        }
        catch(Exception ex)
        {
            MasterDebugLog.Write("DB-TX", "ROLLBACK SQLite transaction after failure", ex);
            try { await tx.RollbackAsync(CancellationToken.None); MasterDebugLog.Write("DB-TX", "ROLLBACK completed"); } catch(Exception rollbackEx) { MasterDebugLog.Write("DB-TX", "ROLLBACK itself failed", rollbackEx); }
            throw;
        }
    }

    private static string CompactSql(string sql)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var compact = string.Join(" ", sql.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }

    public async Task<string> IntegrityCheckAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        return (string?)await cmd.ExecuteScalarAsync(ct) ?? "unknown";
    }

    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE)";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
