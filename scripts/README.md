# Scripts

Automation is grouped by purpose instead of kept in one flat directory.

- `build/` — release builds and source-handoff packaging.
- `release/` — release verification, updater publication, and updater release policy.
- `testing/` — test runners, policy tests, continuity checks, and verification-cache tests.
- `diagnostics/` — shared debug logging, runtime capture, and startup-performance measurement.
- `benchmarks/` — benchmark launch automation.

Repository-root `.bat` files remain stable user-facing entry points and call the canonical scripts in these folders.
