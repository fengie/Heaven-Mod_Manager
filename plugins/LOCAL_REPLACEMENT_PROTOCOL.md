# Local Plugin Replacement Protocol

## Standing directive

When a plugin, connector, or tool that is materially relevant to the current task is **proven unavailable in the current runtime**, do not stop at a fallback or backlog note. In the same execution cycle, preserve the outage evidence in repository state and start a local replacement project.

This protocol applies to missing/removed plugins, usage- or quota-paused plugins, provider outages, unsupported operations, broken integrations, or authorization surfaces that cannot currently be used after the normal supported connection flow. One failed call by itself is not proof of unavailability; the task/plugin preflight must establish current-runtime evidence first.

## Required response to an outage

1. **Prove the outage.** Record the plugin/capability, current task impact, exact runtime evidence, and whether the failure is temporary, quota-related, provider-side, unsupported, or unknown.
2. **Audit existing local capability.** Search `plugins/`, `heaven-bridge/`, active branches/PRs, and `PLUGIN_GAP_BACKLOG.md`. Reuse or extend an existing owner whenever possible.
3. **Create/update the durable plan immediately.** Add or update the backlog entry with capability contract, security boundary, dependencies/reuse, acceptance tests, priority, owner, and project path.
4. **Start the local project immediately.** Create the owning project/scaffold under `plugins/<replacement-name>/` or extend the existing plugin in place. A current-task outage item must not be left as merely `PLANNED` when repository mutation is available; it becomes actively owned/`CLAIMED`.
5. **Make the first concrete implementation move.** At minimum add the project README/plan plus a machine-readable capability/replacement contract or first test/adapter boundary. Do not call a chat-only idea “started.”
6. **Continue the user's immediate task through the safest authorized fallback** when possible. The replacement project removes future dependency; it must not create an artificial blocker for the current task.
7. **Integrate and verify.** Completed replacement work follows the normal main/integration/verification/cleanup rules.

If repository mutation is genuinely impossible, record `BLOCKED` with the exact missing write capability and continue the user task via a safe fallback when one exists.

## Local-first architecture rules

- Prefer a local adapter over reimplementing a mature local primitive.
- Existing Heaven capabilities are building blocks: `heaven-control-plane`, `heaven-file-ops`, `heaven-process-services`, `heaven-desktop`, `heaven-browser`, `heaven-system-ops`, and the existing `heaven-bridge/` transport.
- Never maintain two independent implementations of the same low-level capability without a documented reason.
- A replacement may emulate the useful contract of an unavailable plugin, but must not bypass the original provider's quotas, authentication, OAuth, account permissions, anti-abuse controls, or service-side authorization.
- Secrets stay out of Git, plans, manifests, logs, queue payloads, and replacement metadata.
- Local replacements must expose truthful capability/health state. “Host online” and “specific control transport usable” are separate facts.

## Minimum project contents

Every new local replacement project must contain:

- `README.md` — purpose, scope, architecture, security boundary, local dependencies, validation, ownership.
- `PLAN.md` — upstream capability map, reuse decisions, milestones, acceptance tests, rollout/compatibility plan.
- a machine-readable manifest or equivalent contract identifying the unavailable upstream capability and the local providers/adapters intended to replace it.
- tests or a first executable adapter boundary as soon as the project moves beyond initial scaffold.

## Duplicate handling

If a local capability already covers the unavailable plugin:

- do **not** create a duplicate implementation;
- create only the compatibility/routing layer or parity project that is actually missing;
- update discovery/docs so future agents route to the local capability automatically;
- mark the backlog entry `DONE` or `SUPERSEDED` once the local path is verified on remote `main`.

## Current example: Remote Desktop Commander

The 2026-09-29 Remote Desktop Commander usage pause is handled by `plugins/remote-desktop-commander-local/`. That project is a compatibility/parity layer over existing Heaven Local Bridge and Heaven plugin primitives, not a second desktop-control stack.
