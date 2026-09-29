# Heaven Agent Control ChatGPT Plugin

This directory is the source package for the private **Heaven Agent Control** plugin.

The plugin is intentionally skills-only. It does not point ChatGPT at `127.0.0.1`, because ChatGPT cloud cannot directly reach the user's local heaven2 loopback interface.

Instead, its skill prefers the user's authorized Heaven Local Bridge with `target_host: heaven2` to:

1. locate `agentctl.mjs` on `heaven2`;
2. start the local control-plane service when necessary;
3. retrieve the control-plane snapshot;
4. write deployment tasks to a temporary file;
5. deploy agents with `--task-file`;
6. read logs;
7. launch reviewers;
8. stop explicitly selected managed workers;
9. synchronize Git branch state.

External ChatGPT/GitHub/control-machine observations may also be registered through `agentctl.mjs federation-register` / `federation-heartbeat`. Automatic ChatGPT-session enumeration is not claimed. Once the controller has an intentionally exposed authenticated Streamable HTTP MCP endpoint, this package can add a portable `mcp.json` without changing the normalized provider contract.


Operator topology is strict: heaven2 owns Agent Control and every human-facing control surface; heaven is a delegated worker/resource target and must not become the accidental dashboard or loopback host.
