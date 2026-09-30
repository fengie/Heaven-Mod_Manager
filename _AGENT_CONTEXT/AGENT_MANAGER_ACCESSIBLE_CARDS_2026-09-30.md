# Agent Manager accessible card semantics — 2026-09-30

## Objective

Keep managed and federated Agent Control cards inspectable by mouse and keyboard without hiding their status, task details, or child action controls from assistive technology.

## Canonical source and candidate

- Canonical repository: `fengie/mhw-mods`
- Base: canonical `main` at `5898e7200a525408ce3611f044ee4586fcccbb9a` (v8.8.27 / Agent Control v0.6.6)
- Candidate branch: `fix/agent-control-card-accessibility-20260930`
- Implementation checkpoint: `683c8f0fa1a28e700405259e4fdf746922680de5`
- Behavioral regression checkpoint: `0de7e8684f67a8e57977f93b00325b69829ff32c`
- Root release target: v8.8.28; Agent Control runtime/root plugin/nested plugin target: v0.6.7.

## Defect and fix

The v0.6.6 card-inspection feature attached `role="button"` to managed/federated `<article>` containers with status/task text and nested managed action buttons. An independent review found that button role semantics can flatten descendants in the accessibility tree. The correction removes role overrides, retains the named, keyboard-focusable native article, and leaves click/Enter/Space inspection and nested-action event filtering intact.

The focused UI tests check both cards retain native article markup, an accessible name, focusability, visible state/task content, and native child action buttons. Executable event-handler harnesses cover managed/federated click, Enter, Space, nested-action isolation, and external federated detail expansion.

## Verification and limits

- Focused `node --test tools/agent-control/test/operator-ui-cli.test.mjs`: 6/6 passed at checkpoint `0de7e868`.
- Full `node --test tools/agent-control/test/*.test.mjs`: 228/228 passed at checkpoint `0de7e868`.
- All package syntax checks from `tools/agent-control/package.json` passed at checkpoint `0de7e868` (`node --check` for server, CLI, and 14 library modules).
- `git diff --check`: passed at implementation checkpoint.
- These source and test checks apply to the behavior checkpoint; this branch's remaining changes are release and continuity documentation/version metadata. Hosted exact-head checks are still pending.
- Read-only Brave/CUA accessibility-tree inspection of `http://127.0.0.1:7331/` confirmed managed cards appear as containers with their own content and separate `View log`, `Deploy reviewer`, and `Copy branch` buttons. This is a live heaven2 dashboard observation, not a screen-reader audit of both card types or an input smoke.
- Keyboard/click smoke was deliberately skipped because the live page showed **START PERPETUAL SWARM** active, 0 live agents, repeated failed retries, `swarm.tail-recovery-exhausted` with 5 unresolved recovery roots, `autopilot.safety-gate`, and `heartbeat-auth-invalid`. Interacting could change a running controller. The safe next step is to stabilize/authorize the live runtime, then perform card keyboard and nested-action smoke.
- The `agent-browser` CLI is absent, but Computer Use CUA is callable for read-only local accessibility inspection. Hosted exact-head checks remain pending.
- Heaven Local Bridge instructions are available, but this session has no callable bridge operation. The current-session tool availability finding is captured in PG-001.

## Durable prevention

- `_AGENT_CONTEXT/BUG_PRECEDENTS.md` records the card descendant semantics defect.
- `_AGENT_CONTEXT/LEARNED_RULES.md` adds LR-051.
- `tools/agent-control/test/operator-ui-cli.test.mjs` protects both managed and federated markup.

## Next steps — Agent Manager P0 stays active

1. Run all package syntax checks and `node --test tools/agent-control/test/*.test.mjs` on the final candidate; use exact SHA in verification records.
2. Obtain exact-head hosted checks.
3. On heaven2, inspect both card types in a browser accessibility tree/screen reader. Verify article role/name, status/task details, and native child buttons.
4. Verify card click and Enter/Space inspect correctly, and nested buttons operate without double-triggering inspection.
5. Re-run the v8.8.26 retry-exhausted retirement behavior and safety cases on the current controller.
6. Continue the open remote job-stop and heaven2→heaven1 START SWARM/perpetual dispatch, recovery, and stop gates.
7. Keep Agent Manager P0 active until every gate has exact-head evidence.

## Successor startup

Read root `AGENTS.md`, current `NEXT_STEPS.md`, `CURRENT_REVISION.json`, `CURRENT_STATE.md`, LR-051, and the Agent Manager P0 completion criteria. Re-fetch canonical `origin/main` and inspect open PR/branch ownership before proceeding. Do not claim the browser accessibility tree was verified based on source assertions alone.
