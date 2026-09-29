# Heaven File Ops

Exposes proven Heaven Bridge filesystem actions that were not yet available as a reusable package: metadata/listing, copy, move, delete, and bounded binary transfer.

All mutations require explicit `confirm=True`; paths reject traversal segments and NULs. The bridge remains responsible for allowlisted roots and protected-root delete checks.

Validate with `python .\plugins\heaven-file-ops\verify.py`.
