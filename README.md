# v8.8.54 Universal Mod Manager

## v8.8.54 — single-owner agent coordination leases

Agent Control now treats mutable work boundaries as case-insensitive ownership identities and rejects duplicate routing assignments before dispatch. Counted deploys also refuse to split one explicit mutable boundary across multiple workers instead of inventing synthetic subleases. This closes a race where separately named-but-equivalent boundaries could let agents edit the same mutable surface concurrently. Agent Control advances to **v0.6.25**.

## v8.8.53 — Agent Work Reports dashboard

Adds a standalone, loopback-only Agent Work Reports app that turns Agent Control state plus explicit agent checkpoints into a glanceable overall-work list, expandable agent/task cards, recent activity, and on-demand logs. The reporting surface stays read-only with respect to Agent Control mutations and exposes a stable `agent-work-reports/view/v1` model for later Agent Manager integration.

## v8.8.52 — compact Agent Manager overview and motion polish

Agent Manager now keeps high-volume operational surfaces collapsed by default in a responsive two-column overview, expands one selected surface full-width with bounded internal scrolling, opens Inspector automatically for inspected agents, and summarizes notification severity at a glance. The same patch adds bounded transform/opacity micro-interactions plus opt-in generated UI sounds that respect reduced-motion and hidden-page safeguards. Agent Control advances to **v0.6.24**.


## v8.8.51 — repaired Windows shortcut icon

Fixes the corrupted desktop shortcut icon by removing the malformed 48×48 PNG frame from the shipped ICO; Windows now scales the intact 32×32 artwork instead of decoding corrupt bytes. New integration coverage validates every committed icon frame for ICO bounds, PNG dimensions, chunk CRCs, and exact IEND termination so malformed shell assets fail verification before release.

## v8.8.50 — consolidate training without weakening safeguards

Compact indexed training and current handoffs replace duplicated prose. Pre-response training, full constitution startup reading, recursive propagation, raw-byte budgets and exact-source evidence remain enforced. Superseded PR #525 is preserved inside PR #528 and reconciled with v8.8.49 main without overwriting the merged UI fixes. Workflow Feature CI now cancels only superseded runs for the same PR/ref, preventing unrelated feature branches or stale reruns from destroying exact-head integration evidence.



## v8.8.49 — UI visibility and Mods empty-state refresh

Fixes two current UI regressions without changing feature scope: native WPF ComboBoxes now use matched system control/background text brushes so selected values remain readable, and the Mods empty-state overlay now refreshes when the installed-mod collection count changes instead of continuing to cover populated content. Regression guards pin both behaviors.

## v8.8.48 - automatic Mods page stretch

Reconciles existing PR522 with current main, removes the redundant ancestor ActualWidth dependency from the Mods root and strengthens the actual wrapper contract using parsed XAML. Existing styles, margins, library and toolbar behavior stay intact. Styled probes resize both old/new layouts correctly; the reported populated-window collapse remains a separate verification gap. No new features.

## v8.8.47 — exact inspector identity and integration repair

Agent Control v0.6.22 preserves the requested federated record even when it links to a managed agent. Executable regression coverage catches the prior redirect. Repairs concurrent handoff version drift and compacts current state within the existing manager training budget while preserving the full acceptance contract in indexed history. Existing issues/PRs take priority; new features remain frozen.

## v8.8.46 — cross-repository agent training bootstrap

All repository agents now inherit the MHW repository as the canonical training source before they touch any target repository. The global trainer, shared swarm/manager contracts, and every standalone implementation/integration/recovery/release/review/test/support role explicitly require a fresh `fengie/mhw-mods` `main` bootstrap first, followed by the target repository's own local rules and state. This makes the user's MHW governance/training baseline portable across future repositories without relying on stale chat context.

## v8.8.45 — maintenance consolidation and junior handoff

Consolidates verified work into bounded junior bug-fix assignments, preserves unmerged branch provenance and separates real installed-client proof from release CI. New features are frozen for this maintenance cycle; existing runtime behavior and plugin identities are unchanged. See [`junior maintenance handoff`](_AGENT_CONTEXT/HANDOFFS/junior-maintenance-2026-09-30.md).

## v8.8.44 — Windows-safe agent ownership state

Agent Control v0.6.21 accepts Windows UTF-8 BOM state files without falling back to an older backup and losing newer ownership. Primary, backup, legacy migration and backup validation share one strict decoder; malformed content still fails closed, writes stay BOM-free, and interrupted-write recovery preserves uncertain work.

## v8.8.43 — bounded indexed-context navigation

Agent Control v0.6.20 can jump directly to relevant indexed repository context with hash-checked literal search or Markdown-heading lookup instead of paginating blindly. Navigation stays inside the existing whitelist and exact SHA-256 boundary, returns line numbers for precise follow-up reads, caps queries/results/output, reports truncation explicitly, and keeps the no-persistent-cache startup design. The 8 KiB limit covers actual emitted JSON, including escaping and its newline, for both navigation and pagination.

## v8.8.42 — current CI evidence and fail-closed release gates

Heaven Workflows v0.4.0 adds a bounded, reader-injected GitHub Actions evidence adapter for exact repository/source/event/branch checks. Release decisions now reject ambiguous histories and running states instead of letting an older success mask a failed result. Snapshots disclose partial queries, expire after two minutes and are revalidated at publication authorization. Authentication stays with the existing authorized host reader; no new token store or publication path is introduced.

## v8.8.41 — bounded startup and scalable context retrieval

Agent Control v0.6.19 provides a small live repository bootstrap through the CLI/API, expiring exact-source evidence, bounded ownership, and SHA-256-checked context pagination. Mandatory startup text drops from roughly 120 KB to 40 KB; detailed policies and prior revision history remain preserved in indexed references. Core-size budgets prevent silent growth, cached refs disclose their freshness, and worker deployment reuses one successful remote refresh.

## v8.8.40 — efficient agent bootstrap

Agent startup is now progressive instead of repeatedly ingesting the full historical repository corpus. Agent Control v0.6.18 gives every worker a small full-read core plus a hash-verified indexed context manifest, requires targeted expansion of task-relevant history and precedents, paginates truncated reads, and routes around missing preferred CLIs, unavailable local checkouts, or single-path network failures through authorized GitHub, Heaven, or CI alternatives before allowing a blocker claim. Senior/premium agents are explicitly optimized for high-value diagnosis, review, integration, and verification decisions rather than mechanical context gathering. This same patch also recovers the already-isolated #507 repository-gate repairs onto fresh main lineage—strict analyzer cleanup, verification tracing, ambiguity/update behavior regressions, feed/GitLab normalization, and matching tests—without importing #507's stale version/continuity metadata.




## v8.8.39 — Heaven Local Bridge primary reliability

Heaven Local Bridge is now the explicit primary remote-control path for both `heaven2` and `heaven`; Remote Desktop Commander is retained as a paired, on-demand fallback rather than an always-on competing controller. Worker v8 and Agent Control v0.6.17 share a machine-local `HeavenBridge/auth/allow-repo-acl-only` marker for the private-GitHub-relay compatibility mode, so restart/recovery does not depend on fragile process environment variables and terminal-agent retirement can use the same relay mode as ChatGPT.

Bridge recovery remains three-layered (worker + watchdog + SYSTEM sentinel), canonical runtime comes from `main`, Python runtime caches are ignored, and the operator policy is reproducible through `heaven-bridge/Set-PrimaryControlMode.ps1`. The Heaven Local Bridge plugin advances to **v0.8.3**.

## v8.8.38 — Superseded retry-parent retirement

Agent Manager no longer lets failed `RETRY DISPATCHED` parent records accumulate forever after their replacement has taken ownership. Those terminal superseded parents now pass through the same fail-closed process, branch/worktree, and provenance checks as other terminal workers and are archived out of the live/tracked registry when safe. Unresolved recovery or durable work remains visible instead of being hidden. Agent Control advances to **v0.6.16**.

## v8.8.37 — Agent registry lifecycle closure

Agent Manager now treats the live registry as current presence instead of an audit log. Ordinary terminal managed agents can retire only after process ownership, branch provenance, worktree cleanliness, and task state are safe; unresolved recovery, candidate, cleanup, dirty, divergent, or otherwise uncertain work remains visible for attention instead of being silently discarded.

External federated terminal records and records beyond the disconnected timeout are moved out of current presence into durable registry history. Every correlated provider/source identity receives its own retirement tombstone, stale replay remains suppressed until genuinely newer live heartbeat evidence appears, and historical rows no longer inflate the live-registry total. The dashboard exposes the retained forensic archive in a dedicated **Registry history** surface. Agent Control advances to **v0.6.15**.

## v8.8.36 — Agent Manager notification + stable inspector closure

The Agent Manager dashboard now renders backend notifications instead of silently dropping them. Only the supported `inspect-agent` and `inspect-federation` action types become actionable UI controls; other notification types remain visible and informational.

Managed and federated records now share stable ID-based inspection helpers and a persistent Inspector surface. Selection survives normal snapshot refresh while the record stays live, and retirement/missing races produce an explicit persistent “no longer live / retired” state instead of disappearing or null-dereferencing. Agent cards keep body-click convenience, but keyboard semantics now live on explicit real **Inspect** buttons; dynamic agent IDs are carried through bound `data-*` controls instead of inline JavaScript handlers. Terminal/dead records do not expose invalid Stop actions. Agent Control advances to **v0.6.14**.



## v8.8.35 — Agent Control canonical runtime freshness

Startup restore, watchdog restarts, and the manual launcher now canonicalize Agent Control source before execution. A clean local `main` may fast-forward to `origin/main`; dirty, detached, non-main, ahead, or diverged checkouts fail closed without destructive reset/clean behavior.

The controller publishes its exact repository SHA and Agent Control version through health/process identity. A stale listener is replaced only after persisted PID, exact server-path, and Node-process ownership proof; unknown listeners are preserved. Runtime Git probing uses the shared hidden-process wrapper, and focused Windows fixtures exercise clean fast-forward plus dirty/non-main/detached/ahead/diverged preservation. Agent Control advances to **v0.6.13**.



## v8.8.34 — Agent Control durable stop proof

Operator Stop and provider-capacity auto-termination now share one fail-closed termination boundary. Heaven-backed workers require a durable remote job id and an authoritative processed-terminal Bridge state before their local wrapper can release ownership; ambiguous `not_running`/`unknown` state, missing identity, or failed status authority leaves the worker blocked with its lease preserved.

A persisted proof-pending marker is written before termination begins. If the local wrapper exits first, its exit evidence is recorded without final status, completion evidence, task closure, or lease release; finalization happens only after remote terminal proof and local exit proof are both present. Non-Heaven workers keep the same local stop behavior without requiring remote proof. Focused regressions pin provider fallback, missing-id failure, child-exit ordering, lease preservation, and finalization ordering. Agent Control advances to **v0.6.12**.

## v8.8.33 — Agent Control lightweight health

Controller liveness no longer depends on full dashboard/repository work. `GET /api/status` now reads persisted local control state and returns a lightweight health snapshot without repository scans, branch-divergence work, or Heaven Bridge synchronization, so the existing three-second startup/watchdog probe cannot be delayed by slow Git or relay inspection.

Full dashboard snapshots also reuse one Heaven Bridge assessment across worker and federation views instead of inspecting the relay twice. Dispatch-time trust and bridge authorization remain unchanged and continue to revalidate on real execution. Regression coverage pins the lightweight status contract, shared bridge assessment, and startup/watchdog probe path. Agent Control advances to **v0.6.11**.

## v8.8.32 — Agent Control retirement identity fail-closed closure

The v8.8.31 durable remote-stop proof is now enforced for every Heaven-backed persisted row, including legacy/migrated rows whose provider identity is stored in runtimeProvider or provider. A Heaven worker with a missing or blank durable remote job id can no longer bypass proof; retirement blocks, preserves live ownership/history, and records the retry-blocked condition.

A real-server regression pins the missing-ID path before registry deletion, and migration coverage preserves existing retired-source tombstones and their retiredAt comparison boundary. Agent Control advances to **v0.6.10** while preserving v8.8.31 terminal-state and heartbeat-monotonicity behavior.


## v8.8.31 — Agent Control durable retirement safety

Retry-exhausted Heaven workers now remain visible until their durable remote job is authoritatively proven terminal. Cancellation alone, `not_running`, `unknown`, queued/unclaimed state, status lookup failure, or any other ambiguous state is not enough; Agent Control retires the live-registry entry only after an explicit processed terminal state. If a job races into `running`, cancellation is reasserted and status polling continues until terminal proof exists.

Retired federated identities also require a raw live heartbeat strictly newer than `retiredAt` before reactivation. Older, equal, missing, invalid, or terminal replay remains suppressed before federation normalization can synthesize freshness. This preserves the v8.8.30 card-accessibility closure and advances Agent Control/runtime/plugin identity to **v0.6.9**.


## v8.8.30 — Agent Control card accessibility closure

Agent Manager cards keep the existing click, Enter, and Space inspection behavior while restoring normal article semantics for assistive technology. Managed and federated cards no longer advertise the entire status/task container as a single button, and nested controls remain independent so View log, Stop, Deploy reviewer, Copy branch, and other actions do not double-trigger card inspection.

Executable interaction coverage now exercises managed/federated card activation, keyboard handling, nested-control suppression, and federated detail expansion. This integrates the clean accessibility follow-up from PR #484 as its own visible patch and advances Agent Control/runtime plugin identity to **v0.6.8**.


## v8.8.29 — Handoff visible-progress validator hardening

The continuity gate now validates the **dedicated** `### Mandatory visible-progress versioning` section in `AGENTS.md` instead of accepting unrelated mentions of “README” and “patch” elsewhere in the file. The section must explicitly require the root README update, `VERSION.txt` patch advancement, `CHANGELOG.md`, and same-change-set coupling.

This closes the broad Workflow Feature gate escape where the negative fixture could delete the entire visible-progress rule and still pass. The existing adversarial fixture now directly protects the section-scoped governance contract.

## v8.8.28 — Agent Control Heaven relay execution repair

## v8.8.28 — Agent Control Heaven relay execution repair

Agent Control now uses the same documented Heaven relay checkout resolver for **real job submission/result waiting** that it already uses for provider health. With `~/HeavenBridgeRepo` installed, heaven2 no longer needs a separately injected `AGENT_CONTROL_HEAVEN_RELAY_DIR` just to dispatch a worker to heaven1.

This closes the confirmed “click creates a worker, then it immediately dies” chain: observed main and manager workers both exited code 1 because execution bypassed relay discovery and threw on the absent environment variable. The v8.8.26 retry-exhausted retirement remains intact, so terminal failed workers are terminated/cleaned where ownership is provable and removed from managed/federated live registries instead of accumulating as **failed · RETRY EXHAUSTED** cards. The v8.8.27 agent-card inspection work is preserved unchanged. Agent Control advances to **v0.6.7** with positive/override/fail-closed relay-resolution regression coverage.


## v8.8.27 — Agent Manager card inspection

The v8.8.26 lifecycle fix removes **failed · RETRY EXHAUSTED** workers from the live registry once safe retirement is proven. This follow-up closes the separate operator bug that made the remaining live agent cards look interactive while clicks on the card body did nothing.

Managed and federated agent cards are now inspectable by mouse and keyboard. Clicking a managed card opens its log; clicking a federated card focuses its linked managed worker when one exists or shows the federated session details otherwise. Nested actions such as **View log**, **Stop**, **Deploy reviewer**, and **Copy branch** remain independent and do not double-trigger the card. The dashboard regression parses the emitted inline JavaScript and pins the mouse/keyboard interaction contract. Agent Control advances to **v0.6.6**.

## v8.8.26 — Agent Control exhausted-agent registry retirement

Agent Control no longer leaves **failed · RETRY EXHAUSTED** workers sitting in the live Agent Manager registry. Bounded no-work recovery now ends with an explicit retirement lifecycle: controller-owned live processes are terminated with proof, clean worktrees and leases are released, the dead logical worker is removed from managed/federated live state, and durable task/event/tombstone history remains available without a dead clickable card.

The registry also rejects replayed terminal observations from already-retired federated sessions, preventing dead sessions from reappearing on the next heartbeat. A genuine live heartbeat clears the tombstone and re-admits the recovered source. Safety stays fail-closed: uncertain PID ownership, dirty worktrees, or committed branch divergence blocks retirement rather than killing or deleting evidence. Agent Control advances to **v0.6.5** with regression coverage for retirement, anti-resurrection sync, tombstone reactivation, and state migration.

## v8.8.25 — Agent Control operator-markup + plugin identity repair

Agent Control fixes two escaped operator/release-contract bugs. **Copy branch** no longer embeds a JSON-quoted branch name directly inside a double-quoted inline handler; branch values are URI-encoded before interpolation and decoded only at invocation, so normal branch names cannot break the generated action markup.

The private plugin package is also identity-consistent again: the runtime package, root ChatGPT plugin manifest, and nested Codex plugin manifest now all report **v0.6.4**, with a regression that rejects future nested-manifest drift. Focused dashboard regressions pin the safe Copy branch encoding and reject the former raw JSON.stringify(...) handler pattern.

## v8.8.24 — Agent Manager operator actions

Agent Manager now exposes the decisions its backend was already computing instead of hiding them from the operator. The dashboard renders prioritized recovery/review/capacity/dispatch recommendations and turns supported recommendations into explicit actions, including review workflow launch, one-click swarm continuation, and worker inspection.

Operator actions now match authoritative server state more closely: only completed managed agents offer **Deploy reviewer**, so terminal failure/capacity/recovery states no longer advertise an invalid review action. **Resume** becomes **Clear emergency stop + resume** while the emergency-stop latch is active and requires explicit confirmation before clearing it.

Agent Control advances to **v0.6.3**. Regression guards pin recommendation visibility/action routing, state-valid reviewer affordances, and emergency-stop recovery semantics; LR-047 and the bug-precedent ledger make this operator/server action contract durable. The active P0 lock remains in force until its exact-head control-plane gates are proven.

## v8.8.23 — Agent Manager runtime reliability

Agent Manager's core operator paths are the immediate priority. **START SWARM** now recovers a paused perpetual run instead of falsely saying it is already running while no work advances; it also rejects an active non-perpetual autopilot with an explicit conflict, validates the objective before clearing control/safety friction, and reports a distinct resumed state in the dashboard.

The dashboard now matches the server's authoritative managed lifecycle: reserved, starting, running, waiting, blocked, stale, and stopping workers all expose **Stop**. Its four-second polling is serialized and sequence-checked so an older slow snapshot cannot overwrite a newer sync result or falsely mark the manager offline, and periodic machine-pool renders preserve the operator-selected worker target instead of resetting it to Auto.

Agent Control advances to **v0.5.11** with regression guards for one-click resume, active-state controls, stale-response rejection, selection persistence, and inline JavaScript parseability. LR-045 and the bug-precedent ledger make polling/operator-intent preservation a permanent prevention rule. Fresh exact-head Agent Control/Windows/runtime verification is still required before release-ready claims are transferred to this patch.


### Agent Manager P0 lock

Agent Manager / Agent Control is also the repository's explicit P0 engineering priority. While the continuity marker remains active, autonomous implementation and next-cycle planning stay on Agent Manager functionality, reliability, orchestration, observability, routing, recovery, startup persistence, and exact verification instead of drifting into unrelated product work.

The Agent Control runtime package and private ChatGPT plugin now share version **0.6.3**, and stable ChatGPT session registration/heartbeats are the default first observability step when a real stable identity is available. The P0 lock remains active until exact-head checks plus heaven2 controller and heaven1 worker-bridge smoke are proven together.

## v8.8.22 — Strict analyzer repair

This patch closes a warnings-as-errors break in the catalog/Core verification path. Six production analyzer failures in installed-origin checking, Thunderstore normalization, generic family inference, and Mod DB feed caching were repaired without changing intended runtime behavior; four matching test-project analyzer failures were also fixed so the strict test assembly can compile and execute again.

The repair also records the failure mode as a prevention precedent: diagnose the first compiler/analyzer diagnostic before chasing downstream missing-assembly noise, and require exact-candidate analyzer closure across both the affected production project and its test project. The canonical tree contains the production fix lineage through `cde589f2330f3e10e92b860ffe16dcdb4fc0d9c2` and the four test repairs merged by PR #437.

## v8.8.21 — Visible progression + safer overrides

This patch makes repository progress intentionally visible. Every completed meaningful change set must update this README with a concise versioned summary and advance the patch version in the same change set, so the top of the repository shows what changed and how the project is moving.

Current main also carries the override/dependency safety work previously tracked as unreleased: overlay precedence must be valid and acyclic, non-blocking conflicts must name a real winner, ambiguous texture replacements fail closed instead of being guessed from priority, MHW structural sibling assets are treated as atomic bundles, and requirements are revalidated before Preview, Apply, modded launch, and last-known-good restore.

The handoff validator now checks that `VERSION.txt`, `Directory.Build.props`, this README, `CHANGELOG.md`, and continuity metadata agree on the current version, and that the mandatory README + patch-progress rule remains present in repository governance. One patch increment represents one integrated meaningful change set; verification/evidence-only persistence or release publication that only attests that same change set stays on the same patch to avoid a recursive version-bump loop.

## v8.8.20 — One-click Auto Populate

The same v8.8.20 release also adds short accessibility-aware page/overlay/button transitions. Motion is transform/opacity-only, respects the Windows client-area animation preference, and avoids layout churn on large virtualized mod lists.

The Mods page now includes **Auto Populate**, which builds and applies a deterministic maximal conflict-free setup from the installed library. It preserves existing enabled choices first, recursively brings in required packages, main/base family members, explicit required files and textures, native plugin-loader packages, and pinned resource providers, then validates the whole proposed closure through the same deployment conflict engine used by normal Apply.

Required packages and texture providers are not exempt from conflict checks: if the complete dependency/resource closure cannot coexist safely, that candidate is skipped instead of guessed. Independent texture replacers remain pick-one conflicts, missing/invalid requirements fail closed, and the resulting setup is applied automatically so **Launch Game** is ready immediately afterward.

## v8.8.19 — Auto Modder foundation

Auto Modder is now an implemented engine foundation rather than a planning-only feature. Core includes the v1 recipe/domain contracts, strict JSON parsing and semantic validation, bounded input/property references, adapter registration with capability and version negotiation, deterministic typed patch planning, generated provenance-manifest writing, and a manager-owned build sandbox with output-count/byte budgets and fail-closed path containment.

This release does not yet expose the final Auto Modder WPF workspace or a real Monster Hunter: World binary-format adapter. Those remain the next vertical slices; recipes still cannot execute arbitrary code, launch processes, access the network, or bypass the normal library/deployment/Undo pipeline.

## v8.8.18 — maximum windowed Mods workspace

The Mods page now uses nearly all available windowed client space: the outer padding is reduced, header/status/actions are consolidated, Filters and Bulk Actions stay in one compact strip, and pending-change controls no longer consume an extra row. The structural regression keeps the library from shrinking back to the earlier layout.

## v8.8.17 — reliable first-launch mod discovery

The manager now preserves its canonical data root across automatic-update restarts instead of relying on inherited updater process state. The first launch after updating therefore reads the same populated `Mods` and `State` directories as a normal shortcut launch, so reopening the application should no longer be required just to make installed mods appear.

Release-layout launches also recover the manager home from the project root when no explicit manager-home variable is present, and the resolved path is exported for subsequent child processes. Regression coverage pins both release-layout fallback and updater handoff behavior.

## v8.8.16 — larger mod library workspace

The Mods page now gives the library substantially more vertical room on normal desktop widths. Filters and bulk actions share one responsive toolbar row when space allows, the pending-change controls sit beside their summary instead of consuming another full row, and the surrounding vertical margins are tighter. Narrow windows can still wrap the toolbar naturally, while the mod rows and existing commands keep their current behavior.

## v8.8.15 — true visual overhaul

The application shell and Dashboard now have a genuinely different information architecture rather than another wording-only pass. The old two-row command strip and dominant 2×2 stat-card grid are replaced by a single app bar, a current-setup hero with the next actions directly attached, a compact metric strip, and separate Setup Details / Quick Actions areas.

The shared WPF visual system now uses deeper navy surfaces, teal interaction accents, larger-radius cards, clearer selected navigation, and reusable action tiles across the app. Deployment, updater, conflict, rollback, filesystem, and diagnostics behavior is unchanged; the redesign reuses the existing commands and bindings.

Runtime diagnostics now also report the executing build identity instead of stale hard-coded v8.3.0/v8.8.6 labels in support bundles, startup trace records, and structured logs.

## v8.8.14 — fail-closed Smart Inbox rollback

Smart Inbox now treats source archival and published-package rollback as one atomic outcome. If moving the original Inbox item fails, the manager rolls back the newly published package; if that rollback itself cannot complete, the run fails closed instead of reporting a harmless skip while leaving both a live package and retryable source behind. A deterministic Windows regression forces rollback deletion failure and verifies the inconsistency is surfaced.


## v8.8.13 — repeatable recipe family restore

Restoring family relationships from the same portable recipe is now idempotent. After the first explicit restore creates the manager-owned local family, repeating that same restore recognizes the complete role-consistent imported family and returns a no-op instead of incorrectly reporting a conflicting local family. Mixed, manual, partial, or role-mismatched local families still fail closed for review.


## v8.8.12 — import publication isolation

Manual archives, Smart Inbox packages, and FOMOD installers now build in a manager-owned workspace outside the catalog-visible `Mods` directory. Failed, canceled, or interrupted staging can no longer become a normal mod merely because a partial folder exists; a package becomes discoverable only after validation/normalization completes and one final same-volume directory move publishes it.

The same boundary now covers FOMOD preparation and selected-file installation. Best-effort cleanup remains subordinate to the original failure, and regression tests force partial archive failures before catalog refresh to prove incomplete work stays invisible.

## v8.8.11 — first-time user UX overhaul

The normal interface now prioritizes the path most users actually need: **Install Mod → Apply Mod Changes → Launch Game**. Dashboard maintenance and diagnostics are progressively disclosed under **More tools**, core mod states use ordinary language, active filters are always named, and conflict resolution explains the human choice before exposing technical evidence.

The same language pass now covers game settings, optional installers, profiles, history/support, crash clues, and the advanced workspace. Shared-file analysis is presented as **Shared Files / File Decisions**, raw evidence is moved behind technical details where practical, optional-installer rules such as “choose one” are translated from internal enum names, and common controls have larger default targets for easier scanning and keyboard/mouse use.

No deployment, rollback, conflict, update, or diagnostics capability is removed. Exact paths, rule sources, hashes, relationship data, and other expert details remain available in advanced/diagnostic surfaces. See `docs/UX-FIRST-TIME-OVERHAUL.md` for the audit and acceptance checklist.

## v8.8.10 — workflow feature closure

The workflow layer is now integrated on current architecture rather than replayed from the retired complete-workflows branch. The dashboard opens a single Loadouts, rules & diagnostics workspace for effective-file explanations, portable loadout recipes, inherited profiles/diffs, compatibility rules, relationships, safe update migration, stability evidence, and the active game adapter. FOMOD archives are parsed with bounded XML semantics and interactive choices; Smart Inbox leaves them untouched when user selection is required. Update migration journals selected metadata changes with the existing deployment transaction so rollback and Undo restore both live files and relationship state together.

## v8.8.9 — updater manifest compatibility and installed-client closure

The automatic updater now accepts legacy UTF-8 BOM manifests from already-immutable releases while new release manifests are emitted as BOM-free UTF-8 so older installed clients can parse them. Windows release publication and the real installed-client update/rollback gate run on the verified Heaven self-hosted runner, and the installed-client gate is sequenced after a successful Windows Release Gate so it validates the exact newly-published immutable release instead of racing publication or retrying the obsolete build-61 target.

## v8.8.8 — atomic launch-observation persistence

Each observed game launch now writes its immutable launch-history evidence and enabled-mod trust deltas in one SQLite transaction. The coordinator captures one pre-launch mod/build snapshot, exact launch-ID replay is idempotent, conflicting replay fails closed, and once an external startup outcome is known the authoritative persistence step is not canceled by a later user cancellation. Focused SQLite fault tests cover first/later trust-write rollback, replay, snapshot identity, and exact failed-launch diagnosis linkage.

## v8.8.7 — cross-session updater ownership

Updater apply ownership is now keyed to the normalized installation path in the Windows global kernel-object namespace, so separate interactive sessions targeting the same writable installation cannot independently enter the update transaction. Lock-establishment/type-collision failures remain fail-closed before updater mutation, while a crashed owner releases the kernel object so the existing journal/recovery path can resume safely. Focused Windows regressions cover separate-process contention, crashed-owner recovery, unrelated installations, and namespace/type-collision failure.

## v8.8.6 - unified main integration

This release converges the current engineering-control, product-hardening, responsive UI, and automatic-updater verification work on canonical main. It includes Agent Control 0.5.0 with Heaven Local Bridge execution, persisted game-profile ID containment, the responsive shell/mod-library polish, and the installed-client updater E2E harness while preserving the verified updater publication pipeline.

## v8.8.5 — Agent Control federation and release recovery

This release integrates Agent Control 0.5.0 as the repository's governed engineering control plane, including federated agent registration/heartbeats, live ownership-aware planning, autonomy enforcement, manager-routing awareness, and the heaven2-control / heaven-worker topology model. It also repairs the concurrent-integration duplicate import caught during release validation. The existing v8.8.4 product safety hardening remains intact.

## v8.8.4 — support recovery hardening

This release rebases the final support harvest onto canonical v8.8.3 archive-cleanup main. Save-snapshot retention now limits recursive deletion to verified direct children of the manager-owned `SnapshotRoot`; malformed/outside/missing/reparse rows are de-indexed without following their paths, and over-limit rows are retired only after their owned payload directory is successfully deleted. Failed cleanup therefore remains indexed for retry.

Remote previews derived from imported metadata are HTTPS-only and use a fail-closed egress boundary: automatic redirects are disabled, preview transport bypasses proxies, every DNS answer must be public, and the socket connects to the validated address. Nexus API traffic uses a separate no-redirect client so its custom API-key header cannot follow a redirect to another origin. Exact local release closure for `b48c1ff865ab41841d8c7eb931fca19f371f960e` passed Verify-Release **25/25**, Core **79/79**, Automation **31/31**, Integration **199/199**, self-test **11/11**, and Build-Release/ReadyToRun/helper publish; updater build **326**; ZIP SHA-256 `0F9B9577190037F29B500D2A709356A5F11E1CABA9770343FAA89F160AE6B154`.


## v8.8.3 ? archive streaming failure-cleanup hardening

Archive extraction now treats the currently-created output file as owned cleanup state for every payload-copy failure, not only cancellation and output-budget exceptions. Cleanup remains best-effort: if deletion itself fails, the original cancellation, safety failure, or I/O exception stays authoritative and the secondary cleanup error is logged instead of replacing it.

Smart Inbox now re-checks requested cancellation before classifying filesystem errors as recoverable per-item failures, so a cleanup/write error cannot downgrade a canceled run into ?skip and continue.? Whole-import catalog-invisible staging and process-death residue remain a separate LR-008 follow-up; v8.8.3 deliberately does not broaden this low-level repair into publication redesign.

## v8.8.2 — integrated safety and diagnostics hardening

This integration combines three independently reviewed shipped safeguards: crash bisection now validates a clean control and reproducing full suspect set before it can isolate a culprit; duplicate cleanup compensates ordinary database-delete failures after an archive move without guessing through ambiguous persistence state; and shareable support bundles sanitize recent structured logs at export while preserving full-fidelity local logs.

It also adds the profile-save rollback regression, adversarial continuity-validator fixtures, and durable audits for persisted game-profile path containment and launch-observation atomicity. Duplicate cleanup crash-durable reconciliation, broader diagnostic export sanitization, remaining crash-bisector evidence risks, game-profile ID repair, and launch-observation transaction repair remain explicit follow-ups.

## v8.8.1 — updater publication verification hardening

The automatic-updater release gate now verifies a newly published updater tag through GitHub's authoritative REST git-ref API instead of requiring immediate Git transport propagation. The check fails closed unless the exact expected tag exists, points directly to a commit, and resolves to the exact source SHA being published. This prevents a successfully published immutable release from being reported as failed solely because the Git tag has not propagated to fetch transport yet.


## Repair revision — 2026-09-27

Read `REPAIR-NOTES.md` for the current repairs and validation. This revision fixes
generic scanning, missing-blob recapture, idle watcher logging, verification cache
integrity, and source packaging. The complete solution builds with zero warnings
or errors; all 158 tests and 11 self-test checks pass on the Linux validation host.
Windows UI/locking/release validation remains required via `Test Everything.bat`
and `Build.bat`. The six unchanged previously checked Windows stages are preserved.

## v8.8.0 — incremental function verification and call-error observation

v8.8.0 keeps the v8.7 universal-game architecture and adds a verification layer designed for safe iterative development. Every explicit production executable body (methods, constructors, operators, local functions, explicit accessors, and expression-bodied properties/indexers) receives a stable syntax fingerprint. The verifier keeps a boolean `verified` checklist, preserves unchanged known-good functions as checked, and marks changed/new functions as unchecked. A scan safely persists those exact booleans immediately; only a complete build/test/self-test pass can promote changed/new fingerprints to full-release confirmation.


### Granular checked-state follow-up

After the first real Windows verification run, v8.8 now preserves successful checks granularly instead of discarding them when an unrelated stage fails. `.verification/function-status.json` is rewritten on every scan as a current `verified: true/false` function checklist. `.verification/stage-status.json` records strict-build/test passes by exact project/dependency/toolchain fingerprint; unchanged matches show `PASS-CACHED` on later `Verify-Release.ps1` runs. Production release builds still execute the full release gate.

The same Windows run exposed and this package fixes the FunctionVerifier Roslyn indexer signature bug, DTO `FormatVersion` initializer bug, WPF `Thickness`/`System.IO` issues, stale `GameProfile`-aware constructor call sites, and strict CA1859 discovery-method diagnostics.

Changed/new production functions must enter through `MasterDebugLog.BeginMethod()`. Active method scopes now observe first-chance exceptions raised by nested calls, so the master log records a `PASS-CHECK` when no exception was observed and an `ERROR-CHECK` when a nested call raised an exception (even if later handled); explicit successful scopes with handled nested errors are marked `PASS-WITH-ERROR-CHECK`. This is diagnostic only: exceptions are never swallowed or converted into success. See `docs/FUNCTION-VERIFICATION.md`.


The application now supports arbitrary Windows games through conservative folder-based game profiles, while retaining Monster Hunter: World as the deepest enhanced adapter. Use **Scan games** for Steam/Epic/GOG discovery or **+ Game** to select any Windows game executable manually. Each game has its own mod library, SQLite database, staged state, rollback history, issue history, visuals and deployment manifest.

Generic profiles deliberately avoid guessing game-specific semantics: exact-path collisions remain explicit choices unless metadata/manual rules prove a relationship. Known layouts such as BepInEx, Unreal Paks, `Data`, and `Mods` are used only to choose a sensible deployment target. Configure the profile if a game uses a different mod directory, save file or Nexus game domain.

# v8.6.27 visual source fallback

Basic mod artwork no longer depends on a Nexus API key. The manager uses local screenshots/FOMOD images first, Vortex-style `pictureUrl` metadata next, then a throttled public Nexus main-image fallback when a Nexus mod ID is known. Authenticated Nexus remains optional and is used for richer metadata/update checks.

## 8.6.26 — App compile cleanup

- Fixes WPF App compilation by fully qualifying `System.IO.File.Exists` in visual-row thumbnail/gallery code.
- Reuses one `JsonSerializerOptions` instance in automation tests to eliminate CA1869.
- Preserves all v8.6.25 UX, overlap explorer, dry-run, thumbnail, update, issue-tracking and family behavior unchanged.

## 8.6.25 — UX, overlap explorer, dry-run planning and background hardening

- Added smart mod-library views: **All**, **Enabled**, **Staged**, **Updates**, **Issues**, **Revalidate**, and **Superseded**. Search composes with the active view.
- Added **Preview changes**: a true planner dry run that captures/indexes newly-enabled sources, builds the same deployment plan as Apply, reports add/replace/remove/restore counts, and redirects to **Needs attention** if a blocking choice remains. It never writes `nativePC`.
- Added **Discard staged** to return all staged state to the last applied state without touching deployed files.
- Added an **Overlaps** page: an informational, MO2-style view of assets supplied by multiple enabled mods. Resolved shared textures/family overlays are shown calmly; unresolved choices remain in **Needs attention**.
- Added keyboard shortcuts: **Ctrl+F** focus Mods search, **Ctrl+Enter** Apply, **Ctrl+Z** Undo, **F5** refresh analysis.
- Periodic Nexus metadata/artwork refresh is serialized and skipped while foreground/transactional work is active. Manual sync/import/adoption share the same gate.
- Remote thumbnails now download to bounded temporary files and are atomically renamed only after a complete successful transfer.
- Clicking through visual-heavy libraries reuses persisted gallery metadata before recursively rescanning large source folders.
- See `RESEARCH-UX-ROBUSTNESS.md` for the Vortex/MO2/Fluffy UX patterns used in this pass.

## 8.6.24 — Visual library, Nexus/Vortex artwork and automatic update checks

- Mod rows now show cached thumbnails; selecting a mod expands a visual gallery of Nexus artwork, FOMOD/Vortex installer images, and screenshots found inside the source package.
- Nexus v3 `thumbnail_url` / `picture_url` / `image_url` artwork is cached under `State\Next\PreviewCache\Nexus` and refreshed automatically during metadata sync.
- FOMOD `Info.xml`/`ModuleConfig.xml` `<Image>` references are recognized as author-supplied visual metadata.
- Outfit Coverage now shows a preview thumbnail and supplying mod names for each armor/model row.
- Conflict thumbnails use the same safe image decoder. Corrupt image files fail closed instead of crashing the UI.
- Nexus metadata/artwork refreshes automatically while the app is open; update chains are checked daily and logical mods get an `Update available` badge. Updates are detected automatically but never silently installed/deployed.
- Manual **Sync metadata + visuals** forces an immediate refresh.

## 8.6.23 — Mod issue fallback / suspect tracker

- Added persistent per-mod issue suspect records for startup crashes, general game crashes, GPU/graphics crashes, and crash-bisector isolation.
- Automatic startup failures compare the failing launch against the previous successful modded launch and mark likely changed/enabled mods.
- Added one-click **Report game crash** and **Report GPU/graphics crash** actions for failures that happen after the 15-second startup observation window.
- GPU reports weight texture-heavy packages more strongly; startup/game reports weight plugin, executable, game-data, and structural content more strongly. Prior successful launches reduce suspicion while prior failures increase it.
- Suspects appear in **Needs attention** and as warning badges in the Mods list. Marks are advisory and never change files or enabled state.
- Automatic crash-bisector results are persisted as 99% **ISOLATED** marks.
- Added dismiss/clear controls for false positives and master-log `[MOD-ISSUE]` diagnostics.
- Database schema bumped to v5 with `mod_issue_suspects`.


## 8.6.22 — Shared texture resources + texture safety gate
- Treat shared body/skin textures embedded inside broader armor/outfit packages as one shared resource provider instead of a whole-mod conflict.
- Keep dedicated independent texture/recolor packs blocking unless lineage or an explicit provider rule proves they are related.
- Add pre-launch/Health validation for enabled MHW `.tex` sources: missing/unreadable sources, post-index size changes, truncated files, and invalid TEX signatures are surfaced before launch.
- Invalid/truncated TEX sources are launch blockers and are written to `MHW-DEBUG-ALL.log` under `[TEXTURE-SAFETY]`.
- This gate catches obvious malformed mod textures; it does not claim every MHW ERR12/GPU-device crash is caused by a mod.

## Texture-family resolver correction (v8.6.22)

Unrelated texture replacements remain real choices even when they touch a shared `mod_*` namespace. A resource namespace alone, or generic words such as `recolor`, `armor`, or a color name, cannot establish family lineage. Conversely, providers already proven to share one logical family no longer conflict with themselves on texture paths: explicit saved overlay rules win first, then family priority provides the deterministic internal fallback.

## Manual family chaining (v8.6.20)
When automatic family inference misses a relationship, select the conflict in **Needs attention** and click **Make main + chain others** on the package that should be the main mod. The manager persists the relationship as a manual family, marks the selected root as Main and the other packages as Optional, and writes an explicit ordered precedence chain (Main → Optional 1 → Optional 2 → …). Optional layers are ordered by your current priorities so shared base files still resolve deterministically. **Choose only** remains available for true mutually-exclusive alternatives. Source folders are not moved or merged.

## Family-safe conflict resolution (v8.6.19)

A proven logical family is now a hard boundary in conflict resolution. A base mod and its own optional/patch/component package cannot accidentally fall through to an ordinary structural conflict. High-confidence overlays and mostly-contained component packages auto-compose. Ambiguous sibling alternatives are shown as an internal family choice instead of an unrelated-mod conflict. Explicit user incompatibility/exact-file/resource-provider rules still win. `MHW-DEBUG-ALL.log` records these decisions under `[FAMILY-CONFLICT]`.

## Generic family inference update (v8.6.19)

Family inference is now brand-neutral. HPN is only a regression test; production grouping uses explicit/manager metadata, shared Nexus/source identity, semantic naming, file overlap/subset evidence, content roots, resource namespaces, and MHW asset/model identity. Similar names or shared armor slots alone are not enough to merge unrelated mods.

## Full-process debug build (v8.6.19)

`MHW-DEBUG-ALL.log` remains the single exhaustive handoff log. v8.6.18 keeps the full tracing architecture from v8.6.14, but fixes the trace-instrumentation generator so method scopes are never inserted into C# object/collection initializers. A dedicated C# trace-placement preflight now runs before verification/build, and an integration regression test enforces the same rule.

This is intentionally very verbose. Filenames, mod IDs, paths, operation names, timing, and process IDs may appear. Secret values such as API-key contents are not intentionally emitted. If anything breaks, upload the top-level `MHW-DEBUG-ALL.log`.

## WPF binding safety (v8.6.13)

v8.6.13 fixes a startup crash where WPF attempted to write back through inline `Run.Text` bindings targeting computed/read-only ViewModel properties. Inline display bindings now explicitly use `Mode=OneWay`, and an integration test prevents regressions.

## Startup testing (v8.6.12)

After `Build.bat`, launch the built application with **`RUN BUILT APP.bat`** from this top-level folder.

That launcher intentionally keeps the development/test data root here and forces runtime diagnostics into the same top-level `MHW-DEBUG-ALL.log`. If the loading screen disappears or startup fails, upload that one file.

v8.6.12 also keeps WPF in explicit-shutdown mode throughout bootstrap and performs main-window data initialization inside the traced startup transaction, so a startup exception cannot disappear behind the splash-window lifecycle.

# MHW Manual Mod Manager

## One-file diagnostics (v8.6.11)

The top-level `MHW-DEBUG-ALL.log` is the primary debugging handoff file. **If anything fails, upload this one file.** It accumulates chronological diagnostics from PowerShell syntax checks, `Build.bat`, `Test Everything.bat`, compiler/analyzer/test output, publish, application startup, startup maintenance, normal runtime operation telemetry, watcher warnings, and unhandled exceptions. Detailed per-stage logs remain in `BuildLogs`, `StartupLogs`, and `State\Next\Logs` for deeper inspection.

Use `OPEN MASTER DEBUG LOG.bat` to open it immediately. The logger is best-effort and never intentionally makes a product operation fail.

## Compatibility intelligence + logical mods

v8.6.7 treats the folder library as immutable source material and builds a higher-level compatibility graph above it. Main packages, optional components, patches, updates, texture revisions, Nexus lineage, superseded versions, atomic model/material/physics bundles, unmanaged live files, and game-build changes all feed one resolver. The normal UI shows logical/effective mods rather than raw file-conflict noise.

The intended result is hands-off: high-confidence `MAIN → OPTIONAL → UPDATE/FIX` relationships compose automatically; identical/shared resources are deduplicated; known newer texture providers win only their overlapping paths; old revisions are archived; multipart packages remain one toggle with an internal configuration drawer. A human choice is reserved for independent logical mods that directly replace the same non-mergeable asset and cannot be safely ordered.

### Nexus lineage (optional)

Offline metadata (`mhw-manager.meta.json`, common `meta.ini` fields, stored source URLs/folder hints) is always used when available. For stronger live Nexus lineage, put your Nexus API key in `State\Next\nexus-api-key.txt` or set `NEXUS_API_KEY`, then click **Sync lineage**. Live enrichment is rate-limited during normal startup and can be forced from the UI. The manager never requires Nexus connectivity to deploy local mods.

### Manual-install adoption

At startup the manager counts untracked files already present under the live `nativePC`. **Adopt manual files** copies those files into a new immutable source package under `Mods` without changing the live game tree. The adopted path + SHA-256 is remembered; unchanged adopted files stop being offered repeatedly, while later external edits become visible again.

### Texture previews

Package screenshots/adjacent PNG/JPG/BMP files can be shown on rare texture-choice cards. Raw `.tex` conversion is best-effort and optional: configure `MHW_TEX_CONVERTER` and `TEXCONV_EXE` if you want generated PNG previews. Missing converters never block deployment.

See `docs/AUTO-COMPOSITION.md` for the safety model.

## v8.6.7 Automation + Compatibility Intelligence

### v8.6.7 verifier reliability

`Test Everything.bat` now treats the verifier as another testable subsystem. It parser-checks the active PowerShell scripts, round-trips a sample report through Windows PowerShell JSON serialization, then runs every reachable compile/analyzer/test/self-test stage. A failed native `dotnet` command is logged but does not terminate later stages. The final report uses plain PowerShell arrays for Windows PowerShell 5.1 compatibility.

## Full compiler/analyzer sweep

Run `Test Everything.bat` (recommended) or `scripts\release\Verify-Release.ps1`. v8.6.7 first syntax-checks all active PowerShell scripts, then performs a relaxed whole-solution build so analyzer warnings do not block downstream projects, then compiles every project independently with warnings-as-errors and continues through the entire project list. This exposes all *reachable* compiler/analyzer failures in one run instead of stopping at the first failing dependency.

Artifacts are written to `BuildLogs\`: the human-readable verification transcript, `compile-summary-*.txt`, a relaxed whole-solution `.binlog`, one strict `.binlog` per project, and the final strict solution `.binlog` when the sweep is clean. A genuine C# compiler error in an upstream project can still make a downstream assembly impossible to compile until that upstream error is corrected; the verifier reports this rather than pretending otherwise.


This is the C#/.NET successor to the v7 PowerShell/WPF manager, with the production bug-fix/stability requirements implemented directly into the planner, storage, filesystem transaction engine, diagnostics, tests, and WPF shell.

It is built for the real topology this project was designed around: 160+ source folders, 70+ simultaneously enabled mods, HPN base/component packages, shared `mod_hepsy` resources, body/texture providers, root/plugin files, and genuine structural incompatibilities.

## Do this first

**Keep your existing `Mods` folder and your entire existing `State` folder.**

v8.6.7 writes new state under:

```text
State\Next\manager.db
State\Next\Blobs\
```

Legacy v7 remains under `State\V2` and the fallback v7 source is included under `legacy-v7`. Migration is non-destructive and does not touch `nativePC` merely to import state.

## v8.2 visual redesign

The WPF shell has been rebuilt around a restrained charcoal/gold desktop design: persistent left navigation, a tighter global command bar, metric/dashboard cards, cleaner mod-library rows, stronger staged-state presentation, and consistent page toolbars/cards. The redesign intentionally leaves deployment, conflict, migration, profile, and filesystem semantics unchanged.

## What changed in the hardening pass

The main goal is not more features; it is proving that the features already present survive load, concurrency, external edits, and crashes.

- whole-plan preflight before the first filesystem write;
- per-file TOCTOU revalidation immediately before mutation;
- durable operation journal with explicit recovery states;
- one atomic SQLite metadata commit for manifest + ownership + mod state + `Committed` marker;
- recovery that refuses to overwrite unknown post-crash edits;
- safer `ReplaceFileW`-based existing-file replacement;
- connection-per-operation SQLite/WAL instead of shared-cache coupling;
- one-pass/indexed conflict analysis and sparse incompatibility lookup;
- no hidden O(paths²) decision lookup in the planner;
- XXH3 verification of metadata cache hits so same-size/same-timestamp source edits are detected;
- indexed armor-component coverage instead of wildcard path scans;
- batched/versioned armor DB startup import;
- batched WPF collection replacement and preserved DataGrid virtualization;
- planner/archive/hash/migration/health work kept away from the Dispatcher;
- structured correlation IDs, classified errors, ThreadPool/GC metrics and Dispatcher stall detection;
- Restart Manager lock-owner diagnostics;
- secure archive path/device/ADS/reparse checks;
- expanded crash-phase, stale-plan, cache, archive, large-conflict and deterministic-planner tests.

See `docs/BUG-AUDIT.md` for the concrete failures/root causes and `docs/DIAGNOSTICS.md` for hang capture.

## Conflict semantics remain conservative

Enabled mod state and winning file provider are separate concepts. Several enabled mods may coexist while one wins a particular overlapping path.

The manager can automatically resolve byte-identical files, shared textures/resources, remembered overlays, high-confidence component/patch relationships, dedicated texture providers, and newer related texture revisions. Unknown structural/model/material/physics/plugin/game-data collisions fail closed until there is an explicit rule.

Different bytes are never described as merged unless an actual file-format merger exists.

## Build and verify on Windows

Use the current .NET 10 SDK (the repository pins SDK `10.0.401` in `global.json`). Then run:

```powershell
.\scripts\release\Verify-Release.ps1 -RunBenchmarks
```

If that passes, build the self-contained Windows x64 release:

```powershell
.\scripts\build\Build-Release.ps1
```

The final binary ZIP is produced under `artifacts` by the build script.

This source package was assembled in a Linux execution container without a local .NET SDK or Windows WPF runtime, so **a compiled executable is intentionally not claimed in this package**. `VALIDATION.md` describes the exact boundary between static validation here and runtime validation required on Windows.


## Startup diagnostics

Every application launch writes a verifier-style startup trace under `StartupLogs` beside the manager (or under `%LOCALAPPDATA%\MhwModManager\StartupLogs` if the manager directory is not writable). Each launch produces:

- `startup-YYYYMMDD-HHMMSS-fff.log` — human-readable ordered stages with START/PASSED/FAILED, elapsed time, context, and full exceptions.
- `startup-YYYYMMDD-HHMMSS-fff.json` — machine-readable stage results for debugging/comparison.

The trace covers path discovery, database initialization, service composition, migration, catalog refresh, armor import, Nexus/game-build intelligence, recovery, each startup-automation substage, main-window creation, and watchdog startup. Startup maintenance attempts independent safe substages even after one fails so the report captures more than the first error. The failure dialog prints both log paths. `Open Startup Logs.bat` opens the folder directly.

## Diagnosing a freeze

The app records structured timings automatically. For deeper evidence, click **Activity -> Capture diagnostics**, or run:

```powershell
.\scripts\diagnostics\Capture-Diagnostics.ps1 -ProcessId <PID>
```

When installed, the script uses `dotnet-stack`, `dotnet-counters`, `dotnet-trace`, and `dotnet-gcdump`. The support bundle stays bounded and does not automatically copy mod assets/CAS blobs.

## Safe upgrade procedure

1. Close MHW and v7.
2. Back up the manager folder if you want an additional external copy.
3. **Do not delete `Mods` or `State`.**
4. Compile/verify v8.5.0 with the scripts above, or use a Windows build produced by them.
5. Put the published v8.5.0 files in the manager root next to your existing `Mods` and `State`.
6. Launch `MHW Mod Manager.exe`.
7. Read the migration report before first Apply.
8. Run Health.
9. Test a small staged deployment before a mass profile switch.
10. Keep `legacy-v7` until migration, one Apply, Health, restart, and Undo have all been verified.

## Source layout

- `src/MhwModManager.App` — WPF/MVVM shell
- `src/MhwModManager.Core` — domain, rules, conflict engine, planner
- `src/MhwModManager.Storage` — SQLite/WAL, schema, profiles, v7 migration
- `src/MhwModManager.Filesystem` — CAS/hashing/scanning/archive/deployment/recovery/Restart Manager
- `src/MhwModManager.Mhw` — game discovery and armor catalog
- `src/MhwModManager.Diagnostics` — telemetry, health, exception policy, support bundle
- `tests/` — deterministic/unit + Windows filesystem/failure-injection integration tests
- `benchmarks/` — realistic planner stress fixtures
- `docs/` — architecture, failure modes, migration, schema, diagnostics and bug audit
## v8.6 automation workflow

The default workflow is now intended to be almost hands-free. Drop archives/folders into `Inbox\` or keep using `Mods\`; startup maintenance imports safe inbox content, learns lineage, assigns categories, and archives safe disabled duplicates/superseded revisions. The main action is **JUST PLAY**: it applies staged changes, adopts trackable loose `nativePC` files, snapshots the MHW save and manager state, runs the health/dependency/conflict gate, launches the game, and records a Last Known Good setup when startup survives.

If startup begins failing after newly enabling mods, **Auto-diagnose startup crash** performs a binary-search style bisect against Last Known Good and restores the original setup after diagnosis. Genuine unresolved structural alternatives or missing dependencies still stop rather than being guessed.

For complete verification, double-click `Test Everything.bat` (or `Verify.bat`). The verifier continues through all reachable compile/analyzer/test groups even when one fails and writes `BuildLogs\verification-report-*.md` plus a machine-readable `.json` report. See `docs\AUTOMATION-AND-TESTING.md` for details.


### v8.6.8 build diagnostics
`Build.bat` now writes stage-specific logs under `BuildLogs`. The release builder performs a RID-specific ReadyToRun restore before publish. If only the SDK ReadyToRun optimization phase fails, it records the failure and retries a self-contained JIT publish; application correctness/tests are unchanged.

### v8.6.9 publish analyzer preflight

Release builds now compile the exact `win-x64` App target with warnings-as-errors before publish. Startup database initialization also forwards the diagnostic cancellation token explicitly, resolving CA2016 in RID-specific publish builds.

### v8.6.10 build harness exit-code fix

The release builder now consumes `Tee-Object` output with `Out-Host` and returns only the native `dotnet` exit code from each stage. This prevents successful stages from being misread as failures when their console text is captured alongside exit code `0`. A harness contract check fails immediately if any stage ever returns a non-scalar/non-integer result.

## Generic mod-family inference (v8.6.18)

Family detection is evidence-based and is not tied to HPN or any other author/series. The engine prefers explicit/manual family IDs, imported manager metadata such as `logicalFileName`, and shared Nexus source identity. When those are unavailable it combines semantic name lineage with concrete file evidence: subset/overlap ratios, shared content roots, resource namespaces, and MHW armor/model identity.

Names alone do not collapse packages. Two similarly named mods with disjoint layouts stay separate, and two unrelated replacements of the same armor model stay separate unless there is additional lineage evidence. Nexus Main/Optional/Misc labels are treated as descriptive metadata rather than structural truth.

Accepted heuristic pairings are written to `MHW-DEBUG-ALL.log` as `[FAMILY] GENERIC ... score=... evidence=...` so every automatic grouping can be audited.

## Git-first agent continuity


## Plugin workspace and Heaven control plane

New plugin development is centralized under `plugins/`. The initial platform direction is a Heaven Control Plane: a stable, permissioned capability layer for local execution, filesystem/Git operations, build/test, browser/GUI automation, worker queues, repository indexing, artifacts/checkpoints, observability, and later multi-machine workers.

Read `plugins/README.md` for layout/ownership rules and `plugins/IMPLEMENTATION_SWARM_PROMPT.md` for the current collaborative implementation directive.

The existing `heaven-bridge/` directory remains active compatibility/runtime infrastructure. New plugin-platform code should be developed under `plugins/heaven-control-plane/` and should reuse/adapt proven bridge primitives rather than forking a second implementation. Any physical migration of the existing bridge plugin source must preserve bootstrap/workflow/runtime compatibility and be verified on `heaven`.


GitHub `fengie/mhw-mods` on `main` is the canonical development state. Repository-aware coding agents should read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, and `_AGENT_CONTEXT/CURRENT_REVISION.json` before changing code, then follow the full continuity protocol. Update `_AGENT_CONTEXT/` and commit the handoff state with the code it describes. Every shipped application change must also bump the app version in `VERSION.txt` and `Directory.Build.props`, keep duplicated release/update metadata aligned, update this README, and add the matching `CHANGELOG.md` entry before the work is considered complete. Documentation/agent-policy/evidence-only changes that do not change the shipped application do not require an app-version bump. Run `scripts/testing/Test-AgentHandoff.ps1` before declaring work complete. `Build Source Handoff.bat` remains available when a reproducible source ZIP export is useful.

## Verification closure status

The second Windows/.NET 10.0.401 verification run reached **24 PASS / 1 FAIL**. Every compile/analyzer/test/self-test stage passed; the sole failure was four missing entry traces detected by the function scanner. This source handoff adds exactly those four `MasterDebugLog.BeginMethod()` scopes. Run `Test Everything.bat` once more on Windows to confirm zero function trace/call-site coverage gaps and allow exact-current function fingerprints to be promoted to `verified=true`.
