# v7 -> v8 migration

The v7 source defines its durable state as `State\V2\state.json`, immutable hash blobs, histories, explicit file winners, original/baseline hashes, profiles, mod-pair relations and shared-resource providers. v8 imports those semantics rather than interpreting the folder names from scratch.

## Algorithm

1. If the v8 database already contains mods, migration is not rerun.
2. Validate legacy schema `2`.
3. Create `State\NextMigrationBackup\<timestamp>` using hardlinks where Windows permits and byte copies otherwise. This captures state/history metadata without altering v7.
4. Import enabled v7 mods and exact captured file hashes.
5. Catalog disabled local `Mods` folders without eagerly hashing them.
6. Link/copy every referenced immutable legacy blob into the v8 CAS.
7. Import original-file baselines, exact winners, overlay/incompatible pair relations, resource-provider pins and profile metadata.
8. Reconstruct the deployment manifest from v7 `expected` hashes, preferring a matching explicit/active provider.
9. Re-hash every referenced v8 blob with SHA-256.
10. Verify imported enabled count equals the v7 `order` count.
11. Write a migration report and mark the migration complete.

Any exception leaves `State\V2` and `nativePC` untouched. The v8 database records the failed run for diagnostics.
