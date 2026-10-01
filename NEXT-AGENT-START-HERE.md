# v8.8.59 persistent Settings — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Candidate branch: `feat/settings-v8.8.59-20261001`

## Implemented

The Settings candidate adds a dedicated left-navigation tab with six persistent preferences: automatic program updates, UI animations, remember-last-tab, confirmation before Apply, confirmation before discarding staged changes, and periodic background metadata refresh.

Settings are stored under the isolated `State/Next` root with atomic JSON replacement and safe-default fallback. Automatic updates now honor the preference at startup, periodic checks, staging, and the final safe handoff; the existing manual **Check for updates now** action remains available and carries explicit install intent even when automatic updates are off.

## Verification state

Source checkpoint: `b62c099f2514281a1d1d0b350cad2a090e0554d2`. No exact-head verification is claimed yet for the complete versioned candidate. Required GitHub/Windows gates must pass on the final branch head before integration. Refresh `main` immediately before merge because other product lanes are active.

## Coordination / risks

- RECOVERY-004 is a separate updater/runtime hardening lane. Preserve its unique work and reconcile it onto newer main after this Settings lane; do not overwrite it.
- Catalog recovery remains separate.
- RECOVERY-007 retains its representative Windows/runtime proof gap.
- The local Heaven/Agent Control route required by global training was not exposed in this ChatGPT session, so no local Heaven execution or installed-client UI proof is claimed.
- Global Agent Control/Heaven/plugin work belongs in `fengie/heaven-toolbox`, not this repo.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Preserve the permanent continuity constitution, all unresolved evidence gaps, and this explicit successor-propagation obligation. **Do not break the chain.**
