namespace MhwModManager.Storage;

internal static class Schema
{
public const string Sql = """
CREATE TABLE IF NOT EXISTS schema_info(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS mods(id TEXT PRIMARY KEY,name TEXT NOT NULL,display_name TEXT NOT NULL,source_path TEXT NOT NULL,enabled INTEGER NOT NULL DEFAULT 0,priority INTEGER NOT NULL DEFAULT 0,family_id TEXT NULL,category TEXT NULL,source_url TEXT NULL,nexus_mod_id TEXT NULL,nexus_file_id TEXT NULL,imported_at TEXT NOT NULL,updated_at TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ix_mods_source ON mods(source_path COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_mods_enabled_priority ON mods(enabled,priority);
CREATE TABLE IF NOT EXISTS blobs(sha256 TEXT PRIMARY KEY,size INTEGER NOT NULL,created_at TEXT NOT NULL,verified_at TEXT NULL);
CREATE TABLE IF NOT EXISTS mod_provenance(
    mod_id TEXT PRIMARY KEY REFERENCES mods(id) ON DELETE CASCADE,
    nexus_game_mod_id TEXT NULL,
    nexus_mod_uuid TEXT NULL,
    nexus_file_id TEXT NULL,
    nexus_version_id TEXT NULL,
    nexus_previous_version_id TEXT NULL,
    nexus_category TEXT NOT NULL DEFAULT 'Unknown',
    nexus_file_name TEXT NULL,
    nexus_version TEXT NULL,
    uploaded_at TEXT NULL,
    provenance_source TEXT NOT NULL DEFAULT 'Unknown',
    confidence_score INTEGER NOT NULL DEFAULT 0,
    source_archive_name TEXT NULL,
    metadata_json TEXT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_provenance_nexus_mod ON mod_provenance(nexus_game_mod_id,nexus_mod_uuid);
CREATE INDEX IF NOT EXISTS ix_provenance_nexus_file ON mod_provenance(nexus_file_id,nexus_version_id);
CREATE TABLE IF NOT EXISTS mod_supersession(
    older_mod_id TEXT PRIMARY KEY REFERENCES mods(id) ON DELETE CASCADE,
    newer_mod_id TEXT NOT NULL REFERENCES mods(id) ON DELETE CASCADE,
    confidence_score INTEGER NOT NULL,
    reason TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_supersession_newer ON mod_supersession(newer_mod_id);
CREATE TABLE IF NOT EXISTS family_preferences(
    family_id TEXT NOT NULL,
    choice_group TEXT NOT NULL,
    selected_mod_id TEXT NULL REFERENCES mods(id) ON DELETE SET NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY(family_id,choice_group)
);
CREATE TABLE IF NOT EXISTS resolver_audit(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    time TEXT NOT NULL,
    path TEXT NOT NULL COLLATE NOCASE,
    winner_mod_id TEXT NULL,
    score INTEGER NOT NULL,
    reason_code TEXT NOT NULL,
    explanation TEXT NOT NULL,
    evidence TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_resolver_audit_time ON resolver_audit(time DESC);
CREATE TABLE IF NOT EXISTS game_build_state(
    id INTEGER PRIMARY KEY CHECK(id=1),
    exe_path TEXT NOT NULL,
    length INTEGER NOT NULL,
    last_write_utc TEXT NOT NULL,
    sha256 TEXT NOT NULL,
    observed_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS mod_revalidation(
    mod_id TEXT PRIMARY KEY REFERENCES mods(id) ON DELETE CASCADE,
    required INTEGER NOT NULL,
    reason TEXT NULL,
    detected_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS adoption_runs(
    id TEXT PRIMARY KEY,
    created_mod_id TEXT NULL REFERENCES mods(id) ON DELETE SET NULL,
    source_root TEXT NOT NULL,
    file_count INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    status TEXT NOT NULL,
    error TEXT NULL
);
CREATE TABLE IF NOT EXISTS adopted_live_files(
    path TEXT PRIMARY KEY COLLATE NOCASE,
    sha256 TEXT NOT NULL,
    source_folder TEXT NOT NULL,
    adopted_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_adopted_live_files_source ON adopted_live_files(source_folder);

CREATE TABLE IF NOT EXISTS mod_files(mod_id TEXT NOT NULL REFERENCES mods(id) ON DELETE CASCADE,path TEXT NOT NULL COLLATE NOCASE,blob_sha256 TEXT NOT NULL,fast_hash TEXT NULL,length INTEGER NOT NULL,last_write_utc TEXT NOT NULL,file_class TEXT NOT NULL,PRIMARY KEY(mod_id,path));
CREATE INDEX IF NOT EXISTS ix_mod_files_path ON mod_files(path COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS ix_mod_files_blob ON mod_files(blob_sha256);
CREATE TABLE IF NOT EXISTS mod_file_armor(mod_id TEXT NOT NULL,path TEXT NOT NULL COLLATE NOCASE,model_id TEXT NOT NULL COLLATE NOCASE,component TEXT NOT NULL,PRIMARY KEY(mod_id,path),FOREIGN KEY(mod_id,path) REFERENCES mod_files(mod_id,path) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_mod_file_armor_model ON mod_file_armor(model_id COLLATE NOCASE,component);
CREATE TABLE IF NOT EXISTS original_files(path TEXT PRIMARY KEY COLLATE NOCASE,blob_sha256 TEXT NULL,captured_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS deployment_manifest(path TEXT PRIMARY KEY COLLATE NOCASE,provider_mod_id TEXT NULL,blob_sha256 TEXT NULL,expected_live_sha256 TEXT NULL,rule_id TEXT NULL,deployed_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS conflict_rules(id TEXT PRIMARY KEY,kind TEXT NOT NULL,scope TEXT NOT NULL,left_mod_id TEXT NULL,right_mod_id TEXT NULL,winner_mod_id TEXT NULL,path_pattern TEXT NULL COLLATE NOCASE,reason TEXT NOT NULL,explicit INTEGER NOT NULL,created_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_rules_pair ON conflict_rules(left_mod_id,right_mod_id);
CREATE INDEX IF NOT EXISTS ix_rules_path ON conflict_rules(path_pattern COLLATE NOCASE);
CREATE TABLE IF NOT EXISTS resource_providers(namespace TEXT PRIMARY KEY COLLATE NOCASE,mod_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS mod_families(id TEXT PRIMARY KEY,name TEXT NOT NULL,created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS mod_family_members(family_id TEXT NOT NULL REFERENCES mod_families(id) ON DELETE CASCADE,mod_id TEXT NOT NULL REFERENCES mods(id) ON DELETE CASCADE,role TEXT NOT NULL,choice_group TEXT NULL,PRIMARY KEY(family_id,mod_id));
CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY,name TEXT NOT NULL UNIQUE COLLATE NOCASE,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,legacy_json TEXT NULL);
CREATE TABLE IF NOT EXISTS profile_mods(profile_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,mod_id TEXT NOT NULL REFERENCES mods(id) ON DELETE CASCADE,enabled INTEGER NOT NULL,priority INTEGER NOT NULL,PRIMARY KEY(profile_id,mod_id));
CREATE TABLE IF NOT EXISTS profile_parents(profile_id TEXT PRIMARY KEY REFERENCES profiles(id) ON DELETE CASCADE,parent_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE RESTRICT,CHECK(profile_id <> parent_id));
CREATE TABLE IF NOT EXISTS profile_rules(profile_id TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,rule_id TEXT NOT NULL REFERENCES conflict_rules(id) ON DELETE CASCADE,PRIMARY KEY(profile_id,rule_id));
CREATE TABLE IF NOT EXISTS operations(id TEXT PRIMARY KEY,state TEXT NOT NULL,description TEXT NOT NULL,started_at TEXT NOT NULL,committed_at TEXT NULL,error TEXT NULL,state_before_json TEXT NULL,state_after_json TEXT NULL,parent_operation_id TEXT NULL);
CREATE TABLE IF NOT EXISTS operation_metadata(operation_id TEXT PRIMARY KEY REFERENCES operations(id) ON DELETE CASCADE,changes_json TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_operations_state_started ON operations(state,started_at);
CREATE TABLE IF NOT EXISTS operation_journal(operation_id TEXT NOT NULL REFERENCES operations(id) ON DELETE CASCADE,seq INTEGER NOT NULL,path TEXT NOT NULL COLLATE NOCASE,change_kind TEXT NOT NULL,before_sha256 TEXT NULL,after_sha256 TEXT NULL,provider_before TEXT NULL,provider_after TEXT NULL,status TEXT NOT NULL,error TEXT NULL,PRIMARY KEY(operation_id,seq));
CREATE INDEX IF NOT EXISTS ix_journal_status ON operation_journal(operation_id,status);
CREATE TABLE IF NOT EXISTS external_changes(path TEXT PRIMARY KEY COLLATE NOCASE,kind TEXT NOT NULL,observed_sha256 TEXT NULL,detected_at TEXT NOT NULL,acknowledged INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS ix_external_changes_ack ON external_changes(acknowledged,detected_at);
CREATE TABLE IF NOT EXISTS armor_catalog(series_id INTEGER NOT NULL,name TEXT NOT NULL,model_id TEXT NOT NULL COLLATE NOCASE,PRIMARY KEY(series_id,model_id));
CREATE INDEX IF NOT EXISTS ix_armor_model ON armor_catalog(model_id COLLATE NOCASE);
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS diagnostics(id INTEGER PRIMARY KEY AUTOINCREMENT,time TEXT NOT NULL,operation_id TEXT NULL,kind TEXT NOT NULL,elapsed_ms REAL NULL,data_json TEXT NULL);
CREATE INDEX IF NOT EXISTS ix_diagnostics_time ON diagnostics(time DESC);
CREATE INDEX IF NOT EXISTS ix_diagnostics_operation ON diagnostics(operation_id,time);
CREATE TABLE IF NOT EXISTS error_reports(id TEXT PRIMARY KEY,time TEXT NOT NULL,operation_id TEXT NULL,category TEXT NOT NULL,exception_type TEXT NOT NULL,message TEXT NOT NULL,details TEXT NULL,native_code INTEGER NULL);
CREATE INDEX IF NOT EXISTS ix_error_reports_time ON error_reports(time DESC);
CREATE TABLE IF NOT EXISTS migration_runs(id TEXT PRIMARY KEY,started_at TEXT NOT NULL,completed_at TEXT NULL,legacy_root TEXT NOT NULL,backup_path TEXT NULL,report_path TEXT NULL,status TEXT NOT NULL,error TEXT NULL);
CREATE TABLE IF NOT EXISTS automation_events(id TEXT PRIMARY KEY,time TEXT NOT NULL,kind TEXT NOT NULL,severity TEXT NOT NULL,message TEXT NOT NULL,data_json TEXT NULL);
CREATE INDEX IF NOT EXISTS ix_automation_events_time ON automation_events(time DESC);
CREATE TABLE IF NOT EXISTS save_snapshots(id TEXT PRIMARY KEY,created_at TEXT NOT NULL,reason TEXT NOT NULL,root_path TEXT NOT NULL,save_source TEXT NULL,mod_state_json TEXT NOT NULL,game_build_sha256 TEXT NULL,manifest_json TEXT NOT NULL,success INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_save_snapshots_created ON save_snapshots(created_at DESC);
CREATE TABLE IF NOT EXISTS launch_history(id TEXT PRIMARY KEY,started_at TEXT NOT NULL,ended_at TEXT NULL,mode TEXT NOT NULL,game_build_sha256 TEXT NULL,success INTEGER NOT NULL,exit_code INTEGER NULL,startup_survived INTEGER NOT NULL,state_json TEXT NOT NULL,backup_path TEXT NULL,details TEXT NULL);
CREATE INDEX IF NOT EXISTS ix_launch_history_started ON launch_history(started_at DESC);
CREATE TABLE IF NOT EXISTS mod_trust(mod_id TEXT PRIMARY KEY REFERENCES mods(id) ON DELETE CASCADE,successful_launches INTEGER NOT NULL DEFAULT 0,failed_launches INTEGER NOT NULL DEFAULT 0,rollback_count INTEGER NOT NULL DEFAULT 0,last_success_at TEXT NULL,last_failure_at TEXT NULL);
CREATE TABLE IF NOT EXISTS mod_issue_suspects(
    mod_id TEXT NOT NULL REFERENCES mods(id) ON DELETE CASCADE,
    issue_kind TEXT NOT NULL,
    score INTEGER NOT NULL,
    reason TEXT NOT NULL,
    first_seen TEXT NOT NULL,
    last_seen TEXT NOT NULL,
    failure_count INTEGER NOT NULL DEFAULT 1,
    last_launch_id TEXT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    confirmed INTEGER NOT NULL DEFAULT 0,
    resolved_at TEXT NULL,
    PRIMARY KEY(mod_id,issue_kind)
);
CREATE INDEX IF NOT EXISTS ix_mod_issue_active ON mod_issue_suspects(active,confirmed DESC,score DESC,last_seen DESC);
CREATE TABLE IF NOT EXISTS catalog_sources(
    provider_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    source_kind TEXT NOT NULL,
    health_state TEXT NOT NULL DEFAULT 'Offline',
    last_sync_at TEXT NULL,
    last_error TEXT NULL,
    updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS catalog_items(
    canonical_id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    provider_mod_id TEXT NOT NULL,
    game_id TEXT NOT NULL,
    name TEXT NOT NULL,
    author TEXT NOT NULL,
    summary TEXT NOT NULL,
    description TEXT NOT NULL,
    category TEXT NULL,
    tags_text TEXT NOT NULL,
    source_url TEXT NOT NULL,
    updated_at TEXT NULL,
    mod_json TEXT NOT NULL,
    UNIQUE(provider_id,provider_mod_id)
);
CREATE INDEX IF NOT EXISTS ix_catalog_items_provider_game ON catalog_items(provider_id,game_id);
CREATE INDEX IF NOT EXISTS ix_catalog_items_updated ON catalog_items(updated_at DESC);
CREATE TABLE IF NOT EXISTS catalog_files(
    provider_id TEXT NOT NULL,
    provider_mod_id TEXT NOT NULL,
    provider_file_id TEXT NOT NULL,
    canonical_id TEXT NOT NULL REFERENCES catalog_items(canonical_id) ON DELETE CASCADE,
    category TEXT NOT NULL,
    version TEXT NULL,
    file_name TEXT NOT NULL,
    uploaded_at TEXT NULL,
    file_json TEXT NOT NULL,
    PRIMARY KEY(provider_id,provider_mod_id,provider_file_id)
);
CREATE INDEX IF NOT EXISTS ix_catalog_files_item ON catalog_files(canonical_id);
CREATE TABLE IF NOT EXISTS catalog_provenance(
    canonical_id TEXT PRIMARY KEY REFERENCES catalog_items(canonical_id) ON DELETE CASCADE,
    fetched_at TEXT NOT NULL,
    expires_at TEXT NULL,
    etag TEXT NULL,
    last_modified TEXT NULL,
    source_fingerprint TEXT NULL
);
CREATE TABLE IF NOT EXISTS catalog_sync_state(
    provider_id TEXT NOT NULL,
    game_id TEXT NOT NULL,
    cursor TEXT NULL,
    last_success_at TEXT NULL,
    last_attempt_at TEXT NULL,
    last_error TEXT NULL,
    PRIMARY KEY(provider_id,game_id)
);
CREATE VIRTUAL TABLE IF NOT EXISTS catalog_items_fts USING fts5(
    canonical_id UNINDEXED,
    name,
    author,
    summary,
    description,
    tags,
    category,
    tokenize='unicode61'
);
CREATE TRIGGER IF NOT EXISTS catalog_items_ai AFTER INSERT ON catalog_items BEGIN
    INSERT INTO catalog_items_fts(canonical_id,name,author,summary,description,tags,category)
    VALUES(new.canonical_id,new.name,new.author,new.summary,new.description,new.tags_text,COALESCE(new.category,''));
END;
CREATE TRIGGER IF NOT EXISTS catalog_items_ad AFTER DELETE ON catalog_items BEGIN
    DELETE FROM catalog_items_fts WHERE canonical_id=old.canonical_id;
END;
CREATE TRIGGER IF NOT EXISTS catalog_items_au AFTER UPDATE ON catalog_items BEGIN
    DELETE FROM catalog_items_fts WHERE canonical_id=old.canonical_id;
    INSERT INTO catalog_items_fts(canonical_id,name,author,summary,description,tags,category)
    VALUES(new.canonical_id,new.name,new.author,new.summary,new.description,new.tags_text,COALESCE(new.category,''));
END;
""";
}
