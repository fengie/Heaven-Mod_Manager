# Archive streaming budget — local Windows closure evidence

- Candidate source: `b0be45beed223e0b0fd06ab719f696028581e626`
- Host: heaven2, Windows x64
- SDK: .NET 10.0.401
- `scripts/Verify-Release.ps1`: 25/25 PASS
- Function scan: 616 functions; 0 trace gaps; 6555 call sites; 0 uncovered; 0 parse errors
- Core: 79/79 (release build)
- Automation: 24/24
- Integration/fault injection: 98/98
- Self-test: 11/11
- Strict solution/project analyzers: PASS
- App win-x64 compile/analyzers: PASS
- ReadyToRun self-contained publish: PASS
- ReadyToRun fallback: False
- Release ZIP SHA-256: `50B320A6352D2335D77D6864EA456FEE3FF582D82F5102338882C083CE7B2E40`

Verification state was promoted only through the normal repository verifier/build scripts. Hosted integration-gate evidence is still required before canonical closure.
