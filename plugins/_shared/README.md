# Shared Plugin Modules

This directory contains reusable implementation modules that are consumed by multiple Heaven plugins. It is not a second runtime or credential store.

## Security module

`heaven_security.py` provides:

- opaque secret-handle validation and short-lived purpose-scoped binding resolution;
- a directory abstraction that stores only non-secret host transport references, never credential values;
- fail-closed capability permission brokering with exact, namespace-wildcard, and trusted-local wildcard grants;
- exact manifest-to-registry permission declaration validation.

Actual secrets remain in the OS/user-authorized backing store or connector. The shared module returns only an ephemeral host-side reference suitable for a trusted transport and redacts that transport reference from its serializable binding representation.

Validate with:

```powershell
python .\plugins\_shared\verify.py
```
