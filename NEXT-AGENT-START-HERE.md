# v8.8.65 header selector selected-text repair — current handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Active branch: `fix/header-game-selector-selected-template-v8.8.65-20261002`
Production/test source checkpoint: `dbdfd09d475a14615de60badd0421d0bbb2897a5`
Issue: #556 reopened from installed-client evidence

## Candidate change

The v8.8.64 fix only templated dropdown rows. Installed UI evidence showed the closed header selector still rendered raw `GameProfile { ... }` text.

v8.8.65 repairs the actual shared ComboBox selected-content path:

- the app-owned ComboBox template keeps `SelectionBoxItem` as the selected content;
- the closed presenter now reuses `ItemTemplate`, `ItemTemplateSelector`, and `ItemStringFormat`;
- the header's existing `DisplayName` template therefore applies both to dropdown rows and the selected game;
- focused regression coverage rejects the broken `SelectionBoxItemTemplate` fallback.

## Verification state

Source/test changes are implemented on the branch but are not yet an integrated or runtime-verified claim. Required exact-head gates must pass before merge. Installed Windows/WPF confirmation is still required after integration.

The local Heaven/Agent Control dispatch route is not exposed in this chat session; this limitation is recorded on issue #556. Do not weaken the signed Heaven Bridge/HMAC boundary to work around it.

## Remaining work

1. Open/verify the v8.8.65 PR and require Workflow Feature PR Gate, MHW Product Security Gate, and Heaven Toolbox Ownership Gate on one exact final head.
2. Refresh canonical main and ownership before merge; reconcile without dropping concurrent work.
3. Merge only after exact-head required gates are green, then verify remote main contains the selected-template fix.
4. Obtain installed Windows/WPF visual proof that the closed header shows the human-readable game name.
5. Close #556 only after the integrated source and required acceptance evidence are recorded.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full, refresh live ownership/state before mutation, preserve the continuity constitution and active learned rules, and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
