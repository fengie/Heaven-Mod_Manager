# SQLite schema map

Core tables: `mods`, `mod_files`, `blobs`, `original_files`, `deployment_manifest`, `conflict_rules`, `resource_providers`, `mod_families`, `mod_family_members`, `profiles`, `profile_mods`, `profile_rules`, `operations`, `operation_journal`, `external_changes`, `armor_catalog`, `settings`, `diagnostics`, and `migration_runs`.

The schema intentionally stores file content identity separately from mod/path metadata. SHA-256 is authoritative content identity; cached fast hashes/metadata are performance aids only.
