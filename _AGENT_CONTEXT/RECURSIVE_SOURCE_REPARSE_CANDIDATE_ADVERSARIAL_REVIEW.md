# Recursive source reparse candidate — adversarial support review

## Canonical / candidate state

- canonical repository: `fengie/mhw-mods`
- canonical `origin/main` inspected initially at `831da365c0c67e0239ad668f60fe3c78513dc63b`, then reconciled to `ef9c0dc93ca5270e9ee5ab3dea080b2fcb461585`
- active candidate reviewed: `agent/recursive-source-reparse-hardening-20260927`
- production source implementation reviewed and runtime-probed: `f51f72927e8f90c264df1ef197ba6c9bbe2704de`
- latest candidate evidence/continuity head observed: `5e4b4cc1a524a28640367ce22e1ceee2d62d1134` (production source remains `f51f729`)
- production merge base: `b34bb5a3e2b5fe1b5fc69db83cc6ab19f84297eb`
- latest observed relation: canonical main and candidate remain diverged; reconcile again immediately before integration

This support lane does not modify the candidate's production source or tests. It independently reviews the active production boundary so the programmer can repair gaps without another agent editing the same files.

## Scope and methodology

Inspected the actual candidate bodies and tests for:

- `SafeRecursiveTraversal`
- `ModScanner`
- `UnmanagedAdoptionService`
- `SmartInboxService`
- `HardeningTests`
- `AutomationServiceTests`
- `AutoCategoryService.ClassifyPaths`

Compared them with the canonical `RECURSIVE_SOURCE_REPARSE_CONTAINMENT_AUDIT.md` acceptance matrix and active LR-004 / LR-008 constraints.
## P1 confirmed — safe-tree Smart Inbox classification can change

The candidate replaces Smart Inbox category enumeration:

`Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)`

with:

`SafeRecursiveTraversal.Snapshot(destination, ct).Files`.

The new traversal uses a LIFO `Stack<string>`. On Windows, a two-sibling directory fixture produced:

- old recursive order: `A\one.tex | A\two.tex | B\npc.bin`
- candidate order: `B\npc.bin | A\one.tex | A\two.tex`

This is not merely cosmetic. `AutoCategoryService.ClassifyPaths` accumulates scores in insertion order and its near-tie rule specially preserves Texture only when Texture is the first top-scoring category.

A runtime probe against candidate `f51f729` produced:

- old category: `Texture`
- candidate category: `Mixed`

The fixture contains no reparse points. Therefore the safety patch currently changes ordinary successful behavior, violating the checkpoint's preservation requirement.

Recommended repair: preserve the old traversal ordering with a parity regression, or separately make classification tie-breaking explicitly order-independent. Do not silently accept the category change as part of the reparse boundary.
## P1 verification gap — canonical reparse matrix is incomplete

The candidate currently adds descendant-junction coverage for:

1. scanner;
2. unmanaged adoption;
3. Smart Inbox direct-directory import.

The canonical audit also requires, and the branch does not yet contain, focused coverage for:

- scanner root reparse;
- scanner file-reparse leaf;
- scanner bounded reparse cycle;
- adoption live-root reparse;
- Smart Inbox top-level reparse item;
- Smart Inbox bounded reparse cycle.

Static inspection suggests `SafeRecursiveTraversal` intends to reject root, descendant-directory, and leaf-file reparses and should fail promptly on a junction cycle because the reparse entry is inspected before recursion. Those cases were not runtime-proven by this review and should not be claimed green yet.

File-symlink creation may require a narrow Windows-capability guard or fixture seam, but at least one real directory-junction fixture must remain.

## Existing strengths to preserve

- Smart Inbox snapshots the direct-directory source before creating the final destination, so the tested descendant-junction case fails before catalog-visible publication.
- Scanner snapshots candidates before CAS capture / `mod_files` replacement.
- Adoption discovery snapshots before creating its managed source folder or adoption rows.
- Root and leaf entries are explicitly checked for `FileAttributes.ReparsePoint`.
- Cancellation is checked between directories and entries.
## Parallel candidate evidence observed

After this review began, the implementation branch added evidence/continuity commit `0185443ac561a73c7f6b44416cc152ae1dc20171`, followed by documentation-only head `5e4b4cc1a524a28640367ce22e1ceee2d62d1134`. The branch records a full local Windows `Verify-Release.ps1` **25/25** and successful `Build-Release.ps1` for production source `f51f729`, including functions **614/614**, call sites **6512 / 0 uncovered**, Core **79/79**, Automation **21/21**, Integration **91/91**, self-test **11/11**, and ReadyToRun publication. The latest documentation also records a hosted-gate dispatch limitation; it does not change production source.

That stronger gate evidence is important but does not invalidate the behavior-parity finding above: `0185443` does not change the reviewed production traversal/classification source, and the current suite has no regression asserting old-vs-new ordering/category parity for the demonstrated safe-tree fixture. The branch itself still records root/file-leaf/cycle fixture coverage as a residual.

## Verification actually performed on heaven2

Candidate production SHA `f51f72927e8f90c264df1ef197ba6c9bbe2704de`:

- direct IntegrationTests DLL execution: **91/91 PASS**;
- AutomationTests Release build: **0 warnings / 0 errors**;
- direct AutomationTests DLL execution: **21/21 PASS**;
- targeted Windows enumeration-order probe: reproduced old/new sibling-order reversal;
- targeted classification probe: reproduced **Texture -> Mixed** on an ordinary non-reparse tree.

An initial `dotnet test --filter` attempt used an unsupported runner path and executed zero tests with exit code 5. It is intentionally not counted as evidence.

Not performed:

- full `Verify-Release.ps1`;
- full `Build-Release.ps1`;
- hosted Windows Release Gate;
- root-reparse runtime fixture;
- file-symlink runtime fixture;
- bounded cycle runtime fixture;
- verification-cache promotion.

## Integration / successor handoff

Before candidate integration:

1. repair or explicitly resolve the confirmed classification-order regression;
2. add the missing root / leaf / bounded-cycle acceptance cases;
3. merge/rebase the latest canonical main instead of overwriting its newer evidence commit;
4. rerun focused Windows tests, then the exact full Windows verification/build gates;
5. document the path-check/open TOCTOU residual honestly; this review does not claim handle/file-ID atomicity;
6. update canonical continuity from then-current main, not from stale branch-local snapshots;
7. preserve and recursively pass the permanent continuity constitution and active Learned Rules to the successor and the agent after them.

No new Learned Rule is added here: the confirmed regression exists only on an unmerged candidate. If the lesson becomes a canonical incident, consider an append-only rule requiring behavior-parity checks when replacing recursive enumeration with a custom traversal.
