# Toolbox security consumer

The MHW security workflow runs product updater/release assertions and the canonical
Toolbox generic workflow, unsafe primitive, tracked-secret, and PowerShell syntax
checks. Shared scanner implementations stay in Toolbox.

Provision a dedicated clean checkout of `fengie/heaven-toolbox` at the exact full
commit in `toolbox-security-policy.json` on the trusted Windows runner. Set the
non-secret runner environment variable `HEAVEN_TOOLBOX_ROOT` to that checkout.
The adapter also recognizes `$HOME/HeavenBridgeSource` and
`$HOME/local-ai-workspaces/heaven-toolbox` when no explicit path is configured.
An active relay checkout on another branch is unsuitable: keep the pinned checkout
separate so CI cannot interrupt live bridge transport.

Provisioning uses the operator's existing authorized read access outside the job.
The workflow never fetches credentials or exposes repository secrets to PR code.
Missing checkout, wrong origin/revision, missing scripts, or dirty security scripts
fail closed. Update the pin only after reviewing and verifying the new Toolbox
revision and provisioning it on the runner.

`mhw-security-consumer-policy.json` permits only two exact contents-write workflow
names, with product publication/evidence reasons. It does not exempt PR credential,
action pinning, checkout, or unsafe source checks.

Run `Test-ToolboxSecurityBaseline.Tests.ps1` for negative adapter fixtures; supply
`-ToolboxRoot` to additionally exercise a clean pinned canonical checkout. Run
`Test-ToolboxSecurityBaseline.ps1 -Root <MHW> -ToolboxRoot <pinned Toolbox>` for
the same baseline used in CI. Source/configuration tests do not prove a live CI run.
