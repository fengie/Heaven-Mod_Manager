# Plugin Tooling

Repository-owned development tooling for two reusable jobs: selecting the narrowest implemented Heaven capability before a generic fallback, and preserving reusable plugin gaps in the canonical backlog.

## Capability index and resolver

tooling.py generates a machine-readable index directly from every plugin manifest, validates runtime plugin package structure, rejects duplicate exact capability providers, and resolves exact capabilities, namespaces, prefixes, or task tags without executing them.

    python .\plugins\_tooling\tooling.py index-validate
    python .\plugins\_tooling\tooling.py index-build --output toolbox-index.json
    python .\plugins\_tooling\tooling.py resolve git --machine heaven
    python .\plugins\_tooling\tooling.py resolve browser.navigate --machine heaven2 --runtime-capability browser.navigate

Resolver output includes match reasons, package precedence, explicit runtime-availability evidence, README location, validation command, and entrypoint. Missing runtime evidence is reported as unknown rather than guessed.

## Installed plugin version pruning

`prune_outdated_plugins.py` removes stale local/custom plugin install directories only when a newer strict-SemVer copy with the same manifest `name` is present. It protects canonical repo source, skips symlinked/non-SemVer/ambiguous copies, and can run dry before deletion.

    python .\plugins\_tooling\prune_outdated_plugins.py
    python .\plugins\_tooling\prune_outdated_plugins.py --apply --repo-root .

`Install-PluginVersionPruner.ps1` copies the pruner into local application data, installs a hidden logon + daily Task Scheduler safety net, writes the last JSON result under `%LOCALAPPDATA%\MHW-Plugin-Maintenance\last-prune.json`, and adds a hidden Startup-folder fallback. Upgrade/install workflows must still invoke pruning immediately after the replacement version is verified.

## Plugin-gap planner

The same CLI validates PLUGIN_GAP_BACKLOG.md and drafts complete schema-conforming entries while checking likely duplicates first.

    python .\plugins\_tooling\tooling.py gap-validate
    python .\plugins\_tooling\tooling.py gap-plan --title "example adapter" --use-case "..." --required-capability "example.resolve"

It does not write the backlog. The agent or human reviews the draft and chooses whether a possible duplicate should be extended.

## Security boundary

This tooling reads repository metadata and text only. It does not execute resolved capabilities, grant permissions, activate connectors, read credentials, or persist secret values. Runtime availability is caller-supplied evidence.

## Validation

    python .\plugins\_tooling\verify.py
    python .\plugins\verify.py
