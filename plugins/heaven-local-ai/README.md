# Heaven Local AI

Local model offload for cheap summarization/compression and bounded generation through Ollama on Heaven.

## Capabilities

- `local_ai.models` — fixed `ollama list` health/inventory call.
- `local_ai.generate` — run a validated model with a bounded prompt.
- `local_ai.summarize` — engineering-focused context compression preserving errors, paths, decisions, blockers, and next actions.

Prompts are UTF-8/base64 encoded before entering PowerShell so prompt text is never interpolated as shell syntax. Model names are restricted to a conservative character set.

## Security boundary

This plugin is not a secret broker. Do not send credentials in prompts. It reuses `heaven-control-plane` `execution.run` and inherits its permission, timeout, audit, and transport rules.

## Validate

```powershell
python .\plugins\heaven-local-ai\verify.py
```
