# Repository rename safety receipt — October 9, 2026

- Source canonical: `fengie/Heaven-Mod_Manager` (GitHub repository ID 1390581138); old name `fengie/mhw-mods` currently resolves via redirect.
- Product is **public** as of the live GitHub check, independently of chat descriptions. Public history, CI logs, and source should be audited for leaked tokens/machine data and third-party licenses before broader promotion.
- Existing MHW updater GitHub Releases remain in the source repository and in the renamed `fengie/heaven-toolbox-release` public archive, which contains legacy `updater-main-*` MHW artifacts. Do **not** relabel historical ZIPs as Toolbox builds.
- Compatibility rule: maintain accepted update-manifest locations, exact signature/asset digest identity, atomically staged updater replacement, and rollback. Do not rewrite/delete old release records or rename release tags.
- GitHub-managed redirects are NOT an independently proven runtime update protocol. Ensure release URLs and updater signatures are checked against real installed-client tests before retiring any old URL.
- MHW public workflows that use self-hosted Windows runners must deny fork-supplied source on those runners; verified current relevant workflow gates use `github.event.pull_request.head.repo.full_name == github.repository`, but this is not a whole-history secret audit.
- Source update policy lives in `.heaven/update-policy.json`; its `repository` field must equal GitHub's exact `github.repository` after rename.
- Standalone new Toolbox releases require an independently verified producer, signed authorization, trusted artifact provenance and no MHW publisher mixing. Do not announce/deploy them from this metadata repair.
