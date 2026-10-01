# v8.8.63 crawler containment — current handoff

Canonical MHW product repository: `fengie/mhw-mods`  
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`  
Active PR: #555  
Candidate source commit: `76205544ed23422cfb4056dfa88787a162e9a2fd`  
Current branch: `fix/issue-554-crawler-containment-v8.8.63`

## Candidate change

Issue #554 hardens the permitted HTML crawler before any real HTML provider is enabled:

- path prefixes use segment boundaries, so `/mods` cannot authorize `/mods-evil`;
- malformed, encoded-separator, and dot-segment path inputs fail closed before network dispatch;
- production transport is crawler-owned with automatic redirects disabled;
- redirects are followed manually with a five-hop bound, loop detection, and origin/path validation before every follow-up request;
- deterministic tests record attempted requests and prove disallowed redirect targets are never contacted;
- existing compliance review, robots/terms checks, kill switch, response MIME/size bounds, attribution metadata, cancellation, and provider isolation remain in place.

## Verification state

This is a **candidate**, not yet an integrated claim. MHW Product Security and Heaven Toolbox Ownership gates have passed on intermediate exact heads. The Workflow Feature gate exposed and drove fixes for compile/analyzer errors, handoff-version drift, and constructor function-trace coverage. Merge PR #555 only after all required gates pass on the same final exact head.

## Unresolved risks and remaining work

- No provider-specific HTML adapter is enabled by this change; a future adapter still requires its own reviewed compliance manifest and deterministic parser fixtures.
- Issue #281 remains limited to real conditional Steam Workshop/Vortex/provider contracts.
- Issue #350 still requires an external production updater signing trust anchor/private-key ceremony and a real signed release.
- Issue #354 still requires external repository-admin/publisher controls for full closure.
- RECOVERY-007 representative Windows installed-game/runtime proof and RECOVERY-005 installed WPF interaction acceptance remain evidence gaps.
- Older issue #281 recovery branches remain preserved until unique ancestry is classified.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full. Refresh live findings/ownership before mutation. Preserve unresolved evidence/security boundaries and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
