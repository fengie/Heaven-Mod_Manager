# Remote preview egress — local Windows closure evidence

- Canonical base inspected: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Branch: `agent/support-remote-preview-egress-hardening-20260928`
- Exact candidate source: `66a799074a066ddb237903111d89c08df2681528`
- Host: heaven2, Windows x64
- SDK: .NET 10.0.401
- Focused `RemotePreviewNetworkTrustTests`: **18/18 PASS**
- Full Integration/fault injection: **114/114 PASS**
- `scripts/Verify-Release.ps1`: **25/25 PASS**
- Function scan after normal promotion: **623/623**
- Explicit call sites: **6571 / 0 uncovered**
- Trace gaps: **0**
- Parse errors: **0**
- Core release tests: **79/79**
- Automation: **24/24**
- Self-test: **11/11**
- Strict project / whole-solution analyzers: **PASS**
- App win-x64 compile/analyzers: **PASS**
- ReadyToRun self-contained publish: **PASS**
- Release ZIP SHA-256: `065B62826C9CAD513EE6C79D28F660A14FD39C14D752F194EBD0D01B21FE3A5D`

Pre-fix runtime evidence: a sidecar-controlled loopback preview URL caused an actual TCP connection on canonical-base production behavior. The same reproduction also exposed a separate Windows cache publication lifetime defect at `File.Move` while the output stream was still open.

Verification state was promoted only through the normal repository scripts. Hosted integration-gate evidence remains required before canonical closure.
