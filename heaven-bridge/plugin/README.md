# Heaven Local Bridge ChatGPT Plugin

Canonical source for the private `heaven-local-bridge` plugin.

The plugin routes computer work to the local worker on `heaven` through the private GitHub relay on branch `heaven-bridge`. It is the preferred replacement for Remote Desktop Commander for routine filesystem, command, build/test, process/session, local-agent and structured desktop work.

Worker implementation lives in `heaven-bridge/worker.py`. The transport branch remains `heaven-bridge`; completed repository changes still belong on canonical `main` under `GLOBAL_GIT_DIRECTIVE.md`.

Version 0.5.0 adds an explicit code/build/test execution skill so agents do not treat the bridge as desktop-only.
