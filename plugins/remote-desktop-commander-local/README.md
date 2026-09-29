# Remote Desktop Commander — Local Replacement

Status: **CLAIMED / active**

This project is the local compatibility and parity layer for tasks that would otherwise depend on the external Remote Desktop Commander connector.

It exists because the connector can become unavailable or usage-paused while the owned Windows machines remain online. The local replacement must keep machine administration possible without depending on that external connector.

## Architecture

Do not duplicate the underlying machine-control implementation. Reuse:

- `heaven-bridge/` for authenticated repository-backed transport to owned machines;
- `plugins/heaven-control-plane/` for structured execution and health;
- `plugins/heaven-file-ops/` for bounded filesystem operations;
- `plugins/heaven-process-services/` for process/service lifecycle;
- `plugins/heaven-desktop/` for display/window/input/screenshot control;
- `plugins/heaven-system-ops/` for host/system administration;
- `plugins/heaven-browser/` when browser control is the actual capability needed.

This package owns compatibility, capability mapping, routing metadata, parity tracking, and any thin adapters needed to present a stable local equivalent.

## Security boundary

This replacement operates only on machines/resources the user has authorized through the existing Heaven control plane. It does not bypass Remote Desktop Commander's quota, account controls, or service authorization. It does not expose a public listener or store credentials in Git.

## Initial capability target

The first parity slice covers the operations needed by the current outage:

1. prove target-host presence separately from transport health;
2. inspect logical-disk capacity;
3. measure directory/file sizes;
4. identify top storage consumers;
5. run bounded read-only PowerShell/system probes;
6. return structured evidence to the caller.

Broader parity is tracked in `PLAN.md` and `replacement-manifest.json`.

## Validation

A replacement capability is only considered available when the local provider reports healthy/current state and the exact operation is verified on the intended host. A stale heartbeat or cached device record is not enough.

Project-level verification should be incorporated into `python .\plugins\verify.py` as the adapter gains executable code.
