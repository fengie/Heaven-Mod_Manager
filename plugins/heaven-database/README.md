# Heaven Database

Safe local SQLite control: schema inspection, bounded read-only queries, confirmed mutations, integrity checks, and backups.

Read queries run with SQLite `query_only=ON`. Database/backup paths are physically resolved beneath an allowed root. Mutations and backup replacement require explicit confirmation, and cross-database attach/detach-style SQL is blocked.

Validate with `python .\plugins\heaven-database\verify.py`.
