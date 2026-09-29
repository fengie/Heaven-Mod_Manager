# Heaven Local Bridge ChatGPT Plugin

Canonical source for the private `heaven-local-bridge` plugin.

The plugin routes host-targeted computer work to `heaven2` or `heaven` through the private GitHub relay on branch `heaven-bridge`. `heaven2` is the default human-facing desktop/control-plane target; `heaven` is the heavy worker/resource target. It is the preferred replacement for Remote Desktop Commander for routine filesystem, command, build/test, process/session, local-agent and structured desktop work.

Worker implementation lives in `heaven-bridge/worker.py`. The transport branch remains `heaven-bridge`; completed repository changes still belong on canonical `main` under `GLOBAL_GIT_DIRECTIVE.md`.

Version 0.8.0 adds explicit multi-host `target_host` routing and host-scoped heartbeats. New interactive/control jobs target `heaven2`; omitted `target_host` remains a legacy compatibility default for `heaven` only.
