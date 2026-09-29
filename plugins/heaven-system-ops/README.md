# Heaven System Ops

One clean boundary for host/environment tooling that depends on optional machine software: Docker, WSL, Hyper-V, package managers, DNS, Tailscale, SSH, and SCP.

`system.availability` reports which host tools are actually installed. Read operations remain usable independently; mutations and arbitrary WSL/SSH execution require explicit confirmation. User-controlled host/package/remote-command values are passed through validated environment/argument arrays rather than concatenated into shell syntax.

Validate with `python .\plugins\heaven-system-ops\verify.py`.
