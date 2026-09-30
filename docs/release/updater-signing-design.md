# Independent updater signing design

Status: implementation contract for GitHub issue #350. This document defines the trust boundary that must be satisfied before the issue can be closed.

## Threat model

The updater already validates immutable GitHub release state, exact asset identity and size, SHA-256 digests, path containment, staging, installed product manifests, and rollback. Those controls establish integrity relative to release metadata, but the release artifact and its manifest are still authorized by the same GitHub publication authority.

The added control must ensure that possession of ordinary repository/release-write credentials alone is insufficient to authorize arbitrary updater code.

## Cryptographic choice

Use a small TUF-inspired signed metadata envelope with **ECDSA P-256 + SHA-256**.

Rationale:

- .NET 10 exposes ECDSA signing/verification in the platform cryptography API, avoiding a new updater crypto dependency.
- The private signing key can remain completely outside the repository and outside ordinary GitHub release-write credentials.
- A client key ring can contain public SubjectPublicKeyInfo material only.
- The scheme can support overlapping keys during controlled rotation without weakening existing updater checks.

This is intentionally not a claim to implement the full TUF specification. It adopts the relevant trust-separation, expiration, rollback, and key-rotation properties while preserving the existing updater architecture.

## Signed payload

The signature must cover a deterministic canonical payload containing, at minimum:

1. schema version;
2. signing key id;
3. channel;
4. product version;
5. full source commit SHA;
6. updater build number;
7. artifact file name;
8. artifact size;
9. artifact SHA-256;
10. product-manifest SHA-256;
11. minimum updater protocol;
12. issued-at UTC timestamp;
13. expiry UTC timestamp.

Canonicalization must be implemented in one shared, test-covered routine. Do not sign serializer-dependent pretty-printed JSON or rely on implicit property ordering.

## Client trust root

The updater binary embeds only a public key ring. Each key record must include:

- stable key id;
- algorithm identifier;
- public SubjectPublicKeyInfo bytes;
- first accepted build;
- optional last accepted build / revocation boundary.

Unknown keys fail closed. Duplicate key ids fail closed. Invalid key material fails closed.

A signing private key must never be checked into this repository, written to release artifacts, logged, serialized into updater metadata, or stored alongside the normal GitHub release token.

## Verification order

A release candidate is not trusted merely because GitHub reports the release as immutable.

The updater must perform this order before accepting a candidate for staging:

1. download the bounded signed metadata/envelope;
2. parse with strict schema and size limits;
3. identify an embedded trusted public key by key id;
4. reject metadata outside its validity window;
5. reject a build that rolls back below the installed/current build;
6. reconstruct the canonical signed payload;
7. verify the ECDSA P-256/SHA-256 signature;
8. compare the signed fields to the release tag, manifest, artifact declaration, and product-manifest digest;
9. only then permit the existing artifact download and SHA-256 verification path.

Existing checks remain mandatory and additive.

## Freshness and freeze protection

Signed metadata has an explicit expiry. Clients reject expired metadata rather than accepting an indefinitely replayable signed release.

Expiry policy must leave enough operational recovery time that an unavailable signer does not immediately strand clients. The release operator runbook must define the normal signing cadence and emergency re-sign procedure.

The updater must never accept a lower build than the installed/current build even when the old metadata has a valid signature.

## Rotation and recovery

Normal rotation uses an overlap window:

1. ship client version N containing old key A and new key B;
2. configure key B with a future/explicit first accepted build;
3. publish releases signed by B only after enough clients can recognize B;
4. retire A with an explicit last accepted build;
5. remove A from a later client after the retirement boundary is safely past.

Compromise recovery must be fail closed. Do not silently accept unsigned metadata or an unknown replacement key because the configured signer is unavailable.

If all currently trusted signing keys are compromised, recovery requires an out-of-band client update / new trusted binary or another pre-established recovery key. Document this operationally rather than adding an insecure network key-discovery escape hatch.

## Key ceremony

Before signature enforcement can become production-required:

1. generate the production signing key on a trusted operator-controlled system;
2. store the private key in a signing service, hardware-backed store, or protected operator secret store that is independent from ordinary GitHub release-write credentials;
3. export only the public key;
4. review and commit the public key/key id to the updater trust ring;
5. configure the release signer to access the private key without printing/exporting it;
6. run a test release and verify the installed client accepts the valid signature and rejects all negative fixtures;
7. preserve a documented backup/recovery process for the signing identity.

The key ceremony is an external trust-anchor operation. Source code must not fabricate a production private key as a convenience.

## Mandatory regression coverage

At minimum:

- valid signature accepted;
- wrong key rejected;
- unknown key id rejected;
- malformed public key rejected;
- malformed signature rejected;
- one-byte signed-payload mutation rejected;
- artifact digest mutation rejected;
- product-manifest digest mutation rejected;
- source SHA/channel/build/version mutation rejected;
- expired metadata rejected;
- not-yet-valid metadata rejected when applicable;
- rollback build rejected;
- rotated key accepted only inside its configured build window;
- retired/revoked key rejected outside its window;
- unsigned metadata rejected after the signed-metadata protocol is required;
- existing immutable-release, asset-size, SHA-256, extraction/path, install verification, and rollback tests continue to pass.

## Publication requirements

The release pipeline must fail closed if a release eligible for the signed-update protocol lacks a valid independently produced signature.

Signing must happen after deterministic artifact/manifest hashes are known and before publication is considered complete. Publication verification must check both the signature and the GitHub-side immutable release/digest properties.

Do not make the ordinary GitHub release token the only credential needed to produce a valid signature.

## Completion evidence for issue #350

Do not close #350 until all of the following are true:

- production public verification material is embedded in the client;
- the corresponding private key exists outside the repository and outside ordinary release-write credentials;
- release tooling produces independently signed metadata;
- the client verifies the signature before staging/execution is accepted;
- negative/rotation/expiry/rollback tests pass;
- a real release is published and verified end to end;
- operator rotation/revocation/recovery instructions are tested and documented.

## References

- OWASP ASVS application-integrity requirement for digitally signed auto-updates and verification before install/execute: https://owasp.org/
- The Update Framework overview, metadata roles, expiration, rollback/freeze protection, and key separation: https://theupdateframework.io/docs/
- Microsoft .NET cryptographic signature and ECDSA APIs: https://learn.microsoft.com/dotnet/standard/security/cryptographic-signatures
