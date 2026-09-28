# Remote preview network egress hardening — 2026-09-28

## Canonical baseline and assignment

- Canonical repository: `fengie/mhw-mods`.
- Canonical `origin/main` inspected before implementation: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Support branch: `agent/support-remote-preview-egress-hardening-20260928`.
- Exact locally verified production candidate: `66a799074a066ddb237903111d89c08df2681528`.
- Prior specialized authority: `_AGENT_CONTEXT/REMOTE_PREVIEW_NETWORK_TRUST_AUDIT.md`.

Selected boundary: close the P1 path where imported package sidecar metadata could choose arbitrary HTTP(S) preview destinations, including local/private addresses, while the same shared client also carried credentialed Nexus API traffic.

Deliberate exclusions: response-body hard caps for Nexus HTML/JSON, image decode/signature validation, preview-cache publication lifetime, updater work, archive streaming, migration, support-bundle privacy, and visual reparse containment.

## Pre-fix runtime reproduction

A real loopback regression was created before production changes. A package sidecar declared:
`http://127.0.0.1:<ephemeral-port>/preview.png`.

On production source matching canonical base `4fd61dd...`, the local TCP listener accepted the connection. That runtime result confirmed that imported sidecar metadata could authorize loopback egress during normal metadata refresh.

The request then exposed a separate Windows cache bug: `CacheRemoteImageAsync` reached `File.Move(temp,path,false)` while its output `FileStream` was still open and threw `IOException: The process cannot access the file because it is being used by another process.`

## Implemented network boundary

The candidate separates network trust roles:

- `PreviewHttp` is uncredentialed and used only for remote preview images.
- `NexusHttp` is used for fixed-origin Nexus page/API traffic.
- Both clients disable automatic redirects.
- Declared/API/meta preview URLs must be absolute HTTPS URLs with no userinfo.
- Preview transport disables proxy use so the connection callback validates the actual requested destination rather than a proxy endpoint.
- `SocketsHttpHandler.ConnectCallback` resolves the preview host, rejects non-public address classes, and connects directly to an approved resolved address.
- If any DNS answer is non-public, the request fails closed rather than selecting another answer.
- Loopback, RFC1918/private IPv4, link-local, carrier-grade NAT, multicast/reserved/documentation ranges covered by the policy, IPv4-mapped loopback, IPv6 loopback, link-local, unique-local, multicast, site-local, unspecified/IPv4-compatible, and documentation IPv6 are rejected.
- Declared preview failures remain best-effort and do not abort metadata refresh.
- Credentialed Nexus traffic cannot automatically follow a redirect because `AllowAutoRedirect=false`.

No manual redirect-following path was added.

## Deterministic regressions

`RemotePreviewNetworkTrustTests` covers:

- HTTP loopback sidecar rejection before connection;
- HTTPS loopback rejection at the actual connection boundary before socket connection;
- representative blocked IPv4 and IPv6 ranges;
- representative public IPv4 and IPv6 addresses;
- preview transport `AllowAutoRedirect=false`;
- preview transport `UseProxy=false` and a non-null connection callback;
- Nexus transport `AllowAutoRedirect=false`.

Focused network regressions passed **18/18**. The complete Integration/fault-injection project passed **114/114**.

The first repository verification run correctly failed **24/25** because changing `EnumerateJsonImageUrls` invalidated its exact function fingerprint and the method lacked the required entry trace. The production behavior was not weakened to bypass the gate; the missing `MasterDebugLog.BeginMethod()` scope was added, then the full gate was rerun.

## Local Windows closure

Exact candidate `66a799074a066ddb237903111d89c08df2681528` was verified on heaven2 / Windows x64 / .NET SDK 10.0.401.

`scripts/Verify-Release.ps1`:
- **25/25 PASS**;
- functions **623/623** promoted by the normal verifier;
- explicit call sites **6571 / 0 uncovered**;
- trace gaps **0**; parse errors **0**;
- Automation **24/24**;
- Integration/fault injection **114/114**;
- self-test **11/11**;
- strict project and whole-solution analyzers/builds PASS.

`scripts/Build-Release.ps1`:
- Core **79/79**;
- Automation **24/24**;
- Integration/fault injection **114/114**;
- self-test **11/11**;
- App win-x64 compile/analyzers PASS;
- ReadyToRun self-contained publish PASS;
- release ZIP SHA-256 `065B62826C9CAD513EE6C79D28F660A14FD39C14D752F194EBD0D01B21FE3A5D`.

Verification caches were promoted only by the repository verifier/build scripts, never manually.

## Remaining risks / deliberately not closed

1. **Preview-cache publication lifetime — confirmed runtime defect, separate checkpoint.** The pre-fix loopback reproduction showed `File.Move` can execute while the async output stream remains open on Windows. Fix this with a focused successful-download regression that asserts durable cache publication after the streams are disposed.
2. **HTML/JSON hard byte limits remain open.** Unknown-length public Nexus HTML and API JSON are not yet bounded at the transport stream boundary.
3. **Image content validation remains open.** MIME is checked, but decoded image validity/dimensions/resource cost are not validated before promotion.
4. **No live Nexus compatibility request was run.** Disabling redirects is intentionally fail-closed, but real current Nexus page/API redirect behavior was not exercised in this checkpoint.
5. **Proxy-only network environments were not exercised.** Untrusted preview transport deliberately bypasses system proxies so destination validation cannot be bypassed by proxy routing.
6. **Hosted GitHub Windows Release Gate remains pending integration.** Local Windows closure does not make this branch canonical main.

No new Learned Rule was added because this work implements the already-documented network-trust lesson from the prior specialized audit rather than discovering a new invariant.

## Parallel-agent integration notes

During this work the active archive-streaming, support-bundle sanitization, legacy-migration hardlink, updater, PR-disposition, and visual-source-reparse lanes were rechecked. None owns the `NexusMetadataService` network-transport boundary. The updater lane touches IntegrationTests project configuration but not this test file or Filesystem project.

Do not import this branch's continuity snapshots over newer canonical state blindly. Preserve newer branch-disposition and active-candidate information during integration.

## Successor handoff

Review and integrate this branch on top of the then-current `origin/main`, preserve the network regressions, reconcile verification caches by exact input, and run the hosted Windows Release Gate for the exact integrated candidate before declaring the P1 remote-preview egress boundary canonically closed.

Recommended next independent checkpoint after integration: fix and regression-test the confirmed `CacheRemoteImageAsync` stream-disposal-before-`File.Move` publication defect without bundling HTML/JSON/image-decoder policy.

Preserve the permanent recursive continuity constitution and explicitly require the successor to pass it to the agent after them.

**Do not break the chain.**
