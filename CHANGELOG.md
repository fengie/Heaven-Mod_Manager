# v8.8.77 — 2026-10-02

- Route the updater installed-client E2E publication classifier from unavailable GitHub-hosted `ubuntu-latest` capacity to the known-good self-hosted Heaven Windows runner.
- Preserve the classifier's read-only contents permission, exact immutable-release decision, canonical-main ancestry check, and fail-closed installed-client E2E gating.
- Replace the stale regression that required `ubuntu-latest` with a job-scoped assertion for the self-hosted runner labels.

# v8.8.76 — 2026-10-02

- Add a credential-free, schema-bounded Vortex handoff contract for Monster Hunter: World without reading Vortex private state, credentials, cookies, deployment folders, or authenticated download state.
- Export only reviewed local package/profile identity, enable/priority intent, optional Nexus mod/file IDs, and optional managed-file SHA-256 values; generic source URLs are excluded and unknown fields fail closed.
- Validate the exact MHW game contract, normalized managed paths, hashes, duplicate identities/targets, manifest size, and mod count before any handoff can be saved as a profile.
- Re-read and re-match at import time so stale previews cannot authorize changed local content; missing, ambiguous, wrong-game, malformed, and hash-mismatched entries remain disabled.
- Save imports as isolated manager profiles only; Vortex-specific failures do not mutate live deployment or normal manual-mod-manager state.
- Write exported handoffs through same-directory temporary files and atomic replacement so an interrupted export cannot replace a previously valid handoff with partial JSON.
- Add deterministic credential-boundary, traversal/game-path validation, round-trip, ambiguity, atomic-export, and isolation regression coverage.
- Extend the continuity preflight so CURRENT_STATE must identify the exact current patch, preventing stale canonical-state handoffs from silently surviving later integrations.

# v8.8.75 — 2026-10-02

- Give the Mod Library whole-mod enable checkbox an accessible name bound to the current mod `DisplayName`.
- Give advanced component toggles accessible names bound to their current `Label` while retaining native WPF checkbox TogglePattern and keyboard behavior.
- Keep accessible identity data-bound so DataGrid/ItemsControl recycling cannot retain a previous item's name.
- Add deterministic XAML regression coverage for both toggle surfaces.

# v8.8.74 — 2026-10-02

- Add a lightweight `workflow_run` classifier ahead of the self-hosted updater installed-client E2E.
- Execute the expensive E2E only when the upstream source has an exact immutable updater release.
- Classify a missing exact release as an intentional supersession only when canonical `main` is proven to be a descendant of that source; canonical or divergent missing-release states remain hard failures.
- Share the decision logic through `UpdaterReleasePolicy.ps1` and add deterministic policy/workflow regressions covering published, superseded, canonical-missing, divergent, least-privilege, and heavy-job gating behavior.
- Keep exact-source release resolution inside the installed-client E2E as defense in depth.

# v8.8.73 — 2026-10-02

- Resolve the exact updater ZIP identity from `update-manifest.json`, recompute its SHA-256, and fail closed on any manifest/artifact mismatch before provenance or publication.
- Generate GitHub SLSA build provenance with SHA-pinned `actions/attest` and verify the attestation against `fengie/mhw-mods` before the first updater publication mutation.
- Grant the release workflow the required OIDC and attestation permissions while keeping the existing non-cancellable public/private publication transaction and stale-main guard intact.
- Make private-repository availability explicit: public repositories attest automatically; private repositories require GitHub Enterprise Cloud and `MHW_ENABLE_GITHUB_ATTESTATIONS=true`; unsupported private runs record an explicit skip instead of claiming provenance.
- Add updater publication policy regressions covering action pinning, permissions, provenance ordering, exact subject name/digest wiring, private-tier gating, digest recomputation, and pre-publication attestation verification.
- Close #583 as implementation-complete while retaining external GitHub plan entitlement as an explicit operational constraint.

# v8.8.72 — 2026-10-02

- Prevent torn live-save snapshots by requiring stable source metadata plus matching SHA-256 observations before atomically promoting a copied save into a successful snapshot.
- Retry bounded concurrent save mutation, remove incomplete snapshot payloads on pre-record failure/cancellation, and add deterministic mutation/cancellation regression coverage.
- Revalidate the exact workflow SHA against canonical `main` immediately before the first updater publication mutation; stale queued release runs skip both public and canonical publication.
- Enforce canonical-ready active continuity state in `Test-AgentHandoff.ps1`: `integrationState=canonical-main`, `workingBranch=main`, non-candidate status, and no retained candidate source commit.
- Add negative fixtures for stale integration state, stale feature working branches, candidate status, retained candidate source commits, and active PR/issue state.
- Add an intentional Browse Mods no-selection detail state and keep install disabled until an exact provider file is selected, while preserving stale-file clearing when mod selection changes.
- Close audit findings #575, #576, and #577 while leaving issue #578 and the remaining catalog/UX/recovery queues independent.

# v8.8.71 — 2026-10-02

- Add an optional installed-origin snapshot provider contract so exact update checks can reuse one authoritative mod/file hydration when a provider supports it.
- Implement the snapshot path for GameBanana, eliminating the duplicate `/Core/Item/Data` detail request previously made for each installed-origin update check.
- Preserve the existing two-call fallback for other providers plus cancellation, provider health/failure classification, exact mod/file identity validation, and the rule that replacement files are never guessed.
- Add a deterministic request-count regression proving one GameBanana installed-origin check performs exactly one detail request.
- Reconcile the change onto canonical v8.8.70 main; the two stale-profile edge cases found after PR #572 are tracked separately in issue #578.
# v8.8.70 — 2026-10-02

- Repair a persisted same-root game profile when its executable is missing instead of allowing that stale record to suppress automatic installed-game discovery indefinitely.
- Preserve the existing profile ID and active-game selection during repair, while filling missing store and Steam app metadata from the discovered candidate.
- Leave same-root profiles with a live executable untouched and avoid duplicate registration.
- Refuse automatic Monster Hunter: World repair through a non-`MonsterHunterWorld.exe` executable when MHW identity is known from the persisted profile or Steam app 582010 discovery metadata.
- Add deterministic regressions for inferred executable repair, live-profile no-op behavior, MHW adapter protection, and the stale-generic/MHW-discovery edge case.
- Preserve the integrated v8.8.69 provider-aware catalog search and 1000-row cache capacity boundary; #558 remains open for broader discovery/pagination scale work.

# v8.8.69 — 2026-10-02

- Make explicit Browse Mods searches contact only configured providers that advertise `CatalogProviderCapabilities.Search`, while keeping per-keystroke filtering cache-only.
- Persist provider search results through the existing source-aware `CatalogSyncService` cache boundary and isolate provider failures so cached matches remain usable.
- Preserve providers' declared capability contracts rather than probing unsupported full-catalog text-search modes.
- Raise `CatalogRepository`'s bounded search result ceiling from 500 to 1000 so the already-virtualized 1000-row Browse Mods capacity is actually reachable.
- Add regressions for capability gating, cache-only debounce behavior, and deterministic search results beyond the former 500-row ceiling.
- Preserve the completed v8.8.68 selector/updater acceptance boundary; #558 remains open for broader provider-aware pagination and scale work.

# v8.8.68 — 2026-10-02

- Expose the active game's selected `DisplayName` directly on the header ComboBox automation peer with `AutomationProperties.ItemStatus`, preserving the stable `Active game` accessibility name.
- Add `TextSearch.TextPath="DisplayName"` so the selector has a control-level human-readable text fallback in addition to the existing `DisplayName` data template.
- Change packaged selector acceptance to validate the visible/bounded ComboBox peer value because installed v8.8.67 E2E #290 proved the closed WPF ComboBox exposes zero raw UI Automation descendants.
- Preserve strict failure behavior for missing/raw `GameProfile { ... }` selected values plus enabled Switch/Settings checks, and retain the structural XAML guard for the visual selected-content template.
- Record updater-main-381 as a successful immutable v8.8.67 publication; #556 remains open because its second installed-client E2E attempt failed at selector acceptance after the first attempt hit a transient GitHub asset HTTP 500.

# v8.8.67 — 2026-10-02

- Give the active-game ComboBox its own stable Windows UI Automation ID and explicitly expose the rendered game-name TextBlock's accessible name.
- Make packaged selector acceptance traverse the WPF raw automation tree instead of relying on Control View descendants, while still rejecting any raw `GameProfile { ... }` rendering.
- Capture bounded raw-tree diagnostics in selector failures so headless/session-specific accessibility behavior can be debugged precisely.
- Print installed-client E2E logs and failure evidence directly into the Actions console before failing, so diagnostics remain available even when GitHub artifact storage is full.
- Keep issue #556 open until the exact v8.8.67 package passes update, rendered-selector UI acceptance, adjacent-action checks, and rollback.

# v8.8.66 — 2026-10-02

- Extend the real packaged updater E2E so it inspects the updated WPF application's UI Automation tree and verifies the closed header game selector renders the active profile's human-readable display name.
- Fail installed-client acceptance when the selected surface exposes raw `GameProfile { ... }` record text, the expected rendered text is missing, or the adjacent Switch/Settings actions are unavailable.
- Add a stable automation ID to the selector display template while retaining character ellipsis for long game names.
- Make the updater E2E evidence verifier require selector UI evidence, so future releases cannot claim installed-client success without carrying the rendered selector result.
- Keep issue #556 open until this exact v8.8.66 candidate is verified, published, and the packaged UI Automation acceptance passes.

# v8.8.65 — 2026-10-02

- Fix the app-owned WPF ComboBox closed-selection presenter to reuse the control's `ItemTemplate`, `ItemTemplateSelector`, and `ItemStringFormat` instead of the unreliable `SelectionBoxItemTemplate` path.
- Keep the header game selector's `DisplayName` item template for dropdown rows while now applying the same template to the selected game, preventing raw `GameProfile { ... }` record text.
- Strengthen XAML regression coverage so the custom dark ComboBox template cannot silently revert to `SelectionBoxItemTemplate`/raw-object rendering.
- Reopen issue #556 based on installed-client evidence; installed Windows/WPF visual confirmation remains required after the source fix.

# v8.8.64 — 2026-10-01

- Fix the main-window game selector so the custom dark ComboBox renders `GameProfile.DisplayName` rather than raw record/debug text.
- Replace the plain Browse Mods identity cell with artwork-backed rich rows showing summary, author, category, downloads, provider, version, update time, and cache state.
- Add selected-mod artwork plus version/update context while preserving exact-file loading, provider-page handoff, and safe acquisition.
- Accept only credential-free HTTPS thumbnail URIs at the UI projection boundary.
- Enable recycled DataGrid row virtualization, raise provider refresh requests from 60 to 100 items, and raise cached visible results from 250 to 1000.
- Add focused XAML/source regression coverage and keep deeper provider-aware expansion/search plus filter/sort/provider-health UX tracked in #558 and #559.

# v8.8.63 — 2026-10-01

- Harden permitted HTML crawler path authorization with segment-boundary matching and stricter manifest-prefix validation.
- Replace arbitrary `HttpClient` injection with a production crawler transport that disables automatic redirects.
- Resolve redirects manually with a five-hop bound and validate every target before request dispatch.
- Reject off-origin, HTTP, alternate-port, credentialed, out-of-prefix, missing-location, looping, excessive, and transport-rewritten redirect flows.
- Add deterministic tests proving disallowed redirect targets are never contacted while preserving content-type/size bounds, compliance reviews, kill switch behavior, and cancellation.

# v8.8.62 — 2026-10-01

- Add an integrated **Browse Mods** WPF surface over the provider-neutral SQLite/FTS catalog cache with local search, exact file inspection, provider-page handoff, and failure-isolated refresh.
- Add a bounded catalog acquisition bridge that accepts only exact provider/game/mod/file identities, requires credential-free HTTPS endpoints at the durable boundary, downloads direct artifacts into manager-owned temporary staging, hashes them, and routes them through the existing archive inspection/import pipeline.
- Persist exact installed provider mod/file origins after successful direct import and expose exact-origin update checks that fail closed when a source/file disappears or changes instead of guessing replacements.
- Recover the official CurseForge REST adapter, transport, compliance policy, deterministic fixtures/tests, and the generic permitted crawler framework with reviewed terms/robots, path/origin containment, response bounds, and kill switch.
- Keep CurseForge opt-in through `MOD_MANAGER_CURSEFORGE_API_KEY` plus `MOD_MANAGER_CURSEFORGE_GAME_ID`; secrets and expiring download URLs remain outside durable catalog records.
- Reconcile all catalog work onto current v8.8.61 main rather than reviving stale issue #281 branches.

# v8.8.61 — 2026-10-01

- Replace remaining system-light WPF scrollbars with application-owned dark ScrollBar and Thumb chrome while retaining vertical/horizontal paging behavior.
- Request immersive dark mode for the native Windows title bar through a constrained System32 DWM import without enabling unsafe blocks for the application.
- Repair the updater build-identity display string that shipped mojibake (`â€¢`) and use an ASCII-stable separator for version/build/SHA text.
- Add regressions for dark scrollbar ownership, title-bar dark-mode interop, clean build labels, and literal escaped-newline corruption in the interop source.
- Reconcile on top of integrated v8.8.60 so the Dashboard stretch/clipping repair and runtime/updater hardening remain intact.

# v8.8.60 — 2026-10-01

- Reconcile RECOVERY-004 from the archived audit/runtime hardening lanes onto current 8.8.59 Settings lineage without importing stale version or continuity metadata.
- Restore Dashboard stretch semantics by removing the direct viewport-width binding and using stretch alignment on the ScrollViewer/content wrapper.
- Make first-use metadata refresh retry-safe: completion is recorded only after successful refresh/reload/analysis, while failure or cancellation clears the in-flight state for a later retry.
- Serialize staged program-update identity through the final helper-launch boundary under `programUpdateGate`, invalidating prepared handoffs if a newer staged candidate replaces them while preserving automatic-update and manual-update intent.
- Redact updater health token/file/attempt values from startup diagnostics and add behavioral/structural regressions for all four recovered hardening fixes.
- Focused Windows evidence before versioning: Release build 0 warnings / 0 errors, IntegrationTests 268/268, FunctionVerifier 1469 functions with 0 trace gaps, 0 uncovered call sites, and 0 parse errors.

# v8.8.59 — 2026-10-01

- Add a dedicated Settings tab with persistent preferences for automatic program updates, UI animations, last-tab restore, Apply/discard confirmations, and periodic background metadata refresh.
- Allow automatic program updates to be switched off while preserving the existing explicit Check for updates path as manual install intent.
- Persist settings under the isolated Next state root using atomic same-directory replacement and fall back to safe defaults if the JSON file is malformed or unreadable.
- Apply motion and update preferences immediately, remember the selected tab when requested, and gate periodic metadata refresh without removing demand-loaded refresh.
- Add focused integration regressions for defaults, persistence, malformed settings, Settings bindings, and manual/automatic updater separation.

# v8.8.58 — 2026-09-30

- Replace the native Windows ComboBox surface with application-owned dark-theme ComboBox and ComboBoxItem templates.
- Preserve readable selected/dropdown text while styling popup, hover, focus, selected, arrow, and disabled states from the existing application palette.
- Replace the v8.8.49 matched-system-color regression with a dark-chrome regression that requires the custom template and rejects Windows control brush fallback.
- Recover only the still-useful RECOVERY-005 UI/test semantics from the archived branch; stale archive version/continuity metadata is not imported.
- Integrate the verified source/test tranche via PR #549; installed Windows/WPF visual/interaction acceptance remains an explicit runtime follow-up before RECOVERY-005 is marked DONE.

# v8.8.57 — 2026-09-30

- Cut global programming-agent bootstrap authority over from MHW to `fengie/heaven-toolbox@main`.
- Removed MHW's duplicated global trainer, Git doctrine, plugins, Heaven Bridge, Agent Control, and generic tooling roots after Toolbox parity/inventory completion.
- Relocated MHW-only FunctionVerifier and SelfTest executables under `tests/` and updated solution/build/release verification references.
- Replaced global-infrastructure CI copies with a MHW product-security gate plus a fail-closed ownership regression gate.
- Updated durable MHW handoff/routing metadata so product continuity stays local while reusable permissions, routing, training, and tools resolve through Heaven Toolbox.
- Repaired cutover regression coverage for the relocated FunctionVerifier path, fail-closed Core Rule authorization, and recursive handoff continuity wording.
- Removed global-tool work from MHW's active revision/recovery state and transferred remaining Agent Control/Heaven/plugin context follow-up to Heaven Toolbox issue #5.
- Scoped mixed historical ledgers so old Agent Control/Heaven/plugin entries remain provenance only; complete pre-cutover copies are preserved under Toolbox `history/mhw-global-tooling/`.

# v8.8.56 — 2026-09-30

- Run one automatic best-effort installed-game discovery pass on the first Games-page refresh while retaining the explicit manual rescan and executable-selection fallback.
- Isolate launcher discovery failures and serialize registry mutations so background discovery cannot race manual game-profile writes.
- Expand bounded discovery across Steam libraries, Epic, GOG, and Xbox Games installs; preserve launcher-proven roots and avoid whole-drive executable crawling.
- Replace shallow executable probing with bounded breadth-first lookup that skips reparse/support/redistributable trees and filters helper, installer, crash-handler, and anti-cheat executables without rejecting legitimate Crash-named games.
- Add regressions for mixed MHW + generic first-refresh discovery, one-time lifecycle behavior, Steam multi-library manifests, nested executable resolution, Xbox Content roots, and helper filtering.
- Recover the escaped-defect precedent as LR-063 and keep RECOVERY-007 active until exact-head and Windows/runtime evidence closes it.

# v8.8.55 — 2026-10-01

- Restore a canonical project/recovery ledger containing every still-unique work lane discovered after the branch-zero cleanup.
- Define branch cleanup as semantic work extraction: useful code/tests/research/evidence must be integrated or converted into an actionable canonical-plan item before a stale branch ref is deleted.
- Make archive-only preservation explicitly non-terminal; cleanup dispositions are INTEGRATED, EXTRACTED, SUPERSEDED, or REJECTED.
- Propagate the rule through the Git directive, agent bootstrap, generic operating standard, multi-agent coordination training, learned-rules ledger, and handoff verification.
- Record v8.8.54 Agent Control coordination as recovered/integrated while leaving the remaining archived lanes visible for completion.

# v8.8.54 — 2026-09-30

- Enforce case-insensitive mutable-boundary collision checks during workflow preflight so `Foo` and `foo` cannot become separate writers.
- Reject routing manifests that assign the same mutable boundary to multiple slots instead of trusting contradictory ownership state.
- Reject counted direct deployment when one explicit mutable boundary is supplied; callers must assign distinct boundaries explicitly rather than receiving synthetic `#1`/`#2` leases.
- Add focused routing/deploy/lease regressions and advance Agent Control runtime/private-plugin identity to 0.6.25.

# v8.8.53 — 2026-09-30

- Add the standalone `plugins/agent-work-reports` dashboard for glanceable overall task and agent progress.
- Ingest authoritative Agent Control snapshots/logs plus bounded explicit plan/progress/blocked/done checkpoints with an offline JSONL fallback.
- Keep the reporting app loopback-only and non-mutating, with a stable `agent-work-reports/view/v1` boundary for later Agent Manager integration.
- Wire the plugin into catalog/root verification and preserve deterministic unit, syntax, and HTTP-smoke coverage.

# v8.8.52 — 2026-09-30

- Compact Agent Manager's high-volume operational surfaces into collapsed summary cards with a responsive at-a-glance overview and bounded per-card scrolling.
- Preserve explicit Inspector behavior while auto-opening it for agent/notification inspection and surface critical/warning notification counts in collapsed summaries.
- Add restrained transform/opacity motion, reduced-motion/hidden-page safeguards, and opt-in generated Web Audio UI feedback without continuous animation or polling sounds.
- Advance Agent Control/plugin identity to 0.6.24 and retain regression coverage for the compact overview and motion/audio contract.

# v8.8.51 — 2026-09-30

- Fixed the corrupted Windows desktop shortcut icon by removing the malformed 48×48 PNG frame while preserving the valid 16×16, 24×24, and 32×32 application artwork.
- Added deterministic ICO/PNG integrity coverage for frame bounds, image dimensions, per-chunk CRCs, and exact IEND termination.
- Classified shell-visible binary assets as verified release inputs so a successful compile cannot silently ship structurally corrupt icon resources again.

# v8.8.50 — governance integration and enforcement repair
- Reconcile superseded PR #525 compact operating standard, role deltas and current-state consolidation onto v8.8.49 main while preserving its repaired ancestry.
- Preserve pre-response/global training and full constitution startup obligations; repair recursive successor validation and raw UTF-8 budget enforcement.
- v8.8.49 UI exact-head gates remain historical source proof; v8.8.50 requires fresh exact-head verification. v8.8.48 source/build357 release closure is retained separately.
- Scope Workflow Feature CI concurrency by PR/ref so superseded runs cancel only their own lane; unrelated PRs queue on Heaven instead of canceling exact-head verification for the integration candidate.

# v8.8.49 — 2026-09-30

- Fixed low-contrast selected text in native WPF ComboBoxes by pairing system control background and text brushes.
- Fixed the Mods empty-state overlay so collection-count changes notify InstalledCount and reveal populated mod content.
- Added focused XAML and UX regressions for both defects.

# v8.8.48 - Mods layout integration
- Reconcile original PR522/head e0b423e7 without discarding newer main or rewriting its externally created branch.
- Remove explicit ContentPresenter ActualWidth binding from direct Mods root; preserve automatic Stretch.
- Strengthen existing wide-layout guard with parsed direct-wrapper assertions, independent of attribute formatting or unrelated descendant grids.
- Persist exact v47 release/controller/browser/rollback closure and semantic branch inventory.
- Actual styled WPF probes at four widths preserve layout; loaded .NET10/populated-VM reproduction remains unverified. No new features or claim that probes prove the original reported cause.

# v8.8.47 — existing bug integration
- Recover exact federation inspector fix from branch8f78d21d without stale server/version history. Linked managed identity remains lineage.
- Add executable notification/direct-inspection regression with arbitrary IDs and retired records; existing card routes share the function.
- Repair v8.8.46 handoff version mismatch and over-budget manager bootstrap; preserve all P0 criteria in required indexed history rather than raising the budget.
- Persist v8.8.45 exact publication/rollback closure and branch/UI review limits.
- Agent Control/runtime/plugin version0.6.22; no new features.

# v8.8.46 — cross-repository agent training bootstrap
- Make `fengie/mhw-mods` the canonical global training/bootstrap source before any agent works on any repository.
- Require current MHW training first, then target-repository-specific instructions/state; stale copied training may not silently substitute for the live baseline.
- Propagate the rule through `AGENTS.md`, the generic trainer, shared swarm/manager contracts, and every standalone agent role template.
- Require managers, schedulers, successors, and sub-agents to inherit the same gate, with existing authorized fallback/TRAINING-BLOCKED behavior when the canonical training source cannot be established.
- Documentation/governance-only patch; no runtime or plugin behavior change.

# v8.8.45 — maintenance consolidation and junior handoff
- Prepare bounded existing-behavior, branch-classification, installed-updater and authenticated-control reliability assignments with acceptance/escalation criteria.
- Record preserved branch ancestry without treating rebased/squashed history as proof of unique behavior or deletion authority.
- Reconcile the navigation precedent with completed exact-source release evidence; retain candidate failure history.
- Freeze new features for this maintenance cycle and preserve current runtime/plugin versions.

# v8.8.44 — Windows-safe agent ownership state
- Accept one leading UTF-8 BOM in primary/backup/legacy controller state, preserving newer tasks/leases rather than recovering an older backup for a supported encoding.
- Reuse the strict decoder during pre-save backup validation; keep BOM-free atomic writes and corrupt-state read-only/paused behavior.
- Add real isolated server/HTTP regressions for newer-primary precedence, backup preservation/interruption recovery, legacy uncertain-agent identity, and malformed/duplicate/misplaced BOM rejection.
- Strengthen the prior Windows-encoding precedent with LR-059 and generic training; Agent Control/root+nested plugin version 0.6.21.

# v8.8.43 — bounded indexed-context navigation
- Enforce actual compact CLI output, escaping and newline within the 8 KiB envelope; preserve pagination continuation without dropped lines when serialization expands content.
- Add hash-checked literal `--search` and Markdown `--heading` modes to the canonical repository-context CLI.
- Keep navigation inside indexed repository documents and existing symlink/path/source-size protections; exact indexed SHA-256 remains mandatory.
- Bound queries to one 256-byte line, results to 50, output to 8 KiB, snippets to 512 bytes, and disclose result-set/snippet truncation.
- Return stable line numbers and heading levels so agents can navigate directly into existing bounded pagination without regex execution or persistent caches.
- Advertise search/heading commands and result limits in the live bootstrap packet; advance Agent Control/plugin identity to v0.6.20 and prompt-library identity to 2026.09.30.3.
- Add focused API/CLI regressions for matching, heading selection, stale hashes, malformed queries, conflicting flags and byte/result bounds.

# v8.8.42 — current CI evidence and fail-closed release gates
- Extend Heaven Workflows to v0.4.0 with exact repo/SHA/event/branch Actions evidence through an injected authorized reader, bounded pages/responses/output and explicit provider/partial errors.
- Retain the newest provable workflow result, reject ambiguous ordering/identities and changing counts, and expire snapshots after two minutes.
- Reject conflicting same-gate histories and authoritative running states; revalidate snapshot status/freshness at publication authorization while retaining exact source/artifact/plan/confirmation rules.
- Capture the connector main-run omission in the existing PG-005 owner/plan/contract; no duplicate credentials, transport, publication or cancellation implementation.
- Add behavioral regression/pagination/provider/scope/freshness tests, precedent, LR-056 and generic training propagation.

# v8.8.41 — bounded startup and scalable context retrieval
- Add read-only Agent Control bootstrap CLI/API with exact HEAD/main identities, two-minute expiry, hash manifests, current verification scope and bounded/truncation-marked ownership.
- Add whitelisted SHA-256-checked context pagination with UTF-8 byte/line bounds, linked-path refusal, stale-source rejection and clear recovery errors.
- Compact AGENTS/current revision while retaining the complete prior policy and revision snapshot in canonical indexed/reference history.
- Enforce a 64 KiB full-read core budget (including manager training) and 32 KiB packet budget; preserve task-relevant expansion, authorization, ownership and exact-source verification.
- Reuse the already-successful inventory fetch for worker base resolution instead of performing a duplicate remote refresh.
- Exercise real Git, CLI, HTTP, stale/expired/changed source, path safety, ownership scale, manager training and budget failures; advance Agent Control/plugin to v0.6.19.

# v8.8.40 — efficient agent bootstrap
- Replace the fixed full-corpus startup reread with a compact full-read core plus hash-verified indexed continuity/training context.
- Keep task-relevant precedent, learned-rule, source, test, architecture, ownership, and verification expansion mandatory without rereading giant historical ledgers by default.
- Require pagination/chunking for truncated tool output instead of false training-blocked states.
- Treat a missing preferred CLI, one failed network route, or no local checkout as recoverable routing conditions and require authorized GitHub connector/API, canonical worktree, Heaven Bridge/Agent Control, or CI fallbacks before BLOCKED.
- Optimize senior/premium agents for architecture, root cause, review, integration, and verification decisions; offload mechanical evidence collection when practical.
- Add Agent Control regressions pinning the core/index split, indexed large ledgers, fallback language, and blocker criteria; advance Agent Control/plugin identity to v0.6.18.
- Recover the product verification repairs already isolated on #507 onto current-main lineage without its stale release metadata: CA1822 closure, WPF/trace instrumentation, planner/conflict ambiguity and update semantics, Atom/GitLab normalization, and matching regression fixtures.

# v8.8.39 — Heaven Local Bridge primary reliability
- Make Heaven Local Bridge the explicit primary control path for heaven2/heaven and keep Remote Desktop Commander as an on-demand fallback.
- Add a machine-local `HeavenBridge/auth/allow-repo-acl-only` marker consumed by worker v8 and Agent Control v0.6.17, eliminating restart-sensitive environment-only authorization for the private GitHub relay compatibility mode.
- Add `Set-PrimaryControlMode.ps1` to persist the local ACL marker and convert the RDC scheduled task to triggerless/on-demand operation.
- Ignore Python `__pycache__` / bytecode so bridge STATUS and recovery are not poisoned by harmless runtime caches.
- Preserve HMAC as the secure default; repo-ACL-only execution still requires explicit local opt-in.
- Add worker/provider regressions; advance Heaven Local Bridge plugin to v0.8.3 and product version to v8.8.39.

# v8.8.38 — Retire superseded retry parents
- Stop treating `retry-dispatched` as an unconditional managed-agent retention state after a replacement has taken ownership.
- Allow terminal superseded retry parents to flow through the existing fail-closed retirement proof, branch/worktree safety checks, durable retirement archive, and registry removal.
- Keep unresolved `retry-pending`, retry-blocked, work-incomplete, work-unverified, active task, candidate, cleanup-required, dirty, divergent, and unproven ownership states visible.
- Add regression coverage for the exact failed + retry-dispatched + superseded tombstone leak; advance Agent Control to v0.6.16 and product version to v8.8.38.

# v8.8.37 — Agent registry lifecycle closure
- Generalize managed retirement beyond retry-exhausted rows while preserving unresolved recovery/attention/candidate/cleanup states.
- Fail closed on live unowned PIDs, incomplete branch provenance, committed divergence, or dirty worktrees; preserve the affected task as needs-attention rather than hiding work.
- Archive terminal and disconnected-timeout external federated presence out of the current registry.
- Tombstone every correlated provider/source identity so stale alternate-provider replay cannot recreate a retired logical session.
- Exclude historical records from the live-registry total while retaining explicit historical counts.
- Expose bounded durable retirement history through the Agent Manager snapshot and a read-only Registry history dashboard surface.
- Add focused policy/count/UI regressions; advance Agent Control/root/nested plugin identity to v0.6.15 and product version to v8.8.37.

# v8.8.36 — Agent Manager notification + stable inspector closure
- Render backend Agent Control notifications with severity, message, timestamp, and supported inspection actions.
- Route only `inspect-agent` and `inspect-federation` through shared stable-ID inspector helpers; unsupported backend action types remain informational.
- Add a persistent managed/federated Inspector surface with provider/machine, lifecycle/recovery, task/boundary/lease, branch/PR, heartbeat/action, error/message, lineage, and valid managed actions.
- Preserve the selected inspector across normal refresh; missing/retired targets degrade to an explicit persistent state, including async managed-log retirement races.
- Replace fake keyboard semantics on generic agent articles with explicit Inspect buttons while keeping body-click convenience and nested-control isolation.
- Remove dynamic agent-ID inline handlers from managed/federated action controls and add focused executable UI regressions.
- Preserve v8.8.35 runtime freshness and v8.8.34 durable stop proof; advance Agent Control/root/nested plugin identity to v0.6.14 and product version to v8.8.36.

# v8.8.35 — Agent Control canonical runtime freshness
- Add a checkout-independent runtime sync guard used before startup restore, watchdog restart, and manual launch.
- Fast-forward only clean local `main` to canonical `origin/main`; dirty, detached, non-main, ahead, and diverged checkouts fail closed without reset/clean.
- Publish exact runtime repo/source SHA and Agent Control version through health and controller-process identity.
- Replace stale listeners only after positive persisted PID, exact server-path, and Node-process ownership proof; unknown listeners remain untouched.
- Keep runtime Git probing on the forced-hidden process wrapper and add behavioral Windows fixtures for freshness/fail-closed cases.
- Preserve v8.8.34 durable stop-proof semantics and advance Agent Control/root/nested plugin identity to v0.6.13 and product version to v8.8.35.

# v8.8.34 — Agent Control durable stop proof
- Route operator Stop and provider-capacity auto-termination through the same canonical provider resolver and durable remote termination proof.
- Require Heaven-backed workers to carry a durable remote job id and reach an explicit processed-terminal Bridge state; ambiguous cancellation/status outcomes fail closed.
- Persist a remote-termination-pending marker before termination so a local wrapper exit can record evidence but cannot classify final status, close the task, or release the ownership lease early.
- Finalize only after authoritative remote proof plus local wrapper exit proof, while preserving non-Heaven local stop semantics.
- Add focused stop-safety regressions and advance Agent Control/root/nested plugin identity to v0.6.12 and root product version to v8.8.34.

# v8.8.33 — Agent Control lightweight health
- Make `GET /api/status` a local-state-only health path instead of calling heavyweight `buildSnapshot()`.
- Keep startup restore and the watchdog on their existing `/api/status` probe while removing repository scans and Heaven Bridge synchronization from controller liveness.
- Reuse one Heaven Bridge assessment per full dashboard snapshot across worker and federation views.
- Preserve dispatch-time bridge trust/authorization checks; only advisory snapshot/health work is deduplicated or skipped.
- Add focused health-contract regressions and advance Agent Control runtime/root/nested plugin identity to v0.6.11 and root product version to v8.8.33.

# v8.8.32 — Agent Control retirement identity fail-closed closure
- Resolve persisted worker ownership through the canonical executionProvider/runtimeProvider/provider fallback before remote retirement.
- Block and preserve Heaven-backed retry-exhausted rows whose durable remoteJobId is missing or blank instead of treating proof as unnecessary.
- Add a real-server regression proving missing-ID/provider-fallback retirement becomes retry-blocked before any registry deletion or tombstone creation.
- Add migration coverage that preserves existing retired-source tombstones and retiredAt monotonic comparison boundaries.
- Preserve v8.8.31 processed-terminal proof, running-race re-cancel, and raw-heartbeat reactivation semantics.
- Advance Agent Control runtime/root/nested plugin identity to v0.6.10.

# v8.8.31 — Agent Control durable retirement safety
- Require explicit processed-terminal Heaven Bridge job proof before retry-exhausted live-registry deletion.
- Fail closed for queued/unclaimed, `not_running`, `unknown`, cancellation/status authority failure, and unrecognized remote job states.
- Reassert cancellation when a remote job races into `running`, and accept only `completed`, `done`, `failed`, `error`, `timeout`, or `cancelled` as terminal proof.
- Require a retired federated source to present a raw explicit live heartbeat strictly newer than `retiredAt` before tombstone reactivation.
- Add focused regressions for remote-stop authority, race handling, terminal-state whitelisting, raw-heartbeat monotonicity, and pre-normalization ordering.
- Preserve v8.8.30 card accessibility and advance Agent Control/root/nested plugin identity to v0.6.9.

# v8.8.30 — Agent Control card accessibility closure
- Preserve mouse and Enter/Space card inspection while removing whole-card button semantics from managed/federated status articles.
- Keep nested action controls independent so their clicks/keys do not double-trigger card inspection.
- Add executable interaction regressions for managed/federated activation, keyboard behavior, nested controls, and federated detail expansion.
- Integrate the PR #484 accessibility follow-up as its own visible patch and advance Agent Control/root/nested plugin identity to v0.6.8.
- Preserve v8.8.29 handoff-validator hardening and all earlier Agent Control behavior.

# v8.8.29 — Handoff visible-progress validator hardening
- Scope AGENTS visible-progress validation to the named `### Mandatory visible-progress versioning` section instead of matching README/patch terms anywhere in the document.
- Require that section to reference the root README, patch advancement in `VERSION.txt`, `CHANGELOG.md`, and same-change-set coupling.
- Make the existing adversarial fixture fail closed when the entire visible-progress rule is removed.
- Preserve current v8.8.28 Agent Control relay behavior unchanged.

# v8.8.28 — Agent Control Heaven relay execution repair
- Route Heaven Bridge job submission and result waiting through the same documented relay resolver used by health/preflight.
- Allow normal execution to use `~/HeavenBridgeRepo` when no explicit `AGENT_CONTROL_HEAVEN_RELAY_DIR` is supplied, while preserving explicit relay paths as authoritative.
- Close the confirmed exit-code-1 dispatch failure observed in both main and manager workers when the env var was absent.
- Preserve v8.8.26 retry-exhausted process/worktree cleanup and v8.8.27 agent-card inspection unchanged.
- Advance Agent Control runtime/root plugin/nested Codex plugin identity to v0.6.7 and cover fallback, explicit override, and absent-fallback fail-closed resolution.
- Record the provider resolver-parity precedent/rule/training and exact live verification handoff.

# v8.8.27 — Agent Manager card inspection
- Make managed Agent Manager cards inspectable by clicking the card body or using Enter/Space; the interaction opens the worker log without requiring the small **View log** button.
- Make federated cards inspectable: linked controller workers focus/open their managed card, while external-only sessions expose their current provider/state/heartbeat/recovery/action details.
- Exclude nested buttons/links/form controls from card activation so **View log**, **Stop**, **Deploy reviewer**, and **Copy branch** execute exactly their own action instead of double-triggering inspection.
- Add keyboard focus semantics and a dashboard regression that parses the emitted inline script and pins the interaction handlers.
- Preserve the v8.8.26 retry-exhausted retirement/tombstone semantics unchanged; this patch fixes the separate “click did nothing” operator path.
- Advance Agent Control runtime/root plugin/nested Codex plugin identity to v0.6.6.

# v8.8.26 — Agent Control exhausted-agent registry retirement
- Retire terminal no-work agents when their bounded recovery budget reaches **RETRY EXHAUSTED** instead of leaving failed/dead cards in the live registry.
- Prove controller ownership before terminating any still-live process; refuse destructive cleanup when PID ownership, worktree cleanliness, or branch divergence is uncertain.
- Release clean retry-exhausted worktrees and leases, remove retired logical agents from managed/federated live state, and preserve durable task/event/tombstone history.
- Suppress replayed terminal observations from retired federated sources so dead sessions cannot immediately resurrect themselves; a real live heartbeat clears the tombstone.
- Keep successful/done integration candidates intact; retirement is scoped to retry-exhausted terminal recovery failures.
- Advance Agent Control runtime/root plugin/nested Codex plugin identity to v0.6.5 and add retirement/anti-resurrection/state-migration regression coverage.
- Record the defect precedent and portable rule that terminal lifecycle state must include registry retirement, not only a status-label transition.

# v8.8.25 — Agent Control operator-markup + plugin identity repair
- Fix **Copy branch** generated markup by encoding branch names before interpolation into the inline operator handler and decoding them only when invoked.
- Add a dashboard regression that requires the encoded handler and rejects the former raw JSON.stringify(...) interpolation pattern.
- Synchronize Agent Control runtime, root ChatGPT plugin, and nested Codex plugin identities at v0.6.4.
- Extend the release-identity regression to include the nested `.codex-plugin/plugin.json` manifest so package drift fails tests.
- Record the escaped defect class and promote the reusable whole-output/mirrored-manifest validation rule.

# v8.8.24 — Agent Manager operator actions
- Surface backend-ranked Agent Manager recommendations in the dashboard instead of leaving `suggestedActions` invisible.
- Add explicit dashboard actions for resume/clear-stop, review workflow launch, usual-swarm continuation, and worker inspection.
- Offer **Deploy reviewer** only for completed managed agents; terminal failure/capacity/recovery states no longer expose an invalid review action.
- Make Resume explicitly clear an active emergency stop only after operator confirmation, with state-aware button text.
- Advance Agent Control runtime/private-plugin identity to v0.6.3.
- Record the escaped operator/server contract mismatch in the bug-precedent ledger, add LR-047, and promote the reusable operator-control closure rule into generic verification training.
- Add regression assertions for recommendation visibility/action routing, reviewer affordances, and emergency-stop recovery semantics.\n- Repair the P0 verification regression to follow live repository/runtime version metadata and correct stale v0.6.2 documentation after Agent Control advanced to v0.6.3.

# v8.8.23 — Agent Manager runtime reliability
- Fix START SWARM so a paused perpetual run resumes and advances immediately instead of being reported as already running.
- Reject active non-perpetual autopilot runs with HTTP 409 rather than falsely labeling them as an existing perpetual swarm.
- Reject an empty perpetual objective before changing autonomy, pause, read-only, drain, or emergency-stop state.
- Make dashboard Stop eligibility match every server-active managed state: reserved, starting, running, waiting, blocked, stale, and stopping.
- Serialize normal dashboard polling and sequence concurrent refresh/sync responses so stale snapshots cannot overwrite newer operator-visible state or falsely mark Agent Manager offline.
- Preserve the selected worker-machine target across periodic worker-pool renders instead of resetting it to Auto.
- Advance Agent Control to v0.5.11; add lifecycle, polling, selection-persistence, resume/conflict, and JavaScript-parse regressions; record LR-045 plus the matching defect precedent/generic trainer rule.

- Make Agent Manager / Agent Control the explicit P0 engineering priority in canonical continuity and constrain autonomous implementation/expansion to that control plane while the marker is active.
- Align the Agent Control runtime package and private ChatGPT plugin at v0.6.2, with stable-session heartbeat guidance and dedicated P0 regression coverage.
- Keep the P0 marker active until exact-head Agent Control checks plus heaven2 controller and heaven1 worker-bridge smoke are proven.

# v8.8.22 — Strict analyzer repair
- Repair six warnings-as-errors in Core/catalog code: preserve the installed-origin checker instance API with a narrow CA1822 justification, use direct indexing for indexable Thunderstore categories, and keep concrete collection types where the implementation is concrete.
- Repair four strict analyzer failures in tests: use `Assert.Single` for collection cardinality, propagate xUnit cancellation, and remove repeated constant-array allocations.
- Record the analyzer cascade as a bug precedent and add LR-042 so downstream missing-assembly errors are treated as cascade symptoms until the earliest compile diagnostic is closed.
- Require exact-candidate analyzer closure across both `MhwModManager.Core` and `MhwModManager.Tests` (or the full repository verifier) before integration-ready claims.

# v8.8.21 — Override/dependency hardening and visible progression
- Make root `README.md` progress reporting and a patch-version increment mandatory for every completed meaningful change set.
- Enforce version agreement across `VERSION.txt`, `Directory.Build.props`, README, changelog, and continuity metadata; keep evidence-only attestation/publication commits on the same patch unless they introduce an independent change.

- Require valid enabled overlay edges: the winner must be one endpoint, cycles remain blocking, and malformed precedence rules never enter resolution.
- Remove the planner's emergency priority fallback; every non-blocking multi-provider decision must identify a real candidate winner.
- Stop auto-selecting ambiguous same-family/same-lineage textures from priority alone; only high-confidence revision/role/resource evidence can auto-compose them.
- Treat MHW structural model/material/physics siblings as atomic bundles, blocking unrelated providers even when they modify different filenames in the same bundle.
- Revalidate mod/package/file/texture/native-loader requirements before Preview, Apply, normal modded launch, and last-known-good restore.
- Add adversarial regression coverage for malformed overlay rules, disjoint structural siblings, proven main→optional composition, and randomized ambiguous texture graphs.

# v8.8.20 — One-click Auto Populate

- Add lightweight, accessibility-aware transform/opacity transitions for page navigation, blocking-operation presentation, and shared button/sidebar interaction feedback.
- Respect Windows client-area animation preferences and avoid layout-dimension animation so virtualized mod lists retain performance.
- Add an **Auto Populate** button to the Mods toolbar that computes and immediately applies a deterministic maximal conflict-free installed setup.
- Preserve currently enabled choices first, then fill remaining compatible packages without blindly enabling direct replacements.
- Recursively include explicit mod dependencies, inferred main/base family packages, required file and texture providers, tracked native plugin-loader packages, and pinned shared-resource providers.
- Run every candidate together with its complete requirement closure through the existing DeploymentPlanner/ConflictEngine; dependencies and texture providers must themselves remain non-conflicting.
- Extend dependency validation to prospective staged sets and support requirements sidecars with `mods`/`dependencies`/`requires`, `files`, and `textures` entries while preserving the existing file-requirement format.
- Fail closed on ambiguous required providers, missing packages, unsafe/invalid requirement paths, independent texture-replacer collisions, and non-converging dependency state.
- Add Automation regressions for dependency + required-texture closure, independent texture alternatives, and missing packages, plus a UX regression that pins the Auto Populate entry point.

# v8.8.19 — Auto Modder foundation

- Keep updater progress reporting observational: a loading-screen callback failure is logged but cannot turn a healthy update into rollback or otherwise change update safety semantics.
- Add a dedicated updater progress window with live prepare/install/restart/health-verification stages, an animated working state, safe rollback/error messaging, and automatic close after successful handoff.
- Add the normalized Auto Mod Recipe v1 domain model and strict parser/semantic validator.
- Add bounded whole-value input/catalog-property references; arbitrary expressions, scripts, reflection, process launch, network access, and unrestricted filesystem access remain unavailable to recipes.
- Add explicit adapter registration, dotted-version range negotiation, and per-operation capability checks.
- Add deterministic typed patch-plan lowering and generated provenance-manifest serialization with ordinal dictionary ordering.
- Add a manager-owned build sandbox that reuses PathRules, enforces output file/byte budgets, writes through owned temporary files, and fails closed on duplicate/escaping paths.
- Publish the JSON Schema, adapter/IR contract, generated-manifest contract, threat model, and synthetic recipe fixture; mark M0 locked while catalog discovery, real adapter execution, publication, and WPF UI remain in-progress M1+ work.
- Add focused unit coverage for parsing/validation, expression resolution, adapter capability/version checks, unsafe outputs, deterministic manifests, and sandbox containment.

# v8.8.18 — Maximum windowed Mods workspace

- Make the Mods library consume nearly all available client space in windowed mode by cutting the page's outer padding from 60 horizontal pixels to 16 and tightening vertical spacing.
- Collapse the title, status, help text, and maintenance actions into one compact header row.
- Keep Filters and Bulk Actions on a single compact horizontal strip; at narrower widths the strip scrolls horizontally instead of wrapping into extra rows that steal library height.
- Collapse the pending-change footer to one line and preserve the detailed plan as a tooltip.
- Add a structural regression that locks in the compact windowed layout and prevents the earlier oversized margins/wrapping behavior from returning.

# v8.8.17 — Reliable mod discovery after update restart

- Preserve the canonical manager-home path explicitly across updater handoff and application restart so the first launch after an update reads the real Mods and State directories instead of an install-local empty library.
- Recover the manager home from the known project release layout when no manager-home environment variable is present, matching diagnostic-root behavior.
- Export the resolved manager home for child processes and restore both manager-home environment variables during updated and rollback launches.
- Add regression coverage for release-layout root recovery and serialized updater handoff identity.

# v8.8.16 — Larger mod library workspace

- Give the Mods library more vertical room by compacting the controls around it instead of shrinking mod rows.
- Place Filters and Bulk Actions side-by-side on wide layouts while preserving natural wrapping on narrower windows.
- Keep pending-change summary and actions on one row when space allows, recovering another row of height for the library.
- Tighten the Mods page vertical margins and add a structural UX regression covering the larger-library layout.

# v8.8.15 — True visual overhaul

- Replace the two-row command strip with a single calmer app bar that keeps game context and primary actions visible without dominating the window.
- Rebuild the Dashboard around a current-setup hero, explicit next actions, a compact four-metric strip, and separate Setup Details / Quick Actions areas.
- Replace the old gold-heavy flat panel treatment with a cohesive deep-navy and teal visual system across shared WPF cards, navigation, buttons, focus states, and action tiles.
- Preserve the existing deployment, updater, conflict-resolution, filesystem, and diagnostics behavior by reusing the same commands and bindings.
- Add a structural UX regression so the application cannot silently return to the old four-card + “System status” / “Start here” dashboard while still passing copy-only tests.
- Derive support-bundle, startup-trace, and structured-log release identity from the executing assembly so diagnostic evidence cannot silently report obsolete v8.3.0/v8.8.6 versions.

# v8.8.14 — Fail-closed Smart Inbox rollback

- Make published-package rollback report failure instead of swallowing cleanup errors after source archival fails.
- Stop Smart Inbox processing when both source archival and published rollback fail, preserving both failures instead of recording a clean skipped item.
- Preserve cancellation semantics while surfacing rollback residue as part of the cancellation failure.
- Add a deterministic Windows regression that makes the published file undeletable and proves the run fails closed.

# v8.8.13 — Repeatable recipe family restore

- Make repeated explicit family restoration from the same portable recipe idempotent instead of misclassifying the manager-created local family as a conflict.
- Recognize only a complete `imported:` family whose membership and roles still exactly match the recipe; preserve fail-closed behavior for mixed, manual, partial, or changed local families.
- Add a regression requiring the second identical restore to return zero changes.

# v8.8.12 — Import publication isolation

- Stage manual archive, Smart Inbox, and FOMOD work in a manager-owned sibling workspace outside the catalog-visible `ModsRoot`.
- Publish completed imports only after validation/normalization through one final same-volume directory move.
- Keep failed, canceled, or crash-residue staging structurally invisible to `CatalogService.RefreshFoldersAsync`, preventing persistent ghost mod rows from partial imports.
- Preserve the source Inbox item until publication succeeds and keep cleanup failures secondary to the original operation outcome.
- Add deterministic regressions for mixed failed/successful Smart Inbox runs, failed manual archive publication, and pending FOMOD preparation visibility.

# v8.8.11 — First-time user UX overhaul

- Replace ambiguous/developer-centric primary labels with explicit actions such as **Install Mod**, **Apply Mod Changes**, **Launch Game**, and **Launch Without Mods**.
- Reduce dashboard cognitive load by keeping the everyday actions visible and moving recovery, cleanup, updates, shared-file inspection, batch inbox processing, and diagnostics under **More tools**.
- Rename core mod states and filters in ordinary language, keep the active filter visible, and clarify empty/filter states so users know how to recover hidden content.
- Rework conflict copy around the actual choice—pick the mod that should win or identify a main mod plus add-ons—and move raw evidence behind **Technical details**.
- Rename the overlap explorer to **Shared Files** and the advanced effective-provider view to **File Decisions** while preserving exact technical evidence for expert use.
- Humanize game settings, optional/FOMOD installer choices, profiles, history/support, crash clues, recovery operations, and live progress/status messages.
- Increase shared default font/control/table sizing modestly for readability and larger click targets on common laptop displays.
- Add/update UX regression tests so the primary shell cannot silently regress to legacy labels such as `JUST PLAY`, `SMART VIEWS`, or `Overlaps`.
- Add `docs/UX-FIRST-TIME-OVERHAUL.md` as the durable audit and acceptance checklist.

# v8.8.10 — Workflow feature closure

- Add a consolidated Loadouts, rules & diagnostics workspace for effective-file inspection, portable recipe import/export, inherited profile comparison, relationship graphs, rule editing, update migration, stability history, and adapter diagnostics.
- Add guarded FOMOD parsing with choice visibility, dependency flags, cardinality validation, deterministic file planning, interactive import, and Smart Inbox hold behavior when installer choices are required.
- Restore portable collection recipes with captured path/hash identity, Nexus-aware matching, wrong-version/hash/ambiguity diagnostics, profile import, and explicit family restoration without redistributing mod payloads.
- Add profile inheritance/delta storage, compiled game-adapter contracts with conservative generic fallback, and workflow analysis for effective filesystem/provider changes.
- Make update migration transactional across live files and selected rule/family/provider/supersession metadata so failure recovery and Undo keep filesystem and metadata on the same side of the commit.

# v8.8.9 — Updater manifest compatibility and installed-client closure

- Accept legacy UTF-8 BOM updater manifests already frozen in immutable releases while emitting all newly published update manifests as BOM-free UTF-8.
- Route Windows release publication and installed-client updater acceptance through the verified Heaven self-hosted Windows runner when hosted allocation is unavailable.
- Sequence the real build-60 installed-client update/rollback gate after a successful Windows Release Gate and resolve the exact newly published immutable target instead of hard-coding obsolete build 61.
- Preserve sentinel user/unknown data and require deterministic rollback evidence before updater closure is accepted.

# v8.8.8 — Atomic launch-observation persistence

- Persist each observed launch-history row and all enabled-mod trust deltas in one SQLite transaction so partial trust/history evidence cannot escape a failed write.
- Capture one immutable pre-launch mod/build snapshot for both persisted history and trust updates; exact launch-ID replay is idempotent and conflicting replay fails closed.
- Preserve authoritative persistence once the external startup outcome is known, even if the initiating user cancellation token is canceled afterward.
- Add deterministic SQLite fault regressions for first/later trust-write failure, replay, snapshot identity, and exact failed-launch diagnosis linkage.

# v8.8.7 — Cross-session updater ownership

- Move the installation-scoped updater semaphore from the Windows `Local\\` namespace to `Global\\`, preventing separate interactive sessions from concurrently mutating the same writable installation.
- Preserve fail-closed ownership establishment and the existing updater journal/rollback state machine; a crashed owner releases kernel ownership rather than leaving a permanent updater lock.
- Add Windows regressions for separate-process contention, crashed-owner recovery, independent installations, and named-object type collisions, and exercise them through the installed-client integration gate.

# v8.8.6 - Profile containment and responsive UI integration

- Reject noncanonical persisted/upserted game-profile IDs before registry trust and enforce lexical containment of generic-game workspace/state roots.
- Add hostile traversal, separator, casing, and escaped-workspace regressions for the profile boundary.
- Integrate the reviewed responsive WPF UI/UX overhaul while preserving current updater, backend, and safety behavior.
- Preserve Agent Control 0.5.0 and authenticated Heaven Local Bridge execution from v8.8.5.
# v8.8.5 — Agent Control federation and release recovery

- Integrated Agent Control 0.5.0 with federated agent identity, heartbeat freshness, ownership-aware planning, routing-manifest support, and governed autonomy permissions.
- Preserved the heaven2 control/credential authority and heaven heavy-worker topology in control-plane policy.
- Fixed a duplicate federation import introduced by concurrent Agent Control integration before promotion.
- Advanced the shipped application/release identity from 8.8.4 to 8.8.5 while preserving the v8.8.4 product safety fixes.

# v8.8.4 — Support recovery hardening

- Reconcile `save_snapshots` retention with the SQLite index while limiting recursive deletion to verified direct children of the manager-owned snapshot root.
- Remove malformed, outside-root, missing, and reparse payload rows without following those paths; for valid over-limit snapshots, delete the payload first and retire its row only after deletion succeeds.
- Harden untrusted remote-preview egress to HTTPS-only public destinations, disable automatic redirects/proxy routing for preview fetches, and isolate Nexus API traffic behind a no-redirect client.
- Preserve runtime evidence and reusable doctrine for legacy-migration CAS hardlink aliasing, live-writer recovery takeover, and updater cross-session ownership without importing stale support routing or verification caches.

# v8.8.3 ? Archive streaming failure-cleanup hardening

- Attempt owned current-output cleanup for every exceptional archive payload-copy exit after file creation, including ordinary streamed I/O failures.
- Preserve the primary cancellation, budget, or I/O exception when best-effort cleanup itself fails; log the cleanup failure as secondary diagnostics.
- Re-check Smart Inbox cancellation before treating I/O, access, or archive-data exceptions as recoverable item failures, preventing canceled runs from continuing to later items.
- Add deterministic fault-injection regressions for ordinary I/O cleanup, cleanup-failure exception dominance, and Smart Inbox cancellation dominance.
- Keep whole-import staging/publication residue under LR-008 as a separate future boundary.

# v8.8.2 — Integrated support safety hardening

- Require crash-bisector empty-control and full-suspect preflight before deterministic narrowing can report an isolated culprit.
- Compensate ordinary duplicate-cleanup database-delete failures by reconciling durable row state and restoring the archived source only when safe; abrupt process-death recovery remains separate.
- Sanitize recent structured JSONL logs at support-bundle export, recursively redacting secret-like fields/assignments and absolute Windows paths while leaving local logs unchanged.
- Add generated support-bundle privacy canaries, profile-save rollback fault injection, and stronger adversarial handoff-continuity fixtures.
- Preserve new audits for persisted game-profile ID path containment and launch-observation persistence atomicity as regression-first follow-up boundaries.

# v8.8.1 — Updater publication verification hardening

- Verify newly published updater tags through GitHub's REST git-ref API instead of depending on immediate Git transport propagation.
- Fail closed unless the published ref name is exact, its target is a direct commit, its SHA is a valid 40-hex identifier, and it matches the expected source commit.
- Add regression coverage for empty, malformed, wrong-tag, non-commit, and malformed-SHA ref responses while preserving the existing pre-publication local checks.

# v8.8.0 — Function verification cache and call-error hardening

## Repair audit — 2026-09-27

- Fix generic-mode `nativePC` file exclusion and recapture missing CAS blobs on rescan.
- Avoid entering/flushing the watcher trace when no events or overflow are pending.
- Require unconditional using scopes at method entry; reject conditional, delayed,
  discarded, and short-lived traces, including similarly named fake logger types.
- Preserve the previous function checklist on parse/duplicate-ID failures.
- Reject missing manifested trusted snapshots and duplicate trusted ZIP entries.
- Include source/UI/script runtime inputs in integration-stage cache fingerprints;
  include common build targets, editor configuration, and NuGet configuration.
- Preserve hidden verification files in source ZIPs; validate a staged archive before
  replacing a previous ZIP.
- Add 18 executable regression cases and a PowerShell cache regression gate.
- Validate with SDK 10.0.401: complete strict solution build, 158 passing tests,
  11 passing self-test checks, and zero function-scan gaps. Windows validation pending.

## Checked-pass persistence revision

- Added `.verification/stage-status.json` with exact project/dependency/toolchain fingerprints so independently passing strict builds/tests stay checked and can print `PASS-CACHED` on unchanged reruns.
- Seeded only checks proven by the user's first Windows run **and** whose exact fingerprints remain unchanged: Core, Storage, Mhw, UnitTests, Benchmarks, and 79/79 Core unit tests.
- Function scan now writes a complete per-function `verified: true/false` checklist every run; unchanged trusted/exact-cache functions are checked immediately even if unrelated later stages fail.
- Fixed the FunctionVerifier Roslyn indexer parameter-list type mismatch and `FormatVersion` initializer bug found by the first Windows run.
- Fixed WPF `Thickness` constructor usage and missing `System.IO` in the game-profile editor.
- Updated automation/self-test/integration call sites to the new `GameProfile`-aware service constructors.
- Fixed strict CA1859 diagnostics in private game discovery helpers.
- Preserved the user's first Windows verification log in `_AGENT_CONTEXT/EVIDENCE/` for future agents.
- Fixed source-handoff manifest self-hashing: `SOURCE_HANDOFF_MANIFEST.json` is now deliberately excluded from its own hash inventory, and handoff preflight validates both function/stage checklist schemas.


- Added `MhwModManager.FunctionVerifier`, an SDK-Roslyn source scanner that fingerprints every explicit production executable body in `src` (methods, constructors, operators, local functions, explicit accessors, and expression-bodied properties/indexers).
- Added a persistent boolean per-function verification cache at `.verification/function-status.json`. Exact unchanged fingerprints stay `verified=true` and do not need semantic re-verification.
- Bootstrapped v8.7.0 trust from both full-file SHA-256 values and a read-only v8.7.0 source snapshot, so unchanged functions remain known-good even when another function in the same file changes.
- Changed/new functions are required to start with `MasterDebugLog.BeginMethod()` unless they are part of the tracing implementation itself.
- `MasterDebugLog` now propagates first-chance exception observations through the active scope chain. Scopes emit clean/error-check metadata (`PASS-CHECK`, `ERROR-CHECK`, or `PASS-WITH-ERROR-CHECK`) without swallowing exceptions.
- `Verify-Release.ps1` and `Build-Release.ps1` now scan function fingerprints before compilation and promote the cache only after every required verification stage passes. Failed builds preserve the last known-good cache.
- Added source-level verification documentation and agent handoff context.

# v8.7.0 — Universal game support

- Promotes the existing game-profile scaffolding into a real generic multi-game architecture.
- Adds best-effort installed-game discovery for Steam, Epic Games Store, and GOG plus the existing manual executable picker.
- Adds layout presets for BepInEx, Unreal Paks, Data-folder and Mods-folder games, with a safe generic game-root fallback.
- Keeps every game in an isolated library/database/rollback workspace.
- Fixes generic relative-path validation and generic source-package mapping.
- Generalizes process guarding, game-build revalidation, save snapshots and unmanaged live-file adoption.
- Keeps MHW semantic armor/family/texture intelligence behind the enhanced MHW profile instead of applying it to unknown games.
- Nexus domain is profile-scoped instead of conceptually MHW-only.

# 8.6.27 — Visual Source Fallback

- Fixed libraries showing `No visual available` when Nexus archives contain no screenshots and no API key is configured.
- Imports Vortex-style `pictureUrl` / image URL fields from local sidecar metadata.
- Adds a throttled public Nexus page `og:image` fallback for known MHW Nexus mod IDs, so basic thumbnails do not require an API key.
- Visual sync now reports local, sidecar/Vortex, public Nexus, and authenticated API visual counts separately.
- Accepts local GIF previews and rejects unsupported remote media types instead of saving them with a misleading `.jpg` extension.

## 8.6.26 — App compile cleanup

- Fixed `CS0103` in `ViewModels/Rows.cs` where the new visual gallery code referenced `File.Exists` without `System.IO`.
- Removed the remaining CA1869 test warning by caching test JSON serializer options.
- No behavior changes to deployment, family inference, conflict resolution, visuals, updates, or issue fallback.

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

## v8.6.22 - Texture family regression fix

- Fixed unrelated texture replacers in the same `mod_*` resource namespace being mistaken for one lineage when they only shared generic words such as `recolor`.
- Proven same-family texture providers now resolve deterministically by family priority instead of becoming a blocking self-conflict.
- Manual family-chain precedence remains authoritative because explicit overlay rules are evaluated before this fallback.
- Cleans CA1859/CA1854/xUnit2009 warnings surfaced by the v8.6.20 verifier.

## v8.6.20 - Manual family chaining
- Added a conflict-screen action to make any conflicting logical mod the family main and chain all other conflict candidates beneath it as optional/component children.
- Manual chains persist an authoritative `manual:` family ID, explicit Main/Component/Optional roles, and an ordered explicit overlay chain across the selected logical groups.
- Optional groups are ordered by current mod priority and saved as a total precedence chain, preventing later optional layers from conflicting with earlier optional layers on shared base files.
- Manual chaining preserves current staged enable/disable state, rejects precedence cycles transactionally, and never moves or merges source folders.
- Added `[FAMILY-MANUAL]` master-log events and regression coverage proving a previously blocking structural collision becomes an explicit nonblocking optional overlay after chaining.

## v8.6.19 - Family conflict invariant

- Fixed the v8.6.18 automation regression test string literals that caused CS1010/CS1003 build failures.
- Added a hard conflict-engine boundary for proven logical families: base/optional/patch/component overlaps can no longer fall through to ordinary HARD_STRUCTURAL/HARD_GAME_DATA conflicts.
- High-confidence family overlays still auto-compose with the child/optional provider winning only shared paths.
- Generic same-family subset packages (>=75% overlap of the smaller package) auto-compose even when future authors use unfamiliar names.
- Ambiguous mutually-exclusive sibling variants remain safe: they surface as one MOD_FAMILY_OPTION internal choice instead of being silently mixed or reported as unrelated mods.
- Explicit incompatibility, exact-file winner, and resource-provider rules retain higher authority.
- Added FAMILY-CONFLICT master-log events for every internal family compose/choice decision.
- Added regression tests covering base+optional, unknown-name subset components, ambiguous sibling variants, and explicit same-family incompatibility.

# Changelog


## v8.6.18 - Generic family inference
- Replaced the HPN-centric family patch with generic evidence-based family inference.
- Family evidence now layers manual/manager metadata, shared Nexus/source identity, semantic names, file overlap/subsets, content roots, resource namespaces, and MHW asset/model identity.
- Reads Vortex/Nexus-style logicalFileName/familyId hints from local sidecars when available.
- Nexus file categories no longer decide family identity; shared source lineage is stronger evidence.
- Added conservative negative evidence so similar names or shared armor slots alone do not collapse unrelated replacements.
- Logs accepted generic family pairs with score and evidence in MHW-DEBUG-ALL.log.

## v8.6.17 - HPN / Nexus family inference

- Treats a shared game-scoped Nexus mod ID as strong logical-family evidence even when every Nexus file is author-labeled `Main`.
- Persists Nexus-family hints for all current packages on the same Nexus mod page while preserving non-Nexus/manual family assignments.
- Expands HPN component-name recognition for topless/braless/nude/panties/underwear/nipple, coatless/sleeveless and similar option packages.
- Adds explicit `[FAMILY] GROUP` / `[FAMILY] SPLIT` evidence to `MHW-DEBUG-ALL.log`.
- Adds regression tests for all-Main HPN Nexus packages, different Nexus IDs, and `Free the Nipples` suffix grouping.

## v8.6.16 - WPF trace listener nullability fix

- Fixed CS8765 in `WpfMasterTraceListener.TraceEvent` by matching the .NET 10 nullable `string? format` override contract.
- Null trace format strings are normalized to `string.Empty` before invariant formatting.
- Full-process tracing and all v8.6.15 trace-placement protections remain unchanged.

## v8.6.16 - Trace placement compiler fix
- Fixed the exhaustive tracer inserting method-scope statements into object/collection initializers.
- Removed 14 illegal trace statements while preserving all valid method/process/database/filesystem/WPF tracing.
- Added `Test-CSharpTracePlacement.ps1` to Build/Test/Verify launchers.
- Added an integration regression test preventing this instrumentation corruption from returning.

## v8.6.14 - Full-process master tracing

- Adds dependency-free `MasterDebugLog` tracing in Core so every project layer can write to the same top-level `MHW-DEBUG-ALL.log`.
- Instruments ~300 block-bodied methods across Core, Storage, Filesystem, MHW, Diagnostics, Automation, and App with nested operation IDs and timings.
- Captures every first-chance exception before it is caught, plus unhandled AppDomain/UI/task failures.
- Mirrors WPF `PresentationTraceSources` (bindings, dependency properties, markup, resources, routed events, shell) into the master log.
- Tracks every external process launch/exit used by the manager, including MHW, diagnostics PowerShell, and texture conversion.
- Adds file-by-file deployment mutation records, FileSystemWatcher events/errors, SQLite transaction boundaries, ViewModel property/collection changes, assembly loads, and process exit.
- Keeps build/verifier/startup/runtime logging in the same root handoff file.
- Logging remains best-effort: master-log I/O failures are swallowed so diagnostics cannot become a product failure.

## v8.6.13 - WPF read-only binding safety

- Fixed startup UI crash caused by inline `Run.Text` bindings attempting TwoWay/OneWayToSource updates into read-only/computed ViewModel properties.
- `ComposedCount`, `RevalidationCount`, `WinningFiles`, and `ShadowedFiles` inline bindings are now explicitly `Mode=OneWay`.
- Added an integration regression guard requiring every inline `Run.Text` binding in `MainWindow.xaml` to explicitly use `Mode=OneWay`.
- Retains unified root-level `MHW-DEBUG-ALL.log` and traced startup lifecycle.

## v8.6.12 - Startup lifecycle + root-log continuity

- Keeps WPF in `OnExplicitShutdown` mode until the real main window has initialized and been shown.
- Moves initial `MainWindowViewModel.InitializeAsync()` into the traced startup pipeline instead of an `async` `Loaded` event.
- Converts dispatcher-unhandled exceptions into an explicit logged error dialog + controlled shutdown.
- Adds very-early `APP-BOOTSTRAP` tracing before the splash workflow.
- When running a locally built app under `release\MHW-Manual-Mod-Manager-v*`, automatically writes runtime diagnostics back to the project-root `MHW-DEBUG-ALL.log`.
- Adds root-level `RUN BUILT APP.bat`, which sets `MHW_MANAGER_HOME` and `MHW_MASTER_DEBUG_ROOT` so the built app uses the project-root Mods/State and the same one-file debug log.
- Cleans diagnostic/test analyzer warnings (CA1068 / xUnit2031).

## v8.6.11 - Unified master debug log

- Adds `MHW-DEBUG-ALL.log` at the top level as the single file to share for debugging.
- Build, verification, per-stage dotnet output, startup tracing, runtime Serilog events, telemetry signals, and unhandled WPF/AppDomain/task exceptions append to the master log.
- Existing detailed `BuildLogs`, `StartupLogs`, JSON reports, and rolling runtime JSONL logs are retained.
- Adds `OPEN MASTER DEBUG LOG.bat` for one-click access.
- Build release copies the master log into the published release folder so subsequent app startup/runtime entries continue in the same obvious location.
- Logging remains best-effort: a log-write failure never becomes an application/build failure.

## v8.6.10 - Build harness exit-code fix

- Fixed `Build-Release.ps1` stage execution so `Tee-Object` console output cannot contaminate the function return value.
- `Invoke-DotNetStage` now returns exactly one scalar integer exit code while still streaming output to both the console and per-stage log.
- Added an explicit scalar-exit-code contract check before stage results are evaluated.
- Keeps the v8.6.9 CA2016 fix, RID compile gate, startup diagnostics, and ReadyToRun publish diagnostics.

## v8.6.9 - Publish analyzer fix

- Fixed the win-x64 publish-only CA2016 failure in `App.xaml.cs` by forwarding the startup diagnostic cancellation token into `ManagerDatabase.InitializeAsync`.
- Added an explicit `App win-x64 compile/analyzers` gate to `Build-Release.ps1` so RID-specific compiler/analyzer failures are caught before `dotnet publish`.
- Retains v8.6.8 build/publish diagnostics, ReadyToRun restore/fallback handling, and v8.6.7 startup diagnostics.

## v8.6.8 - Build/publish diagnostics and ReadyToRun recovery

- Adds per-stage BuildLogs for restore, compile, tests, self-test, RID restore, and publish.
- Explicitly restores win-x64 assets with PublishReadyToRun=true before the ReadyToRun publish.
- If the SDK/runtime-pack ReadyToRun phase fails, retries a self-contained non-R2R publish and records PUBLISH FALLBACK.txt instead of losing the entire release build.
- Build failures now print the first diagnostics and exact log path.
- Keeps the full v8.6.7 startup diagnostic tracing.

# 8.6.6 - Startup path classification fix

- Fixed startup failure when uncaptured mod folders contain root-level documentation such as `Troubleshootings.txt`.
- Auto-category classification now accepts safe source-relative paths without requiring the deployment-only `nativePC\` / `root\` prefixes.
- Unsafe rooted/traversal-style paths are ignored during classification rather than reaching deployment path normalization.
- Added regression coverage for documentation-only folders and startup categorization before capture.

# 8.6.5 - Final App compile/analyzer cleanup

- Fixed the logical-provider tuple to preserve the `identity` element name in both conditional branches.
- Removed the redundant always-true `ConflictRow.Blocking` property and made blocker count use the blocker-row collection count directly.
- Replaced LINQ `FirstOrDefault` calls on the indexable logical-member list with direct indexed lookup to satisfy CA1826 without changing winner selection.
- Marked `NexusMetadataService.TryFetchNexusAsync` static to satisfy CA1822.
- No planner, deployment, persistence, conflict-resolution, or test semantics were weakened.

# 8.6.4 - Final compile cleanup

- Fixed `CS1009` in the metadata-sync status message by escaping the Windows path `State\Next\nexus-api-key.txt` correctly in the C# string literal.
- Marked the two Nexus lookup helpers static to satisfy `CA1822`; behavior is unchanged because they only use static HTTP helpers/data.
- Replaced a repeated constant-array assertion in `AutoCompatibilityTests` to clear `CA1861` without weakening the test.
- No planner, deployment, storage, conflict-resolution, or migration behavior changed in this patch.

# 8.6.3 - Analyzer + regression repair

- Fixed strict-analyzer regressions in Core private helpers by using the concrete array types actually passed by the intelligence engine.
- Fixed invariant timestamp serialization in filesystem journaling/blob registration (`CA1305`).
- Tightened private deployment helper collection types to match their concrete call sites (`CA1859`) without changing public APIs.
- Tightened the private Nexus family-hint input and App analysis pipeline collection types for the same strict analyzer rule.
- Corrected the independent-texture regression test: an unresolved independent replacement is a blocking `TextureOverride`, not a resolved `SharedTexture`.
- Updated the verifier to print the first compiler/analyzer/test diagnostics directly after each failed stage while still continuing all gates.

## 8.6.2 - Harness continuation + test regression fixes

- Fixed the Windows PowerShell 5.1 verifier so failing native `dotnet` stages are recorded and the harness continues instead of being terminated by stderr/ErrorRecord conversion.
- Replaced the verifier's generic `List[object]` report accumulator with a plain PowerShell object array to eliminate the `Argument types do not match` JSON/report binder failure.
- Added a report-serialization preflight stage so the harness tests its own Markdown/JSON data path before the .NET matrix starts.
- Fixed AutomationTests and SelfTest compile regressions where a helper named `File(...)` shadowed `System.IO.File`; the helper is now `ModFile(...)`.
- Updated stale Core texture tests to the current policy: unrelated exact-path texture replacers require one compact human choice, while proven shared texture families remain deterministic/non-blocking.
- Preserved warnings-as-errors and the continue-through-all-stages verification policy.

## 8.6.1 - Verifier parser/encoding fix

- Fixed Windows PowerShell 5.1 parse failure in `Verify-Release.ps1` caused by a UTF-8 em dash being decoded as a curly quote under legacy code pages.
- Active build/test PowerShell scripts are now ASCII-safe so Windows PowerShell 5.1 cannot corrupt punctuation during parsing.
- Added a PowerShell parser sweep to the full verifier; every active script is syntax-checked and recorded as its own verification stage.
- Added root-launcher syntax preflight so a broken verifier is detected before execution with a direct parser error instead of a cascade of misleading failures.
- `Test Everything.bat`, `Verify.bat`, and `Build.bat` now preserve the real exit code.

# v8.5.0 — Compatibility intelligence / hands-off resolver

- Added Nexus-aware provenance and lineage. Local sidecars/folder metadata work offline; optional live Nexus v3 enrichment can identify Main, Optional, Update, archived/old versions, upload time, and version chains.
- Nexus evidence outranks filename heuristics for automatic main → optional → update precedence and supersession.
- Logical mods now expose an internal configuration drawer while preserving one-click family enable/disable. Physical source packages remain immutable.
- Added atomic MHW asset-bundle grouping for model/material/physics/game-data conflicts so direct-alternative choices select a coherent logical mod rather than mixing unrelated structural providers file-by-file.
- Replaced pairwise conflict spam with one-of-N choice cards grouped by atomic asset bundle. Selecting a logical winner stages competing logical alternatives OFF; no live files change until Apply safely.
- Added superseded-revision tracking. Conclusively older Nexus versions and safe local texture revisions are hidden from the normal library/conflict graph but retained as archived source members for provenance/rollback. Structural v1/v2 packages are not auto-archived from names alone.
- Added unmanaged `nativePC` adoption. Manual live files are copied into an immutable tracked source package without modifying/deleting the live tree; adopted path + SHA-256 state prevents repeated adoption and re-surfaces the file if it is later changed externally.
- Added confidence/evidence to automatic resolver decisions and persisted resolver audit records. Low-confidence unrelated replacements remain human choices rather than silent priority wins.
- Added best-effort texture-choice previews from package/adjacent images, with optional raw `.tex` conversion when `MHW_TEX_CONVERTER` and `TEXCONV_EXE` are configured.
- Added game-build fingerprinting. When `MonsterHunterWorld.exe` changes, plugin/executable/game-data mods are marked for revalidation while ordinary texture-only mods are not blanket-invalidated.
- Added effective-mod state projection: Effective, Composed, Superseded, Pick one, Revalidate, or Disabled, including winning/shadowed file counts.
- Startup now counts unadopted live files and surfaces the count on the Dashboard.
- Added intelligence regression coverage for Nexus optional/update precedence, version supersession, independent texture alternatives, HPN dedicated/newer texture providers, structural atomic bundles, and superseded-source exclusion.

# v8.4.0 — Logical mod families / direct-replacement-only choices

- Collapsed recognizable main + Top/Waist/Legs/No Cape/Open Top/optional/patch/fix/update packages into one logical library row while preserving every physical source folder.
- A logical-family toggle expands to all underlying source packages at planning/apply time; no source folder is rewritten or deleted.
- Existing profiles with partially enabled family members are preserved as a PARTIAL state until the user deliberately toggles the family.
- Dedicated texture revision families such as HPN skin v1/v2/Updated collapse into one logical entry; newest-provider inference still determines the actual winning texture bytes.
- Arbitrary versioned structural mods are deliberately not collapsed merely because they share a base name.
- Alternative/Alt/Variant packages remain separate pick-one logical mods rather than being misclassified as additive components.
- Needs Attention is projected onto logical mods instead of raw source folders. Blocking N-way collisions are decomposed into compact pairwise direct-replacement choices.
- Choosing Use A / Use B stages the chosen logical mod ON and the other logical mod OFF, avoiding accidental hybrid alternatives.
- Rare direct collisions between two components inside one logical family are surfaced as a component-level pick instead of inventing an unsafe binary merge/order.
- Added logical-family regression tests for HPN multipart groups, alternatives, HPN texture revisions, unrelated HPN armors, and structural version separation.

# v8.3.1 — Smart composition / provider precedence

- Added deterministic provider ordering for intentional MHW overwrite workflows: main/base → optional component → patch/fix/update.
- Dedicated texture/skin packs now beat stale incidental texture copies embedded in armor packages while every source mod remains enabled.
- Related texture revisions use Nexus-style upload timestamps, date/version labels, explicit Updated/Fix wording, and finally file revision time before falling back to configured priority.
- HPN/UHPN/HHPN `mod_hepsy` resources receive lineage-aware texture revision handling without applying timestamp ordering to structural files.
- Explicit exact-file winners, pinned resource providers, incompatible rules, and remembered human overlays remain authoritative over all automatic inference.
- Same-label structural packages (for example multiple `Fatalis Patch` archives) no longer auto-order merely because one is smaller/newer.
- `Alternative` / `Alt` / `Variant` naming alone is intentionally not enough to auto-order structural files.
- Added Nexus archive suffix metadata detection for newly discovered local mod folders.
- Added regression coverage for HPN optional components, dedicated texture providers, v1→v2 texture updates, explicit resource pins, ambiguous alternatives, and same-label patches.
- Source mods remain immutable. The manager still performs a virtual merge into one final deployment tree; format-aware binary splicing is not attempted.

# v8.3.0 — Automatic compatibility composition

- Added research-backed automatic base/option/patch inference. High-confidence families are composed without human conflict prompts.
- Overlay chains now resolve 3+ providers (`base -> option -> hotfix`) when every provider is ordered.
- Shared/different textures remain non-blocking: all mods stay enabled while one exact-path provider is selected for the composed tree.
- Pair-overlap indexing now works even when more than two mods share a path.
- Explicit user incompatibility/overlay rules always beat inferred rules; auto edges that would create cycles are skipped.
- Blocking conflict UI is compacted by provider pair + conflict kind instead of showing one row per path.
- Group-level exact winner actions now apply to every file in the compacted conflict.
- Added `docs/AUTO-COMPOSITION.md` documenting the safety model and MHW research basis.
- Source folders remain immutable; this release performs a virtual merge rather than unsafe binary splicing.

## 8.2.0 - Modern UI
- Rebuilt the WPF visual system around a restrained MHW-inspired charcoal/gold palette.
- Added a persistent left navigation rail with clearer selected/hover states.
- Reworked the command bar so high-frequency actions are visually prioritized.
- Rebuilt Dashboard with metric cards, system-status panel, quick actions, and conflict-policy guidance.
- Reworked Mods into a denser library view with integrated search, two-line mod identity, staged-state pill, and cleaner table hierarchy.
- Refined Needs Attention, Profiles, Outfits, and Activity screens with consistent page headers, cards, toolbars, and data tables.
- Added consistent rounded inputs/buttons/cards, improved typography and spacing, table hover/selection states, and a cleaner busy overlay.
- Redesigned the startup splash to match the main shell.
- Made release artifact naming derive from VERSION.txt to prevent stale versioned output paths.
- No deployment, migration, conflict-resolution, profile, or filesystem semantics were intentionally changed by this UI pass.

# 8.1.7 Recovery Diagnostic Fix

- Fixed the only failing integration/fault-injection test after the full solution compiled cleanly.
- Recovery already failed closed and preserved the externally edited file; the top-level exception was replacing the precise inner diagnostic with a generic summary.
- `RecoverIncompleteAsync` now preserves the exact recovery reason in the user-facing exception while retaining the original exception as `InnerException`.
- Strengthened the regression test to verify both the top-level actionable reason and the preserved inner diagnostic.
- No deployment/recovery safety rule was weakened.

# 8.1.6 App Compile Fix

- Fixed the full remaining App compile surface exposed by the all-project verifier.
- Added explicit `System.IO` imports for App startup/path discovery and MainWindowViewModel.
- `App` now implements `IDisposable` and centrally disposes DispatcherWatchdog, FileChangeHintService, and the logger during shutdown.
- `MainWindowViewModel` now implements `IDisposable`; busy/search cancellation sources are cancelled and disposed when the window closes.
- MainWindow now disposes its ViewModel on `Closed`.
- Changed the hot `BuildAnalysisAsync` stage parameter to the concrete `Dictionary` type requested by CA1859.
- Fixed the last xUnit1051 integration-test call by passing `TestContext.Current.CancellationToken` into synchronous archive extraction.
- Preserves the all-project compile/analyzer sweep and automatic BuildLogs introduced in 8.1.5.

# v8.1.5 — All-errors compile sweep + Diagnostics/Integration fixes

- `Verify-Release.ps1` now performs a relaxed whole-solution dependency build followed by a strict per-project compiler/analyzer sweep. Analyzer errors in one project no longer prevent the verifier from exposing analyzer errors in downstream projects.
- Every compile sweep writes a transcript, summary, relaxed binlog, final binlog, and one binlog per project under `BuildLogs\`.
- Fixed Diagnostics Serilog JSON file sink overload usage.
- Reused a static `JsonSerializerOptions` instance in support-bundle exports and made count conversion invariant-culture safe.
- Integration tests now propagate `TestContext.Current.CancellationToken` to cancellable APIs instead of triggering xUnit1051 across the fixture suite.
- Proactively made process launching disposable-safe and invariant-culture conflict-rule timestamps in the WPF app.
- Version bumped to 8.1.5.

# v8.1.4 — Compile Fix 4

- Fixed the two remaining `ArchiveInspector` CA1822 warnings while preserving it as an injectable service.
- `Verify-Release.ps1` now automatically writes timestamped text transcripts and MSBuild `.binlog` files to `BuildLogs\`.
- Version metadata updated to 8.1.4.

# v8.1.3 Compile Fix 3

- Updated SharpCompress 0.50.x usage to `ArchiveFactory.OpenArchive`.
- Fixed .NET 10 `XxHash3` usage (`XxHash3` is not `IDisposable`).
- Reordered public `CancellationToken` parameters to satisfy CA1068 and updated callers.
- Updated Restart Manager P/Invoke to use a character buffer and check `RmEndSession` results.
- Removed CA1859 hot-path interface abstractions in the scanner where concrete types are guaranteed.
- Hoisted armor CSV newline separators to a static readonly field.
- Preserved DI-friendly instance services with narrowly documented CA1822 suppressions.


## 8.1.3 - Analyzer/compile pass 2

- Fixed locale-sensitive `DateTimeOffset.Parse` in profile loading with `CultureInfo.InvariantCulture`.
- Fixed locale-sensitive migration backup timestamp formatting with `CultureInfo.InvariantCulture`.
- Moved BenchmarkDotNet entry/types into the `MhwModManager.Benchmarks` namespace.
- Scoped CA1707 off only in xUnit test projects so descriptive underscore test names remain readable; production analyzers stay strict.
- Replaced collection-membership `Assert.True(...Any(...))` with xUnit's `Assert.Contains` predicate assertion.
- Fixed invariant-culture formatting in stress-test hash generation.


## 8.1.1 - compile/analyzer fixes
- Fixed nullable-flow warning in `RuleGraph.FindCycle` by materializing validated winner/left/right IDs before graph insertion.
- Fixed `CS1628` in armor-component parsing by avoiding capture of the `out modelId` parameter in a lambda.
- Replaced single-character `StartsWith(string)` calls with `StartsWith(char)` to satisfy CA1865.
- Kept `ConflictEngine` as an injectable instance service and documented/suppressed CA1822 for the indexed overload.
# v8.1 Hardened

This release is a production-hardening pass over the C#/.NET 10 v8 rewrite. It does not intentionally weaken conflict or recovery guarantees for speed.

## Correctness / recovery
- Reworked deployment around explicit `Prepared -> Applying -> FilesWritten -> StateCommitting -> Committed` states.
- Added whole-plan live-file preflight before first mutation and a second per-file TOCTOU check immediately before each write.
- Committed manifest, original-file ownership, enabled/priority state, and the `Committed` marker in one SQLite transaction.
- Added deterministic startup rollback for incomplete transactions and `RecoveryRequired` fail-closed behavior when a file matches neither known transaction image.
- Preserved first-takeover capture of unmanaged/manual files and ownership release after restoration.
- Hardened existing-file replacement with flushed same-directory temp files and Windows `ReplaceFileW` semantics.

## Performance / responsiveness
- Removed SQLite shared-cache usage; retained WAL with pooled connection-per-operation and short transactions.
- Added indexed conflict-rule lookups and sparse incompatibility adjacency instead of repeated raw-rule scans.
- Removed a hidden O(paths²) planner lookup by indexing decisions by normalized path.
- Restricted planner snapshot file loading to staged-enabled mods.
- Added indexed `mod_file_armor` metadata so outfit coverage avoids wildcard path scans.
- Batched/versioned the 663-row armor catalog import instead of repeating hundreds of DB operations every startup.
- Added `ObservableRangeCollection.ReplaceAll` and kept WPF DataGrid virtualization/recycling enabled.
- Moved planner/conflict CPU work, archive work, source enumeration, health hashes, and startup migration work away from the Dispatcher.

## Cache / filesystem safety
- Metadata cache hits are verified with XXH3 so same-size/same-timestamp source edits cannot silently reuse stale SHA-256 blobs.
- FileSystemWatcher is hints-only and overflow is explicitly recorded; live hashes/manifests remain authoritative.
- Archive extraction rejects traversal, rooted/device/ADS paths, reparse-point escape, excessive file counts, and excessive expanded size.
- Added Restart Manager lock-owner diagnostics for Windows sharing/permission failures.

## Diagnostics
- Added correlation IDs, structured operation telemetry, runtime/ThreadPool/GC counters, classified error reports, and a Dispatcher heartbeat watchdog.
- Added one-click support-bundle enrichment and a `Capture diagnostics` UI action.
- Added `scripts/Capture-Diagnostics.ps1` for `dotnet-stack`, `dotnet-counters`, `dotnet-trace`, and `dotnet-gcdump` collection when installed.
- Added `docs/BUG-AUDIT.md` and `docs/DIAGNOSTICS.md`.

## Tests / gates
- Added crash-phase matrix tests across the durable commit boundary.
- Added stale whole-plan preflight regression test.
- Added same-size/same-timestamp scanner regression test.
- Added archive traversal/path normalization tests.
- Added randomized deterministic planner invariants and large shared-resource incompatibility coverage.
- Added `scripts/Verify-Release.ps1` and strengthened `Build-Release.ps1`.

# v8 Next

- Replaced the normal PowerShell runtime with a typed C#/.NET 10 architecture.
- Added SQLite/WAL indexed persistence and v7 schema migration.
- Added immutable content-addressed blob store, streaming hash/copy pipeline and cached XXH3 metadata.
- Ported typed conflict semantics to an indexed one-pass provider model.
- Added overlay precedence cycle detection.
- Added atomic/journaled deployment with live precondition verification and crash rollback.
- Added first-takeover protection for unmanaged/manual files.
- Added WPF/MVVM GUI with staged changes and virtualized grids.
- Added safe archive inspection, armor coverage, diagnostics/support bundles and manager-controlled Safe Mode.
- Added unit/integration/benchmark projects and documented failure invariants.

## 8.6.0 — Automation + reliability

- Added `MhwModManager.Automation` as a separately testable convenience/orchestration layer.
- Added Smart Inbox (`Inbox\` → automatic safe import → `Inbox\Processed\`).
- Added automatic content-derived categories and dependency checks.
- Added rolling pre-launch save/mod/deployment snapshots and Last Known Good tracking.
- Added `JUST PLAY`: auto-apply staged changes, adopt safe unmanaged `nativePC` files, snapshot, health gate, launch observation, trust history, and Last Known Good recording.
- Added one-click automatic startup crash bisection for newly enabled mods; surviving probes are closed and the original setup is restored afterward.
- Added update diff logging, game-update impact reports, timeline events, safe duplicate/superseded archive cleanup, collection recipe export, effective-file provenance, asset heatmap service, outfit preset inference, and per-mod launch trust history.
- Added schema v4 tables for automation timeline, save snapshots, launch history, and mod trust.
- Added `MhwModManager.AutomationTests` and a standalone `MhwModManager.SelfTest` executable.
- Reworked `Verify-Release.ps1` into a continue-on-failure whole-application test harness. One broken project/test no longer prevents later test groups from running.
- Verification now creates per-stage logs plus final Markdown/JSON reports with failure excerpts.
- Added `Test Everything.bat` as a one-click full test entry point.

8.6.7 — Startup diagnostic trace
- Added verifier-style startup text and JSON diagnostics under StartupLogs.
- Records path discovery, service composition, database init, migration, catalog/armor import, intelligence, recovery, automation maintenance, main-window construction, and watchdog startup.
- Startup maintenance attempts each safe substage independently and aggregates failures after collecting diagnostics.
- Startup failure dialogs now show exact startup log/report paths.
- Auto-category diagnostics identify the mod/source currently being classified.

## 8.8.0 handoff-hardening revision

- Added mandatory propagating `_AGENT_CONTEXT` continuity protocol and machine-readable handoff manifest.
- Added handoff preflight and clean source-handoff packaging with per-file hashes.
- Excluded generated `bin`/`obj` C# from function verification.
- Added trusted v8.7 snapshot hash/self-consistency validation.
- Prevented explicit-interface function-ID cache collisions.
- Added explicit call-site coverage counts/gate to function reports.
- Made full first-chance exception stack logging opt-in while preserving per-scope exception observation and aggregate counts.
- Documented the re-audited architecture debt and research-backed follow-up recommendations.

## 8.8.0 verification-closure revision

- Consumed the second authoritative Windows verifier run: 24 PASS / 1 FAIL.
- Confirmed relaxed and strict whole-solution builds at 0 warnings / 0 errors.
- Confirmed Automation tests 18/18, Integration + fault injection 43/43, and full automation self-test PASS.
- Fixed the sole remaining function-verifier failure by adding entry traces to `GameProfileEditorWindow.AddField`, `GameProfileRegistry.DiscoverSteam`, `DiscoverEpic`, and `DiscoverGog`.
- Preserved exact-input known-good stage checks and added the independently verified FunctionVerifier strict-build check; dependency-invalidated checks will rerun automatically.
- Added the second Windows debug log to durable agent evidence.
