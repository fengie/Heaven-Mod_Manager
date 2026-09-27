# Complete session handoff — workflow expansion

This is durable project context, not a substitute for reading the current source and Git history. Written at the user's explicit request to transfer as much relevant knowledge as possible before session credits expire. No prior chat is needed to continue.

## User message and standing instructions

The user requested all twelve features listed below, directly in `fengie/mhw-mods`, after reading the existing training/research materials and fixing/optimizing the app. The user asked to use relevant installed plugins and subsequently asked for Medium reasoning. An agent cannot change the application's model/reasoning selector; concise progress updates were used. Do not claim to have changed that setting.

The user's later instructions, preserved verbatim:

> keep going. from now on, push at every checkpoint or so and log your plan/ what you did so the next agent knows what to do before i run out of credits.

> pass the message and all your knowledge to the git

> remind future agents to do the same. add as much knowledge as possible

Act on these instructions throughout future work. At meaningful checkpoints, push code and context together, including incomplete work labeled honestly. Keep the current plan, completed work, exact evidence, known limits and next action discoverable from CHECKPOINT.md. Do not leave the only copy in a transient runtime. Ask only when necessary; ordinary fixes, tests, branch commits, and checkpoint pushes are already authorized. Do not interpret these instructions as authorization to merge/release, download mod payloads without permission, send unrelated messages, or load untrusted code.

Use relevant tools rather than invoking unrelated plugins merely to say they were used. This session used the connected GitHub integration for private repository reads, Git data writes, PR creation and CI logs, the local .NET/PowerShell toolchains for implementation/validation, and public FOMOD documentation for format research. No private source was sent to research services. No sub-agents were used.

## Remote state and authoritative history

- Private GitHub repository: `fengie/mhw-mods`.
- Canonical base branch: `main`.
- Active feature branch: `codex/complete-mod-workflows`.
- Draft PR: https://github.com/fengie/mhw-mods/pull/1 . It is open, not merged. Do not assume a draft PR means the automated tests failed.
- Main at start of work: `91f2b9e9815caab2e4670084022a5c2ab3e45f55` — Add MHW app icon branding.
- Current version remains `8.8.0`; this is unreleased workflow expansion, not a newly packaged release.
- The earlier user ZIP was superseded as canonical source by GitHub. Do not restore an old ZIP over this branch.

| Remote commit | Purpose |
| --- | --- |
| `02fa1dd5c3e98d319067043598bfdfd32617e65f` | Production implementation, tests, Windows CI and detailed Linux evidence |
| `c4c6db8521358e1af778b19456411d638b62c0dd` | Exact verification revision metadata |
| `172d22118f2b33feb03b56b6971358195f628aa3` | User's checkpoint-push requirement in AGENTS/protocol; continuation checkpoint |
| `a11ca39bcf5ba7fed6593d767dfa3a2e427d3209` | LF checkout policy fixing Windows-only function fingerprint drift; native test evidence |
| `66a48bf8f00f212d0943bdc57d8074433392a839` | Deduplicate CI and skip documentation-only rebuilds |
| `4555d97e1e79764dd70c59b6ada5c5dd071b948e` | Passing Windows CI evidence and remaining acceptance steps |

This handoff is an additional documentation checkpoint after those commits. Determine its SHA from the branch/history rather than inventing a self-referential commit ID. Re-read the remote branch before each push; other sessions may have advanced it.

All 221 remote blob hashes matched the tested local working files after the initial publication. Later changes were incremental, explicit checkpoints. Main was not modified by this work.

## Required reading and continuity

Read AGENTS.md, NEXT-AGENT-START-HERE.md, CURRENT_REVISION.json and CONTINUITY_PROTOCOL.md. Then CHECKPOINT.md, this document, WORKFLOW_IMPLEMENTATION.md, docs/WORKFLOWS.md and the normal README_FIRST read order. Existing architecture/research documents remain relevant; historical counts/statuses are superseded by the newer evidence sections.

Do not break transactional deployment, rollback, content-addressed storage (CAS), path safety, or fingerprint verification. Never manually promote function booleans. Preserve the generated icon at `src/MhwModManager.App/Assets/MHWModManager.ico`, its executable resource declaration, and the App.xaml Window style.

## Architecture and integration map

The application is C#/.NET 10 with WPF and CommunityToolkit.Mvvm. Directory.Build.props enables nullable, analyzers and warnings-as-errors. Existing package versions were preserved. Core and Storage target net10.0; the desktop/automation/filesystem and associated tests use net10.0-windows10.0.19041.0. It is a desktop app, not a website; web-browser verification cannot establish WPF correctness.

`ManagerDatabase` owns SQLite. `LoadPlannerSnapshotAsync` loads mods, captured files, rules, resource pins, deployment manifest and originals. `DeploymentPlanner.Build` is the authority for desired winners and blockers. Its `Conflicts` list contains decisions for normal/single-provider paths too; do not treat every decision as an unresolved conflict. Enabled, non-superseded mods are the active providers. A blocked plan has no deployment changes.

`DeploymentExecutor` owns filesystem mutation and the durable commit point. CAS blobs supply immutable content. Existing journaling restores before-images after interrupted writes. Main UI staging changes enabled/priority intent; ordinary profile application does not deploy until Apply. Preserve that distinction.

`App.Services`/`AppServices` provide the existing service composition. The new workflow command captures packages for accurate previews, acquires the metadata gate, opens a modal WorkflowWindow and reconciles staged state on close. The modal window disables controls during its own asynchronous operations and prevents closing during them. Background Nexus metadata refresh is paused while it is open. Existing tabs and their numeric indices were preserved.

### Source map

Paths beginning with a project name below are under `src/`.

| Area | Primary files |
| --- | --- |
| Portable recipes | `MhwModManager.Automation/CollectionRecipeService.cs` |
| Inherited profiles | `MhwModManager.Storage/ProfileRepository.cs`, additive tables in `Schema.cs` |
| Decision/filesystem/profile projections | `MhwModManager.Core/WorkflowAnalysis.cs` |
| Actual overlay-chain evidence | `MhwModManager.Core/ConflictRuleIndex.cs`, `ConflictEngine.cs` |
| Global incompatible-package check | `MhwModManager.Core/DeploymentPlanner.cs` |
| Rules editing | `MhwModManager.Automation/RulesEditorService.cs` |
| Safe update workflow | `MhwModManager.Automation/UpdateMigrationService.cs` |
| Metadata transaction/Undo support | `MhwModManager.Storage/DeploymentMetadata.cs`, `MhwModManager.Filesystem/DeploymentExecutor.cs` |
| FOMOD parser/planner/copy | `MhwModManager.Automation/FomodInstallerService.cs` |
| FOMOD user choices | `MhwModManager.App/FomodInstallerWindow.cs` |
| Archive/Inbox integration | `MhwModManager.App/ViewModels/MainWindowViewModel.cs`, `MhwModManager.Automation/SmartInboxService.cs` |
| Workflow window | `MhwModManager.App/WorkflowWindow.xaml`, `.xaml.cs`, `ViewModels/MainWindowViewModel.Workflows.cs` |
| Crash minimization | `MhwModManager.Automation/CrashBisectorEngine.cs` plus probe orchestration in MainWindowViewModel |
| Stability | `MhwModManager.Automation/ModTrustService.cs`, WorkflowWindow |
| Adapter contract | `MhwModManager.Core/GameAdapters.cs`, `docs/GAME-ADAPTER-SDK.md` |
| Regression tests | `tests/MhwModManager.AutomationTests/WorkflowTests.cs`, existing suites, updated AutomationLogicTests |
| Self-test | `tools/MhwModManager.SelfTest/Program.cs` |
| CI | `.github/workflows/windows-workflows.yml` |

## The twelve requested features: implementation and boundaries

### 1. Collection/loadout import

Format-2 recipe JSON can use `.mhwrecipe` or `.ummpack`. The latter is a recipe document, not a ZIP of copyrighted payloads. Export includes mod/Nexus identity, enabled/priority state, family/category/provenance references, supersession, captured file hashes and game-build metadata. Format 1 remains readable. Preview bounds the input file to 32 MB and at most 10,000 entries, rejects duplicate/empty IDs, validates paths/hashes and refuses a known cross-game/domain mismatch.

Preview matches local IDs, then exact local Nexus mod/file identity. It distinguishes FoundLocally, FoundThroughNexusIdentity, WrongVersion, Missing, HashMismatch, ReplacementAvailable and Ambiguous. Multiple entries mapping to one local package are not silently restored. Superseded local packages are held for review. Legacy/uncaptured entries are explicitly labeled unverified. Hash comparison uses the captured catalog; recapture source files if they changed.

Import re-reads/re-matches rather than trusting a stale preview, saves a profile, and leaves unmatched/mismatched entries OFF. It does not download or deploy. Existing local rules remain authoritative. Family restoration is a separate explicit transactional action; it remaps matching groups into new imported family IDs and refuses to overwrite conflicting local groups. It preserves recognized roles rather than inventing overlay rules. It is not a universal migration of every saved relationship or FOMOD option across PCs.

### 2. Explain the winning provider

WorkflowAnalysis projects the actual planner output. It shows captured providers including disabled/superseded context, priority/family, Nexus provenance, winner SHA, reason code, confidence and evidence. Context and decisive evidence are labeled separately. Exact pins are correctly attributed to manual rules even when the existing engine does not return a RuleId for that path. Fully ordered overlay resolution now carries the applicable stored/inferred chain with source labels and reasons.

The existing `EffectiveInspectorService.ExplainAsync` remains the deployed-manifest inspector for compatibility. The new pre-deployment UI uses WorkflowAnalysis. Do not conflate current live ownership with staged effective intent.

### 3. Rules editor

The central UI creates/edits/deletes overlay, incompatibility and exact-file rules; pins/clears resource namespaces; creates base/optional family relationships through the established ChainManualFamilyAsync API; and stages priority relationships. Rules validate IDs, distinct pair members, correct winner membership, exact-path provider existence and cycles. Existing exact-path rules must be edited instead of silently duplicated.

A discovered semantic gap was fixed: enabled mods marked incompatible now block even when their payloads have no overlapping path. The planner avoids adding a duplicate global blocker when the same rule already blocked a file. Scope support is deliberately restricted to the combinations the editor/engine actually implement; the editor is not a general arbitrary rule-language interpreter.

### 4. Update migration

Preview compares captured old/new payloads, transferred rules, family and priority, and builds a real staged plan. It rejects different known Nexus mod identities, superseded candidates, an uncaptured replacement, untransferable exact-file rules, and untransferable resource pins. Existing replacement family membership may require manual resolution instead of guessing. Old-to-new self-relationships are retired. Profiles saved earlier are not silently rewritten across the whole database.

MetadataRowChange stores fixed-allowlist row before/after images. Edits are optimistic: changed metadata aborts the commit and file rollback restores the old setup rather than overwriting a concurrent edit. `ApplyWithMetadataAsync` preserves the old ApplyAsync API and commits metadata, manifest, enabled/priority state and the operation commit marker together. `operation_metadata` contains inverse information. Undo and rollback restore metadata as well as payloads. Dependent profile_rules links are explicitly journaled when a rule is retired so cascades do not lose them on Undo.

The old source package is retained by migration and marked superseded only on commit. Cross-version FOMOD option IDs are not guessed; the replacement uses the payload/options the user installed. The UI requires a reviewed pair, no unapplied staged state, and a closed game. Fault-injection tests cover both sides of the commit boundary and stale metadata.

### 5. FOMOD installer choices

The parser looks for exactly one `fomod/ModuleConfig.xml`, disables DTD/entity resolution, limits XML size and rejects unsupported top-level content. Choices support static/dependent plugin types, group cardinalities, ordered/visible steps, condition flags, required/conditional file/folder directives and priority. Selected files alone are materialized into a new destination. Equal-priority conflicting copies are rejected. Traversal, unsafe paths and links/reparse points are refused. Failed copy cleanup removes the owned new destination.

MHW payload paths are mapped to nativePC or explicit GameRoot; generic games retain their configured scanner behavior. The choice window displays groups and descriptions and enforces valid selections before import. Selection sets are stored in settings keyed by config SHA-256. Identical configs can reuse IDs; different version configs require review.

Archive import uses quarantined extraction, invokes the chooser when detected, and only then imports the selected payload. Smart Inbox holds FOMOD inputs rather than importing every option. It leaves the original Inbox item available for interactive archive import.

External game/plugin/installer-manager version dependencies are not implemented and fail closed with an actionable message. No scripts execute. There is no claim of complete support for every FOMOD dialect, arbitrary dependency environment, or cross-version option mapping. Public references are linked in docs/WORKFLOWS.md.

### 6. Profile inheritance

`profile_parents` is additive; old flat profiles remain valid. Resolve recursively within a read transaction, detect cycles and bound depth to 128. Save child rows only where enabled/priority differs from the parent. Omitted inherited entries become explicit disabled overrides when saving a complete state, avoiding accidental enabling. List summaries count resolved enabled state. Saving an existing child from the original Profiles screen preserves its parent.

Profiles still share global compatibility rules. This is state inheritance, not a full per-profile rules/family overlay system. The new window creates new names to preserve existing saved loadouts; it is not a comprehensive profile rename/delete/reparent UI.

### 7. Profile diff

Compare resolved states through the same planner using current captured files/global rules. Report enabled/disabled/priority changes, changed providers, introduced/resolved blockers and changed armor components. This is a current-data comparison, not historical replay of previously deployed file versions.

### 8. Virtual effective filesystem

The workflow screen has a directory navigator, filterable virtualized file grid and per-file decision detail. It reflects staged state before Apply and keeps global incompatibility blockers visible. It does not modify the real nativePC tree. Rule lookup is indexed rather than rescanning all rules per path.

### 9. Crash interaction minimization

Validate an empty-suspect baseline and the complete suspect set before attribution. Probe subsets and complements with cached outcomes and cancellation checks. The result is 1-minimal under observed outcomes: removing any single remaining member passed. It is not guaranteed globally smallest and cannot prove absence of intermittent failures. A multi-mod combination is not marked as multiple individually confirmed culprits.

The existing 12-second startup observation remains the probe criterion. The orchestration closes a surviving/cancelled probe process before restoring the prior setup. Restoration uses CancellationToken.None so cancellation does not strand a test state. Do not claim this diagnoses every mid-game crash or arbitrary GPU/driver fault.

### 10. Stability evidence

Display actual successful launches, associated startup failures, rollbacks and last success/failure. A missing record means untested. Failure association is not causation; there is no invented quality score. GetAllAsync loads history in one query rather than opening a connection per mod.

### 11. Relationship visualization

The selected package's neighborhood includes stored rule/family relationships, limited to 50 nodes. Overlay edges have directional arrowheads; incompatibilities are red; families are blue; explicit cycles have red node borders. This is a scoped persisted-relationship graph, not an all-library graph or a complete view of transient inference. Use the file inspector for inferred decision evidence.

### 12. Game adapter SDK

`IGameAdapter` exposes discovery, executable/mod roots/save paths, Nexus domain, semantic parsing, categories, dependency declarations and validation. Generic/MHW built-ins exist; unknown IDs fall back to generic semantics. Planner and engine choose MHW conflict semantics via the registry. Registration rejects duplicate IDs and is explicit compiled host code; downloaded DLLs are not automatically loaded.

This is the initial extension contract. Several hooks are available to future host integration; existing game-specific automation remains intact. It does not ship completed Stellar Blade/Wilds semantic adapters, and must not opt unrelated games into MHW rules merely for convenience.

## Verification: what actually ran

| Check | Result |
| --- | --- |
| Linux strict solution Release cross-build, SDK 10.0.401 | 0 warnings / 0 errors |
| Windows strict solution Release build | 0 warnings / 0 errors |
| Core suite | 79/79 |
| Automation suite | 39/39, including 21 new workflow tests |
| Integration suite | 61/61 |
| Self-test | 11/11 |
| Function scan | 683 functions; 562 known-good; 121 pending normal promotion; 0 trace gaps; 0 uncovered call sites; 0 parse errors |
| Handoff preflight / whitespace check | PASS |
| Native WPF visual/input acceptance | NOT DONE |
| Real user mod collection / game launch acceptance | NOT DONE |
| New packaged release | NOT PRODUCED |

Passing Windows runs:

- https://github.com/fengie/mhw-mods/actions/runs/36318654840 at a11ca39.
- https://github.com/fengie/mhw-mods/actions/runs/36318708338 at 66a48bf (latest CI configuration).

Evidence is in `_AGENT_CONTEXT/EVIDENCE/workflow-expansion-validation.log`, `workflow-expansion-function-scan.json`, `workflow-windows-initial.log`, and `workflow-windows-passed.log`. CURRENT_REVISION separates Linux/Windows evidence commits. The integration suite intentionally emits TRACE GAP/PARSE ERROR messages for negative verifier fixtures; final suite totals distinguish expected diagnostic output from actual failures.

The first Windows run passed compilation/tests/self-test but failed the final scan. Git autocrlf had changed line endings inside raw C# SQL strings, altering exact token fingerprints in unchanged functions. `.gitattributes` now pins `*.cs text eol=lf`. A local autocrlf=true checkout probe preserved identical bytes; both subsequent Windows scans passed. Do not normalize away meaningful literal changes inside the verifier or manually mark those functions verified.

Changed/new explicit production function bodies must begin with `using var __mhwTrace = MasterDebugLog.BeginMethod();` (optional context argument is allowed). Expression-bodied methods edited during this work were converted when required. Auto-properties/records without executable bodies do not need synthetic traces. Use the actual scan; do not assume every new accessor/lambda/local function is exempt.

The normal release verifier may promote changed fingerprints only after its required pipeline succeeds. CI here deliberately scans without promotion. The unchanged stage cache and trusted baseline ZIP were preserved; no forged evidence was added.

## Runtime and command details

The runtime used `/workspace/scratch/bcf29506d2df/mhw-mods` for the repository; .NET was `/workspace/scratch/bcf29506d2df/toolchain/dotnet/dotnet` (10.0.401); PowerShell was `/workspace/scratch/bcf29506d2df/toolchain/pwsh/pwsh` (7.5.3). These paths can disappear. The repository, not the scratch directory, is durable.

Linux examples (put the installed SDK on PATH):

```sh
dotnet restore MhwModManager.sln -p:EnableWindowsTargeting=true --disable-parallel -m:1
dotnet build MhwModManager.sln -c Release --no-restore -p:EnableWindowsTargeting=true -m:1 -warnaserror
dotnet tests/MhwModManager.Tests/bin/Release/net10.0/MhwModManager.Tests.dll -noLogo -noColor -maxThreads 2
dotnet tests/MhwModManager.AutomationTests/bin/Release/net10.0-windows10.0.19041.0/MhwModManager.AutomationTests.dll -noLogo -noColor -maxThreads 2
dotnet tests/MhwModManager.IntegrationTests/bin/Release/net10.0-windows10.0.19041.0/MhwModManager.IntegrationTests.dll -noLogo -noColor -maxThreads 2
dotnet tools/MhwModManager.SelfTest/bin/Release/net10.0-windows10.0.19041.0/MhwModManager.SelfTest.dll /absolute/temporary/report-directory
dotnet tools/MhwModManager.FunctionVerifier/bin/Release/net10.0/MhwModManager.FunctionVerifier.dll --root /absolute/repo --mode scan --report /absolute/report.json
pwsh -NoLogo -NoProfile -File scripts/Test-AgentHandoff.ps1
git diff --check
```

The execution sandbox blocked named-pipe MSBuild/VSTest paths, so single-node builds and in-process compiled xUnit DLLs were used. Linux can cross-compile WPF but cannot establish its native behavior. Windows CI runs the same backend DLL suites natively. CI runs once per PR change or manual dispatch, cancels superseded runs and skips Markdown/agent-context-only changes. Run the handoff preflight locally for documentation checkpoints.

PowerShell launch syntax for filenames with spaces: `& ".\RUN BUILT APP.bat"`, not `.\RUN BUILT APP.bat` unquoted. Use `Test Everything.bat` and `Build.bat` for the normal release pipeline. Do not substitute this CI success for packaging or visual acceptance.

## Private Git access and publishing mechanics

Direct Git clone/push lacked credentials in this runtime. The baseline was materialized using the connected GitHub recursive tree and file fetches; 181 identical files were reused from a prior cache and 23 fetched files were verified, for 204 exact baseline blobs. Local Git was initialized with a synthetic baseline. Thus local commit IDs differ from remote IDs even when tree bytes match. Never pass a synthetic local parent SHA to GitHub or attempt to force-push that history.

When this limitation persists, use connected tools to read the current remote ref/tree, create a tree over the remote base, create a commit with the actual remote parent, and fast-forward the branch. Create/update the existing PR, not a new PR per checkpoint. Verify changed files or full tree hashes after publication. For fresh normal authenticated checkouts, ordinary Git is preferable.

Successful read routes included `/git/ref/heads/codex/complete-mod-workflows`, recursive `/git/trees/<sha>?recursive=1`, `/git/commits/<sha>`, `/actions/runs/<id>` and `/actions/runs/<id>/jobs`. An encoded branch name in the branches endpoint did not work reliably. Job log downloads require the dedicated `github_fetch_workflow_job_logs` connector method; the generic fetch route rejected `/actions/jobs/<id>/logs`.

Git data writes used create_tree, create_commit and update_ref(force=false). Large content payloads were assembled from local files in bounded chunks, parsed and submitted without printing source/base64 into chat. No repository credentials were stored in source or handoff files. WPF compiler `*_wpftmp.csproj` files are build outputs and are ignored; never publish them.

## Next concrete work: native acceptance

Do not endlessly repeat passing backend tests without a concrete new risk. The next unverified part is the desktop experience and real-world inputs.

Use a disposable game directory/database and copies of test mod archives. Keep the user's actual game saves and live nativePC untouched during exploratory UI testing.

| Area | Manual acceptance scenario |
| --- | --- |
| Entry point | Dashboard button opens workflow window; main window remains safely modal; busy/close behavior works; branding/icon remains intact |
| Effective files | Browse nested directories, filter names/paths, inspect single-provider, identical, family overlay, exact pin and global blocker; compare staged winner with actual dry-run plan |
| Recipes | Export/import across two disposable workspaces with remapped local IDs; inspect missing/wrong/hash/ambiguous/replacement states; ensure no live enabling; test explicit family restore refusal |
| Profiles | Parent + child delta; change parent then resolve child; stage right profile and verify main UI does not lose state on closing the modal window |
| Rules | New/edit/delete rules, reversed pair, cycle refusal, shared-provider pin/clear, optional family, priority staging, incompatibility without file overlap |
| Updates | Old/new preview, untransferable pin refusal, successful apply, old supersession, Undo restores both files and metadata; game-running/staged-state checks |
| FOMOD | Real representative archive, required/optional/radio-like groups, dependent visible step, cancellation, remembered identical config, unsupported dependency explanation, no unwanted variant copied |
| Inbox | FOMOD archive remains available for interactive import rather than importing all components |
| Graph | Direction points to winner; labels readable; scroll/50-node limit/cycle highlight; no implication that inferred edges are all persisted |
| Stability | Real histories vs untested state; association language does not claim guilt |
| Crash diagnosis | Only with explicit safe disposable game setup; cancellation closes test process and restores state; interacting pair is not individually blamed |
| Adapter | Generic workspace stays conservative; MHW semantics only where intended; configured roots and validation display correctly |

Record failures with exact steps and logs, fix them, run focused tests plus required gates, then push a checkpoint before continuing. If native access is unavailable, state that limitation; do not claim completion or invent screenshots. Do not merge or release until authorized.

## Future-agent obligation

Every future agent must pass this knowledge forward. Update current facts rather than appending contradictory status forever; preserve historical evidence under clearly labeled history. Add material discoveries, decisions, test failures/fixes and limits to the relevant documents. Keep CHECKPOINT.md short and current, linking this deeper handoff. Add new required knowledge documents to handoff-manifest.json where appropriate. Run Test-AgentHandoff, push the checkpoint, verify the remote update, and tell the user the commit/PR.

**Do not break the chain.**
