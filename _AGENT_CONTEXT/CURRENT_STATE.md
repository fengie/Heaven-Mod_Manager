# v8.8.56 installed-game discovery — active candidate

Canonical base is v8.8.55 `main` at `9de6f81c5b2377ee1568b5bf4de5baa389eb90de`. RECOVERY-007 is now implemented on `fix/installed-game-discovery-v8.8.56-20260930` from the preserved archive lane without importing its stale v8.8.50 metadata.

The candidate makes general installed-game discovery part of the default Games-page lifecycle: existing profiles render immediately, one background discovery pass runs per view-model lifetime, Steam/Epic/GOG/Xbox sources are isolated, registry writes serialize, and executable fallback remains bounded to proven install roots with helper/reparse filtering. Manual scan/add-game paths remain intact.

Focused integration regressions were recovered for mixed MHW + generic first-refresh discovery, one-time lifecycle behavior, Steam multi-library manifests, nested executable lookup, Xbox Content roots, and helper filtering. The recovered discovery rule is LR-063 because canonical LR-060 is already the shell-visible binary-resource rule.

No exact-head test/build/Windows result is claimed yet. Required next evidence is exact candidate CI/gates, fresh-main reconciliation, post-merge tree proof, and a Windows/runtime discovery smoke. Other active P0 recovery work remains RECOVERY-002 (owned catalog lane) and RECOVERY-004 (unclaimed updater/runtime reconciliation).
