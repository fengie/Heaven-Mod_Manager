# v8.8.59 Settings candidate — MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

The v8.8.59 candidate on `feat/settings-v8.8.59-20261001` adds a persistent in-app Settings surface. It keeps current defaults for existing users, lets automatic program updates be disabled while retaining explicit manual checks, and adds working UI-motion, last-tab, Apply/discard confirmation, and background metadata-refresh preferences.

## Current MHW product work

- **RECOVERY-003 / P1:** implemented in the v8.8.59 Settings candidate; exact-head gates and integration remain.
- **RECOVERY-004 / P0:** updater/runtime hardening remains a separate divergent lane; it must refresh newer main and preserve unique semantics instead of overwriting Settings work.
- **RECOVERY-002 / P0:** in-app catalog browser recovery remains active on its existing product lane.
- **RECOVERY-007 / P0:** source is integrated; representative Windows/runtime discovery proof remains before DONE.
- **RECOVERY-005 / P1:** dark ComboBox source integration is complete on main; retain any separately recorded installed Windows/WPF runtime acceptance gap until its evidence closes it.

## Verification boundary

The Settings candidate has not yet earned exact-head verification evidence. Do not call it integrated or DONE until required GitHub/Windows gates pass on its final source head and current `main` is rechecked immediately before integration.

## Coordination / execution note

The candidate was reconciled with main `a2e4dbadc6353bd1c1dccd77d0c89779d39b0b24` using a non-force merge commit, preserving the RECOVERY-005 continuity updates that landed during this task.

The required local Heaven offload route was not exposed in this ChatGPT session, so this bounded lane was implemented through the connected GitHub repository instead of a local Heaven worker. Preserve that limitation in the successor handoff; do not treat it as local runtime proof.

Global Agent Control/Heaven/plugin work remains owned by `fengie/heaven-toolbox` and must not be recreated in MHW.
