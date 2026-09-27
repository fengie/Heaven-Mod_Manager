# Verification Infrastructure / CI / Supply-Chain Audit — 2026-09-27

## Scope and audit base

This is an independent support-agent audit of what a green result actually proves in `fengie/mhw-mods`.

Audit branch base: `0e561f3c059475ad443a79ac4a27dd68264a7bdb`.

The verification/CI implementation itself was unchanged between the earlier inspected verification base `bb5e86e1bc956df9dfd4c1cd7ebed0e9c07e2fe8` and this audit base. The intervening commits implement and repair the `PlannerSnapshotRepository` production boundary and update handoff state. This audit does **not** modify that production boundary.

At this base, `CURRENT_REVISION.json` correctly treats `c9b27b98d280b144ba52ba35167f1fcb594945bd` / hosted run `36334644325` as the last closed Windows verification and treats the repaired PlannerSnapshotRepository candidate as awaiting a fresh exact Windows gate. This audit does not change that status.

Primary files inspected:

- `docs/FUNCTION-VERIFICATION.md`
- `.verification/README.md`
- `.verification/function-status.json`
- `.verification/stage-status.json`
- `.verification/trusted-v8.7.0-files.json`
- `.github/workflows/windows-release-gate.yml`
- `scripts/Verify-Release.ps1`
- `scripts/Build-Release.ps1`
- `scripts/Test-VerificationCache.ps1`
- `scripts/Test-AgentHandoff.ps1`
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1`
- `tools/MhwModManager.FunctionVerifier/Program.cs`
- all project files, `Directory.Build.props`, `Directory.Packages.props`, and `global.json`
- current hosted Windows closure evidence and the permanent continuity constitution

No verifier, workflow, production source, verification cache, or continuity validator is changed by this audit.

## Executive result

The system has several strong fail-closed properties, and the current hosted Windows closure is materially stronger than a single cached verifier run because the workflow runs both `Verify-Release.ps1` and the uncached release build/test/publish path.

However, **"615/615 function fingerprints verified" is narrower than it sounds**, and **the stage cache is not yet an exact fingerprint of the verification contract itself**. There are also supply-chain hardening gaps around mutable GitHub Action tags, NuGet graph reproducibility, and write-token scope.

The most important future hardening checkpoints are:

1. make function verification cover executable declaration state / semantic context rather than callable bodies alone;
2. bind promotion to the exact source snapshot that was built and tested;
3. include the stage contract/verifier policy in every reusable stage fingerprint;
4. isolate GitHub write permission from build/test execution and pin Actions by immutable commit SHA;
5. replace regex-only continuity confidence with adversarial semantic fixtures;
6. make release evidence immutable per source SHA/run rather than overwriting one fixed evidence path.

These should be separate verification-infrastructure checkpoints, each independently tested. Do not combine them with production architecture work.

---

# Finding matrix

| Area | Classification | Concrete result |
|---|---|---|
| Hosted source SHA provenance | **sound** | Checkout is tied to the triggering revision; provenance records `GITHUB_SHA`, ref, runner, SDK and PowerShell. |
| Function baseline ZIP integrity | **sound** | Manifested trusted C# entries are SHA-256 checked, parsed, de-duplicated and required to be present before bootstrap trust is reused. |
| Failed hosted release persistence | **sound** | Workflow persists promoted caches to `main` only after both Verify and Build steps succeed. |
| Concurrent evidence pushes overwriting newer `main` | **sound** for overwrite safety | Push is non-force, so an old run should fail non-fast-forward rather than overwrite an advanced branch. |
| Callable fingerprint inventory completeness | **high concern** | Executable field/property initializer state and related declaration semantics are outside the function inventory and call-site coverage. |
| Promotion tied to exact tested source snapshot | **high concern** | Confirm rescans the mutable worktree after tests; no manifest proves the confirmed tree is the one compiled/tested. |
| Stage-cache contract fingerprinting | **high concern** | Cache keys omit the verifier script/stage command contract for most stages, so verifier-policy edits can reuse old green stage state. |
| External/non-project build inputs | **medium concern** | Project fingerprinting follows directory trees and ProjectReferences, not arbitrary MSBuild Content/AdditionalFiles/import inputs; e.g. linked root data is not naturally represented. |
| ReadyToRun meaning of green | **medium concern** | Release can intentionally fall back to self-contained JIT and still pass. Current closed run did not use fallback. |
| GitHub Actions pinning | **medium concern** | `actions/checkout@v7`, `actions/setup-dotnet@v6`, and `actions/upload-artifact@v7` are mutable tags rather than immutable commit SHAs. |
| GitHub token privilege isolation | **medium concern** | `contents: write` is job-wide and checkout persists credentials by default, although only the final evidence step needs write. |
| NuGet graph reproducibility | **medium concern** | Direct central package versions are exact, but there are no checked-in `packages.lock.json` files or repo feed policy. |
| Local SDK exactness | **medium concern** | CI installs 10.0.401, but scripts accept newer SDKs and `global.json` allows latest feature roll-forward. |
| Build-release promotion ordering | **medium concern** | Local cache promotion occurs before documentation copy, ZIP creation and final artifact hashing. |
| Continuity validator semantics | **medium concern** | Regex presence checks can accept contradictory/dead text; current negative fixtures mainly test keyword removal. |
| Evidence immutability/retention | **medium concern** | One hosted-closure path is overwritten; detailed Actions artifacts expire after 30 days. Git history preserves prior summaries, but evidence is not first-class immutable per run. |
| Full verifier test-source inventory | **evidence missing** | The private repository code-search index was unavailable during this audit, so targeted test-file discovery was not exhaustive. Core implementation paths and named harness scripts were fetched directly. |

---

# 1. Function verification

## 1.1 What is sound

The verifier is materially fail-closed in several places:

- current production C# is parsed before trust/promotion;
- parse errors and duplicate generated function IDs stop promotion;
- the previous checklist is not rewritten on incomplete inventory/identity failure;
- changed/new callable bodies require a first-statement `MasterDebugLog.BeginMethod()` scope unless they are the narrow tracer recursion exemption;
- trace acquisition hidden in a branch/lambda, delayed after other statements, or scoped over only part of a body is rejected;
- trusted v8.7 source is validated file-by-file against the manifest before bootstrap comparison;
- trusted ZIP C# entries must be manifested, hash-correct, parseable, non-duplicate, and complete relative to manifested C# files;
- generated `bin`/`obj` C# is excluded;
- explicit-interface member identity was hardened;
- the existing LR-001 incident is correctly preserved: moving a production method can create a changed fingerprint and lose required entry instrumentation.

The current `function-status.json` at the inspected closed evidence state has 615 entries and all 615 are `verified=true` with `full-release-confirmation` basis. That accurately describes **the verifier's current callable inventory**.

## 1.2 High concern — executable authored state exists outside the callable inventory

`EnumerateCallables` inventories:

- method/constructor/destructor/operator/conversion declarations with bodies;
- local functions;
- explicit accessor bodies;
- expression-bodied properties/indexers.

It does not create independent verification identities for executable declaration state such as:

- field initializers;
- auto-property initializers;
- declarations that drive generated behavior;
- class primary-constructor declaration semantics;
- invocation/object creation occurring in those declaration initializers.

`CountExplicitCallSites` is likewise called only for inventoried callables, so an object creation in a field initializer is outside explicit-call-site coverage.

Concrete current examples exist in `MainWindowViewModel.cs`:

- `private readonly CancellationTokenSource backgroundCts=new();`
- `private readonly SemaphoreSlim metadataGate=new(1,1);`
- collection auto-property initializers such as `Mods { get; } = [];`
- many `[ObservableProperty]` backing declarations whose syntax drives generated WPF-visible properties.

Changing `new(1,1)` to `new(2,2)`, changing a property initializer, or changing generator-driving declaration metadata can change runtime behavior while leaving every existing callable fingerprint unchanged. The project/source file changes still invalidate relevant build/test fingerprints and full solution compilation still runs, so this is **not** a universal CI bypass. The failure is narrower but important: per-function "known good" and trace coverage do not cover all authored executable behavior.

Class primary constructors are also not represented by `ConstructorDeclarationSyntax`; the type may contain methods that are inventoried, but the declaration-level constructor state is not itself a function entry.

**Recommended future checkpoint:** add a second inventory class for executable declaration state, or add a containing-file/semantic-context fingerprint that invalidates callable trust when unowned executable declarations change. Add negative regression fixtures that mutate only a field initializer, auto-property initializer, primary-constructor parameter/default, and generator-driving attribute and prove old callable trust cannot silently remain sufficient.

## 1.3 High concern — fingerprinting is syntax-local, not semantic-context complete

A function fingerprint is SHA-256 over the callable's trivia-free Roslyn token stream. That is stable and useful, but it does not encode binding context outside the callable.

Examples of context that can alter meaning without changing the callable token stream include:

- changed `using`/alias/global-using resolution;
- changed containing type/base/interface declaration;
- changed conditional compilation symbols;
- source-generator inputs/attributes declared outside the callable;
- project/build properties that affect compilation semantics.

The full build and tests mitigate this for release correctness, but the phrase "exact unchanged function" should be understood as **exact unchanged local callable syntax**, not exact unchanged compiled semantics.

**Recommended future checkpoint:** either document that narrower contract explicitly, or derive a semantic/context fingerprint that includes the containing declaration and relevant compilation inputs.

## 1.4 High concern — promotion is not bound to the exact source snapshot that passed earlier stages

Both release scripts do:

> scan current worktree -> build/test -> later run verifier in confirm mode

Confirm mode performs a fresh scan of the current filesystem and then promotes the fingerprints it sees. There is no source manifest/tree hash captured before compilation and asserted immediately before promotion.

Failure mechanism:

1. source A is scanned, built and tested;
2. source changes to B during the long run (developer edit, another process, or repository-controlled tooling);
3. confirm rescans B;
4. if B parses and satisfies trace/call-site rules, B can be promoted although tests compiled/exercised A.

The hosted Actions environment is isolated enough that accidental edits are less likely than locally, but repository-controlled scripts/tests still execute inside the same mutable checkout. The repository's exact-input constitution should not rely on "nothing probably modified source."

**Recommended future checkpoint:** produce an immutable source-input manifest/tree digest at initial scan; require confirm to consume/compare that manifest and fail if any verification input changed. A clean `git diff` assertion alone is insufficient for intentionally dirty/local source; content hashes are preferable. The exact stage contract/toolchain should be bound into the same promotion evidence.

## 1.5 Baseline ZIP trust root — medium concern / evidence provenance limitation

The verifier strongly checks **internal consistency** between `.verification/trusted-v8.7.0-src.zip` and `.verification/trusted-v8.7.0-files.json`.

However, both are mutable repository files. There is no independent signature or externally anchored digest enforced by the verifier that says "this manifest/ZIP pair is the authentic historical v8.7 trust root."

That is acceptable if the threat model is accidental corruption and normal Git review; it is not a cryptographic trust boundary against a repository writer. After a complete current full-release confirmation, the bootstrap source is less important for normal unchanged-current-code reuse.

**Recommended future checkpoint:** preserve an externally anchored release/source digest or signed provenance if historical bootstrap authenticity is intended as a security property.

---

# 2. Stage cache

## 2.1 What the current fingerprint contains

`Get-ProjectFingerprint` recursively hashes:

- the target project directory, excluding `bin`/`obj`;
- transitive `ProjectReference` project directories;
- selected common root inputs when present:
  - `Directory.Build.props`
  - `Directory.Build.targets`
  - `Directory.Packages.props`
  - `global.json`
  - `.editorconfig`
  - `NuGet.Config` / `nuget.config`;
- exact detected dotnet SDK version;
- `$env:OS`;
- process architecture.

The IntegrationTests fingerprint receives an intentional broader special case covering `src`, FunctionVerifier tooling, `scripts`, and selected continuity documents because those tests inspect files without ProjectReference edges.

The existing cache regression script usefully proves invalidation for Integration XAML/script edits and common build targets, and proves generated output/unrelated App edits do not unnecessarily invalidate Core.

## 2.2 High concern — the stage's verification contract is not part of its cache key

For normal strict/test stages, the fingerprint does **not** include the implementation/contract that decides how the stage runs:

- `scripts/Verify-Release.ps1` itself;
- the exact dotnet command-line arguments for that cache ID;
- cache algorithm/schema/policy version beyond data-file format;
- test filters/options if those are changed in the verifier script.

Concrete failure mechanism:

1. `test:Core unit tests` is green and cached for test-project fingerprint X;
2. a future verifier change alters how that stage is invoked, filtered, or interpreted;
3. test-project files remain X;
4. the old cache entry still matches and the newly modified verification policy skips the stage it was supposed to redefine.

This is exactly the class of problem where a verifier modification can accidentally "prove itself" using evidence produced under older verifier semantics.

Today's hosted workflow reduces the blast radius because `Build-Release.ps1` subsequently reruns the core/automation/integration/self-test paths uncached before evidence is persisted. That is a valuable independent layer. It does **not** make the stage cache semantically exact, and a standalone `Verify-Release.ps1` green can still inherit a stage result from a different verification contract.

**Recommended future checkpoint:** derive a stage-contract fingerprint from the exact stage ID, command/arguments, relevant verifier/cache code hash, and a deliberate contract-version constant. Add an independent regression fixture that changes only the stage contract and proves a cache miss. Do not let the modified cache implementation be its only test.

## 2.3 Medium concern — arbitrary MSBuild inputs are not fully modeled

The current recursion follows project directories and `ProjectReference` edges. It does not parse arbitrary MSBuild inputs such as linked `Content`, `AdditionalFiles`, imported targets outside the project tree, or other explicit includes.

A concrete project example is App's linked root file:

`../../data/Armor Database.csv`

That file is outside `src/MhwModManager.App`, so it is not naturally included by the App project-directory recursion. A change can therefore leave `strict:App`'s project fingerprint unchanged.

Again, strict whole-solution compilation is never skipped and the release build is uncached, so this does not by itself prove a hosted false green. It means the cached-stage claim "exact project/dependency fingerprint unchanged" is stronger than the implementation warrants.

**Recommended future checkpoint:** use evaluated MSBuild inputs/dependency graph where feasible, or explicitly include known linked content/additional inputs. Add a fixture for an external `Content Include`.

## 2.4 Medium concern — environment-sensitive inputs are only partially represented

The cache records SDK version, OS string, and processor architecture, but not all behavior-affecting host inputs, for example:

- specific Windows runner image/build;
- PowerShell version;
- locale/culture;
- selected environment variables;
- restored NuGet graph/package hashes.

Not every environment value belongs in every cache key. The concern is the wording "exact input fingerprint": today it is a selected-input fingerprint.

**Recommended future checkpoint:** define a documented stage input model per stage and explicitly classify environmental inputs as included, intentionally ignored, or non-cacheable.

---

# 3. Windows Release Gate

## 3.1 Sound — source provenance is explicit enough for the current workflow shape

The workflow checkout runs at the triggering event's ref/SHA, uses full history, and records:

- `GITHUB_SHA`
- `GITHUB_REF`
- runner OS/architecture
- dotnet SDK
- Windows PowerShell version.

Hosted evidence records the source SHA and run ID. The latest closed evidence inspected during this audit was for exact source `c9b27b98d280b144ba52ba35167f1fcb594945bd`, run `36334644325`, Windows x64, SDK 10.0.401.

A small additional hardening would be to explicitly pass `ref: ${{ github.sha }}` to checkout and assert `git rev-parse HEAD == GITHUB_SHA`, but current default checkout semantics already target the event revision.

## 3.2 Sound — durable promotion is gated on both verification and release build success

The workflow sequence is:

1. Verify-Release;
2. Build-Release;
3. upload evidence always;
4. persist promoted verification state only on overall success on `main`.

Therefore a failure in Build-Release after Verify-Release locally promoted the working-copy cache does not result in the workflow committing that cache to `main`.

This is an important independent guard and should be preserved.

## 3.3 Medium concern — Build-Release locally promotes before packaging is complete

Inside `Build-Release.ps1`, function confirmation/promotion happens after compile/tests/publish, but **before**:

- documentation/script/data copy completes;
- ZIP compression completes;
- artifact hash/report generation completes.

If packaging fails after promotion, the script fails but the local `.verification/function-status.json` has already been upgraded. Hosted persistence will not commit it because the step failed, but a local subsequent run can observe that promoted state.

That conflicts with documentation that describes promotion as occurring only after the complete release path succeeds.

**Recommended future checkpoint:** move promotion after successful packaging/hash, or transactionalize cache promotion so any later release failure restores the pre-run baseline.

## 3.4 Medium concern — green does not necessarily mean ReadyToRun succeeded

The release script intentionally treats certain ReadyToRun/SDK runtime-pack failures as optimization-only failures. It retries a self-contained JIT publish, writes `PUBLISH FALLBACK.txt`, and can still return success.

Therefore:

> green release != guaranteed ReadyToRun release

The current closed hosted run recorded `ReadyToRun fallback used: False`, so this concern does **not** invalidate that specific closure.

**Recommended future checkpoint:** decide the product contract. If R2R is mandatory, fail the gate. If functional self-contained JIT fallback is allowed, make all handoff/release wording say so and record the publish mode in machine-readable closure evidence.

## 3.5 Sound for overwrite safety; operational concern for concurrency

The workflow has no `concurrency` group. Multiple main pushes can therefore run simultaneously.

The evidence persistence step uses an ordinary non-force:

`git push origin HEAD:main`

An older run should fail non-fast-forward if `main` advanced, rather than overwrite newer production work. That is the correct safety direction.

The downside is wasted CI and a successful verification run whose evidence commit can fail to persist because a newer main commit won the race.

**Recommended future checkpoint:** add a concurrency policy appropriate to whether obsolete runs should be cancelled, and add an explicit fetch/fast-forward/source-SHA guard before evidence push. Never introduce force push.

---

# 4. Supply-chain review

## 4.1 Medium concern — GitHub Actions use mutable major-version tags

Current workflow references:

- `actions/checkout@v7`
- `actions/setup-dotnet@v6`
- `actions/upload-artifact@v7`

Major-version tags are mutable. GitHub's own secure-use guidance recommends pinning third-party/action dependencies to full commit SHAs when immutability matters; a full-length commit SHA is the immutable form.

These are first-party GitHub-maintained actions, which lowers practical concern relative to an unknown action, but mutable tags still mean the same repository commit can execute different action code later.

**Recommended future checkpoint:** pin each action to a reviewed full commit SHA and retain a human-readable version comment. Update intentionally through a dedicated dependency/CI review.

## 4.2 Medium concern — `contents: write` is job-wide and checkout credentials persist

The workflow declares:

`permissions: contents: write`

at workflow scope. The same job then runs restore, verifier code, all project builds, tests, self-test and packaging. `actions/checkout` persists authentication credentials by default unless configured otherwise. Thus the build/test checkout has authenticated repository-write capability even though write is only needed by the final evidence persistence operation.

This is broader authority than necessary.

The workflow only triggers on pushes to `main` and manual dispatch, not untrusted pull-request code, which substantially reduces exposure. The remaining issue is blast radius from compromised build dependencies/actions or repository-controlled execution.

**Recommended future checkpoint:** isolate privilege rather than simply deleting it:

- verification/build job: `contents: read`, `persist-credentials: false`;
- upload promoted caches/evidence as workflow artifacts;
- separate minimal persistence job: `contents: write`, downloads only the expected evidence, verifies source/run identity, makes the evidence commit, and executes no repository build/test code.

Document the exact data crossing the privilege boundary before changing permissions.

## 4.3 Medium concern — dependency versions are direct-pinned but the resolved graph is not locked

`Directory.Packages.props` pins exact direct package versions. This is good.

No checked-in `packages.lock.json` files were present for the inspected projects, and no repository `NuGet.Config` was present. Therefore restore resolution depends on transitive dependency metadata and environment/default feed configuration at run time.

The full hosted gate tests the actually restored graph, so this is primarily a reproducibility/provenance gap rather than evidence that the current run skipped testing.

**Recommended future checkpoint:** evaluate central transitive pinning/lock files and locked restore mode; pin/limit package sources as appropriate; record the resolved dependency graph or lock hash in closure evidence.

## 4.4 Medium concern — local toolchain accepts newer feature SDKs

CI requests SDK 10.0.401. However:

- `Verify-Release.ps1` and `Build-Release.ps1` reject only versions *below* 10.0.401;
- `global.json` uses `rollForward: latestFeature`.

So a local "green" can be produced by a newer feature-band SDK. The scripts do record the actual SDK and stage fingerprints include it, which is good.

**Recommended future checkpoint:** distinguish "hosted canonical closure" from "local compatible verification" in docs, or enforce exact SDK if bit-for-bit toolchain parity is desired.

## 4.5 Evidence missing / medium trust-root limitation — package/action artifact attestations

This audit did not find a repository mechanism that records signed attestations/SBOM-style provenance for the final ZIP or third-party package graph. The release ZIP SHA-256 is recorded, which is useful integrity evidence.

If stronger supply-chain provenance becomes a requirement, add it as a separate release-security checkpoint rather than mixing it into application changes.

---

# 5. Continuity validator adversarial audit

## 5.1 What is sound

The continuity system is substantially stronger than an unvalidated handoff:

- required files and manifest fields are checked;
- canonical repo/branch and current version consistency are checked;
- cache JSON schemas/basic identities are checked;
- the read-order relation between constitution and Learned Rules is checked;
- recursive-propagation concepts must appear in AGENTS/start-here/protocol;
- four negative fixtures prove that selected missing-keyword/core-protection failures are rejected;
- the negative fixture harness invokes the real validator against a copied fixture rather than a fake reimplementation.

The governance checkpoint therefore has real value.

## 5.2 Medium concern — phrase presence is not semantic validity

Most constitutional assertions use `-match` against raw Markdown text. The validator does not parse instruction polarity, active sections, Markdown comments, or contradictions.

A concrete broken state can still pass. For example, this replacement in `NEXT-AGENT-START-HERE.md` would retain every currently required token:

> You inherit the permanent continuity constitution at `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and the `_AGENT_CONTEXT/LEARNED_RULES.md` ledger. The **successor must not preserve or propagate it**, and the **agent after** the successor should ignore it. **Do not break the chain.**

The current checks still see:

- "permanent continuity constitution";
- protocol path;
- Learned Rules path;
- "successor";
- "agent after";
- "Do not break the chain".

The same general bypass works with required phrases hidden in comments/dead historical text while operative instructions are weakened elsewhere.

This is not an argument to make the validator understand natural language perfectly. It means the current negative-fixture result should be interpreted as **structural keyword regression protection**, not proof of semantic continuity.

## 5.3 Medium concern — negative fixtures are too friendly

The four current negative fixtures mainly remove/replace words that the validator explicitly searches for. They prove fail-closed behavior for those exact classes, but not adversarial contradiction.

Concrete future negative fixtures should include:

1. **negated propagation:** "successor must not propagate to the agent after them";
2. **comment/dead-text bypass:** required phrases preserved only in a Markdown/HTML comment or archived example;
3. **contradictory Core Rule protection:** retain phrase "explicit user authorization" while operative sentence says it is *not* required;
4. **read-order deception:** mention files in the right textual order outside the actual read-order section while the operative ordered list is reversed;
5. **manifest/text conflict:** manifest booleans remain true but start-here explicitly opts out.

Per the continuity constitution, any validator repair is verification-infrastructure production work and must be isolated, independently negatively tested, pushed, and closed by exact hosted verification. This audit therefore documents the weakness but does not patch it.

---

# 6. Evidence durability

## 6.1 Medium concern — one mutable hosted-closure file is reused

The workflow writes every successful hosted closure to the same path:

`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`

Git history preserves old versions, but the current tree only exposes the latest contents at that path. Historical handoff text can easily cite an old run while the path later contains a new run unless all references are updated together.

The repository recently handled this correctly by updating current revision state to the newer closed c9/run-36334644325 evidence, but the data model remains easy to drift.

**Recommended future checkpoint:** write immutable evidence files named by source SHA and/or run ID, for example:

`_AGENT_CONTEXT/EVIDENCE/windows-release/<source-sha>-<run-id>.log`

Then maintain a tiny machine-readable "latest closed evidence" pointer if desired.

## 6.2 Medium concern — detailed hosted artifacts expire

`actions/upload-artifact` retains detailed build logs, artifacts, release output and caches for 30 days. The committed closure log keeps summary reports and release hash, which is useful, but detailed forensic logs are not durable forever.

**Recommended future checkpoint:** decide which compact evidence is expensive to lose and commit only that stable summary/manifest. Avoid committing huge transient logs merely for permanence.

---

# 7. Platform exercise

## Sound

The canonical hosted gate uses Windows and explicitly runs:

- Windows integration/fault-injection tests;
- automation self-test;
- win-x64 compile/analyzers;
- self-contained publish;
- artifact packaging/hash.

That is appropriate for a WPF/Windows application and stronger than treating Linux-only compile evidence as release closure.

The scripts also intentionally make the Windows-only integration/self-test stages fail/skip as failures on non-Windows verification rather than silently counting them green.

## Evidence missing

This audit did not perform an additional hosted Windows run because it is documentation-only and the production PlannerSnapshotRepository boundary is already awaiting its own exact Windows gate. Triggering an unrelated audit-branch release would mix concerns and would not validate a code change here.

No claim in this document upgrades the current production candidate's verification state.

---

# 8. Recommended future checkpoints

These are ordered by dependency/risk logic, not by urgency hype. Each must be a separate independently verifiable checkpoint.

## Checkpoint A — exact-source promotion binding

Goal: guarantee that confirm/promotion applies to the exact bytes compiled/tested.

- initial scanner emits a verification-input manifest/tree digest;
- all build/test/publish stages operate with that manifest as the expected source identity;
- confirm fails if any covered input changed;
- add a negative test that mutates source between scan and confirm and proves promotion is rejected.

## Checkpoint B — stage contract fingerprint

Goal: make `PASS-CACHED` mean the same check under the same policy.

- include cache algorithm/contract version;
- include exact stage command/arguments;
- include relevant verifier script/tool hashes;
- add independent cache regression tests for command/filter/policy changes;
- separately expand MSBuild external input coverage.

## Checkpoint C — executable declaration/context coverage

Goal: close the gap between "callable fingerprint" and "authored executable semantics."

- inventory field/property initializers and primary-constructor/declaration execution;
- cover invocation/object creation in those nodes;
- decide how source-generator-driving declarations affect trust;
- document whether semantic binding context is intentionally included or excluded;
- add focused mutations that affect only those contexts.

## Checkpoint D — CI supply-chain least privilege

Goal: reduce the ability of build/test code to mutate `main`.

- pin GitHub Actions to full reviewed commit SHAs;
- build/test with `contents: read`;
- set checkout `persist-credentials: false`;
- create a separate evidence-persistence job with minimal `contents: write`;
- verify exact source SHA before any push;
- preserve non-force push behavior.

## Checkpoint E — dependency reproducibility

Goal: make source SHA + dependency evidence reproducible.

- evaluate checked-in NuGet lock files and locked mode;
- define package sources explicitly where appropriate;
- record lock/resolved graph digest in evidence;
- preserve direct central version pinning.

## Checkpoint F — continuity adversarial fixtures

Goal: make the validator reject semantically broken handoffs that retain keywords.

- add negation/contradiction/dead-comment/read-order-deception fixtures;
- improve structural validation without relying on one exact prose template;
- test the changed validator independently before the normal verifier runs;
- obtain exact hosted Windows closure because this changes what green means.

## Checkpoint G — immutable closure evidence

Goal: prevent evidence-path drift.

- write SHA/run-named evidence;
- keep a machine-readable current pointer;
- record action SHAs, SDK, package-lock/graph digest, source SHA, run ID, publish mode and artifact SHA.

---

# 9. Do-not-do list for the next agent

- Do not weaken any verifier/test just to make a candidate green.
- Do not manually edit verification booleans to transfer trust.
- Do not treat 615/615 as a claim about code outside the callable inventory.
- Do not change workflow write permissions casually inside an unrelated product checkpoint.
- Do not fix the regex validator inside a production storage/UI change.
- Do not force-push evidence over a newer `main`.
- Do not overwrite the PlannerSnapshotRepository candidate's pending verification state with this documentation-only audit.
- Do not combine multiple verification-infrastructure hardenings into one opaque checkpoint.

The successor inherits the permanent continuity constitution and active Learned Rules. Before any modification it must re-check canonical `main`, current verification state, and recent relevant history. Before finishing it must update durable handoff state and explicitly require its own successor to preserve and recursively propagate the same rules to the agent after them.

**Do not break the chain.**
