# Heaven State Store

Durable local artifacts, optimistic checkpoints, and machine-health history for long-running agent workflows.

Artifacts are content-addressed by SHA-256 and metadata is stored in SQLite. Checkpoints use compare-and-swap revisions so competing agents cannot silently overwrite each other. Credential-bearing metadata is explicitly refused; credential values should remain in the existing named-handle/host-side secret path rather than this store.

Validate with `python .\plugins\heaven-state-store\verify.py`.
