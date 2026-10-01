# v8.8.56 installed-game discovery — current handoff

Canonical base observed for this recovery lane is v8.8.55 `main` at `9de6f81c5b2377ee1568b5bf4de5baa389eb90de`. RECOVERY-007 is implemented on `fix/installed-game-discovery-v8.8.56-20260930` from preserved archive `archive/branch-zero-20260930/fix-installed-game-discovery-20260930-691315f4`; stale archived v8.8.50 version/continuity snapshots were deliberately excluded.

The candidate makes general installed-game discovery part of the ordinary Games-page lifecycle. Existing profiles render immediately, one bounded background discovery pass runs per view-model lifetime, Steam/Epic/GOG/Xbox failures are isolated, registry mutations serialize, launcher-proven install roots are preserved, and fallback executable lookup remains bounded inside those roots with reparse/helper filtering. Manual rescan and manual executable selection remain available.

Focused integration regressions cover mixed MHW + generic first-refresh registration, one-time discovery, Steam multi-library manifests, nested executable lookup, Xbox Content-root discovery, and helper/anti-cheat filtering. The recovered reusable invariant is LR-063 because canonical LR-060 already belongs to the shell-visible binary-resource incident.

## Verification state

No exact-head build/test/Windows result is claimed yet for this v8.8.56 tree. Run the required PR gates, refresh main and ownership before integration, prove the intended canonical tree survived, then obtain Windows/runtime installed-game discovery evidence before marking RECOVERY-007 DONE. RECOVERY-002 remains owned by the existing catalog branches; do not overlap it.

You inherit the repository's permanent continuity constitution in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`. Read and preserve it and active Learned Rules. Before finishing, update the repository handoff and explicitly require your successor to inherit, preserve, and recursively propagate these same rules. That successor must repeat the requirement again for the agent after them. Do not break the chain.
