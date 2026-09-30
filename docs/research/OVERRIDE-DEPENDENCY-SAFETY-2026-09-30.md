# Override and dependency safety — 2026-09-30

## Goal

Make file precedence and dependency resolution deterministic, explainable, and fail-closed. The manager may automate a choice only when it can prove the effective deployed filesystem remains internally consistent. Ambiguity must block or skip instead of degrading to package priority.

## Research basis

The implementation was checked against current Monster Hunter: World mod-manager guidance describing same-path file replacement/load-order behavior, current MHW loader-generation concerns, deterministic package-solver practice, and dependency-solving literature. The important design conclusion is to separate two questions:

1. Is the requested mod set dependency-satisfiable?
2. Given that set, which exact provider survives at each deployed path?

A source package merely containing a dependency is not enough. The required file/provider must survive conflict resolution in the effective deployment plan.

## Safety invariants

- Every non-blocking multi-provider path has one concrete enabled winner. Never fall back to priority if the resolver returns no winner.
- Differing root bootstrap binaries/configuration fail closed unless an explicit exact-path decision exists. This includes loader DLL/ASI/EXE surfaces that can prevent game startup.
- Native loader capability is a complete pair: `dinput8.dll` and `loader.dll` must survive together from one effective provider; tracked partial loaders and mixed-generation winners are invalid.
- Structural atomic bundles cannot be assembled from unrelated/ambiguous providers.
- File-vs-directory topology collisions block before deployment.
- Executable/plugin collisions require explicit intent or high-confidence same-source lineage; local naming/overlap heuristics are not enough.
- Dependency constraints support exact/min/max versions and optional relationships. Unknown or untrustworthy version metadata cannot satisfy a constrained hard dependency by guess.
- Hard dependency cycles are analyzed as strongly connected groups; every member/edge still has to be selected and version-compatible.
- Required paths are checked against the effective plan, not only package contents or a currently-live managed file that the plan will remove.
- Auto Populate preserves user-selected anchors, rejects fully shadowed additions, rejects additions that silently override protected content, and revalidates the final effective plan before returning a setup.
- Preview, Apply, recovery, crash-diagnosis direct apply, safe-mode restore, and launch health all use the same plan-aware dependency preflight.

## Regression requirements

Keep coverage for incompatible dependency versions, prerelease-vs-release minimums, optional dependencies, dependency cycles, missing/superseded staged identities, managed-live dependencies scheduled for removal, partial native loaders, protected bootstrap collisions, deterministic repeated planning, file/directory collisions, atomic structural bundles, and protected Auto Populate selections.

## Rule of thumb

When safety evidence is incomplete, preserve user state and stop before filesystem mutation. A safe false negative is preferable to a guessed overwrite that can create a mixed or unloadable game state.
