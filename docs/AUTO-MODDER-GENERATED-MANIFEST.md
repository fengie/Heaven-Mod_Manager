# Auto Mod generated manifest v1

Format name: mhw-auto-mod-output
Format version: 1

The manager-authored manifest records the identity needed to reproduce and diagnose a generated mod without embedding copyrighted source bytes.

Required semantic fields:

- format and formatVersion;
- recipeId and recipeVersion;
- adapterVersions keyed by stable adapter ID;
- gameBuild when known;
- sourceFingerprints keyed by source alias;
- normalized portable inputs;
- outputFingerprints keyed by manager-relative output path.

Fingerprint strings use an algorithm prefix, for example sha256:<hex>.

The foundation serializer writes dictionaries in ordinal key order so repeated serialization is stable for the same normalized values. Adapters remain responsible for deterministic output bytes and for recording the exact source/output hashes used by the completed build.

Secrets, access tokens, arbitrary environment data, and unnecessary absolute user paths are forbidden in portable manifests.
