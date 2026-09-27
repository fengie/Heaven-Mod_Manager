# Remote Preview Network Trust Audit

Date: 2026-09-27
Canonical main inspected: 6ada5a5c4cc83afadfba42bc6af6559540920e3d
Branch: agent/support-network-preview-security-audit-20260927

## Scope and independence

This support lane audits remote preview/network trust in:
- src/MhwModManager.Filesystem/NexusMetadataService.cs
- tests/MhwModManager.IntegrationTests/VisualMetadataTests.cs
- tests/MhwModManager.IntegrationTests/HardeningTests.cs

It deliberately does not overlap the active SQLite, CI/supply-chain, MainWindow/WPF, broad test/performance, async-lifetime, Windows-filesystem, multi-source discovery, or MHW semantic-coverage support lanes.

## Top finding: P1 network destination trust gap

Normal metadata refresh calls TryRefreshDeclaredVisualsAsync for every mod before Nexus API-key/live-sync gating.

ReadDeclaredImageUrls accepts image URL fields from imported package metadata:
- mod-manager.meta.json
- mhw-manager.meta.json
- meta.ini
- vortex.meta.ini

Accepted keys include pictureUrl, imageUrl, thumbnailUrl, previewUrl, and underscore variants. JSON is walked recursively.

The only destination check is IsHttpImageUrl, which accepts any absolute HTTP or HTTPS URI. Every accepted value can flow into CacheRemoteImageAsync, which calls the shared HttpClient.

This means imported package metadata can select a network destination during normal refresh. The current code has no policy that rejects loopback, private, link-local, multicast, unspecified, or otherwise non-public targets. This does not require a Nexus API key.

This is a confirmed source-level control-flow finding. No live network exploit was run.

## P1 redirect-policy gap

CreateHttpClient constructs a plain HttpClient with a timeout and no explicit handler policy.

Microsoft documents automatic redirect following as enabled by default for HttpClientHandler. Therefore a future fix that validates only the initial URL would still be insufficient: redirect destinations need to be validated as well.

Reference:
https://learn.microsoft.com/dotnet/api/system.net.http.httpclienthandler.allowautoredirect

OWASP's SSRF prevention guidance specifically recommends disabling automatic redirects when URL validation is part of the defense and warns that image URL fetching is a common server-side request pattern.

Reference:
https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html

## Nexus API credential transport risk

GetJsonAsync adds a custom apikey header to requests for the fixed https://api.nexusmods.com/v3/ origin.

The current fixed API origin and escaped identifier construction are good properties and should be preserved.

However, Microsoft documents that automatic redirect handling clears Authorization but does not clear other headers. The current transport therefore lacks an explicit invariant proving the custom apikey header can never accompany a cross-origin redirected request.

This audit classifies that as a risk requiring a deterministic redirect test, not as a demonstrated credential leak.

Recommended contract for the credentialed API client:
- fixed HTTPS origin;
- no cross-origin redirects;
- preferably fail closed on any redirect unless Nexus explicitly requires one;
- only add credentials after final destination policy is established.

## P2: plain HTTP preview URLs are accepted

IsHttpImageUrl accepts both HTTP and HTTPS.

For remote artwork derived from imported metadata, the safe default should be HTTPS-only. Any intentional local-network image feature should be a separate explicit user-controlled feature, not inferred from a mod package sidecar.

## P2: response-size limits are incomplete

Public Nexus HTML:
- declared Content-Length above 4 MB is rejected;
- but when length is unknown, ReadAsStringAsync materializes the response before html.Length is checked.

Therefore the 4 MB value is not a hard read/allocation cap for unknown-length responses.

Nexus JSON:
- GetJsonAsync streams directly into JsonDocument.ParseAsync;
- there is no explicit response-byte limit at the transport boundary.

Preview images are stronger here:
- ResponseHeadersRead is used;
- unsupported MIME types are rejected;
- a streaming 12 MB cap is enforced;
- temp files use unique names and are cleaned in finally.

Those existing protections should be preserved.

## P2/P3: MIME is checked, image bytes are not validated before cache promotion

CacheRemoteImageAsync selects an extension from Content-Type and writes the received bytes to the preview cache when the byte cap passes.

It does not validate the file as a decodable bounded image before File.Move promotes it from the temp path.

A later hardening slice should validate signature/decoder success and sensible dimensions/resources before durable promotion.

## Existing strengths

Preserve these current properties:
1. Nexus API base origin is hard-coded rather than package-controlled.
2. API-key logging is redacted.
3. Remote preview downloads use ResponseHeadersRead.
4. Preview payloads have a streaming 12 MB cap.
5. Only a small image MIME set is accepted.
6. Temp downloads are unique and cleaned in finally.
7. Cache filenames are SHA-256-derived from URL, not remote filenames.
8. FOMOD XML parsing already disables DTD processing and XmlResolver.
9. Background public-page preview attempts are budgeted.

## Existing test gap

VisualMetadataTests verifies local gallery behavior and FOMOD image discovery, but has no remote destination-policy tests.

HardeningTests covers deployment/recovery, scanner, archive traversal, SQLite, adoption, family behavior, and game-build behavior, but not network egress.

Missing deterministic tests include:
- loopback destination rejected before a connection attempt;
- private IPv4 rejected;
- local/private IPv6 rejected;
- public destination redirecting to a blocked destination rejected;
- HTTPS-only preview policy;
- credentialed API redirect does not expose apikey;
- oversized unknown-length HTML/JSON stops at a hard cap;
- invalid image bytes are not promoted to cache.

These should use fake handlers/resolvers and not the public Internet.

## Recommended production checkpoint

Implement one isolated network-security boundary only:

### RemoteFetchPolicy plus injectable transport

Do not mix this with multi-source downloading, WPF work, database decomposition, or filesystem containment.

Recommended design:
1. Separate credentialed Nexus API transport from uncredentialed preview transport.
2. Make transport injectable through HttpMessageHandler or a narrow interface.
3. Disable automatic redirects.
4. Validate every redirect hop.
5. Require HTTPS for remote previews.
6. Reject non-public destinations.
7. Validate the actual resolved connection target, not only the hostname string, so DNS changes cannot bypass policy.
8. Keep credentialed API requests on the fixed Nexus API origin.
9. Add hard streaming byte limits for HTML, JSON, and images.
10. Validate image content before cache promotion.
11. Preserve cancellation, timeout, log redaction, streaming download, temp cleanup, and current cache naming.

Microsoft exposes connection-level customization through SocketsHttpHandler / ConnectCallback if that is needed for a connection-time address policy:
https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler

## Acceptance criteria for that later checkpoint

- imported package metadata cannot cause requests to non-public local/private destinations;
- redirects cannot bypass destination policy;
- connection-time address selection cannot bypass destination policy;
- arbitrary plain HTTP preview URLs are rejected;
- Nexus API credentials cannot leave the intended API origin;
- blocked URLs fail best-effort without breaking startup;
- HTML/JSON/image bodies are bounded even without Content-Length;
- invalid image data is not promoted;
- deterministic tests do not require real Internet access;
- the exact production source passes the normal Windows Release Gate;
- verification cache is never manually promoted.

## Verification actually performed

Performed:
- verified canonical main and exact HEAD;
- inspected recent commits and open PRs to avoid duplicate support work;
- read canonical continuity/start files;
- inspected the actual network-related NexusMetadataService method bodies;
- inspected current visual and hardening test bodies;
- checked current Microsoft redirect behavior documentation;
- checked OWASP defensive guidance.

Not performed:
- no live request to local/private infrastructure;
- no real Nexus request;
- no dotnet test;
- no hosted Windows Release Gate;
- no benchmark;
- no production code change;
- no verification-cache promotion.

## Parallel integration / continuity

This audit is the specialized authority for remote preview network egress, redirect handling, destination policy, and Nexus credential transport.

Other audits remain authoritative for their own boundaries, especially SQLite transactions, Windows filesystem containment, async lifetime, CI verification, and multi-source provider architecture.

Several parallel support branches are editing the shared continuity files. To avoid overwriting or conflicting with that work, this branch intentionally adds only this standalone audit. When integrated, a successor should link it from the current handoff/read-order files if still applicable.

A durable Learned Rule is justified in principle, but no rule number is assigned here because parallel branches may add rules first:

"Untrusted package metadata must not directly authorize network destinations; remote fetches require an explicit egress policy that validates redirects and actual connection targets."

## Recommended next independent checkpoint

Start with deterministic failing tests for:
- imported sidecar loopback/private destinations;
- redirect from an allowed public destination to a blocked destination.

Then implement the smallest RemoteFetchPolicy/transport seam that makes those tests pass.

The successor must inherit and preserve the permanent recursive continuity constitution and require the next successor to propagate it again.

Do not break the chain.
