# v8.8.81 canonical update-migration mod identity — canonical state

v8.8.80 source `e7af34331b608fca3d115d2dfe20f5c8e69a5973` remains the last closed hosted-Windows verification boundary. v8.8.81 fixes inconsistent case-sensitive mod identity inside update migration.

## Behavior

- Requested old/replacement IDs resolve case-insensitively and canonicalize to stored IDs.
- Rule, metadata, family, resource-provider, staged-state, supersession, and diff migration use canonical logical identities.
- Old/new IDs differing only by case remain invalid as the same package.

## Verification boundary

Fresh exact-input Windows verification is required for v8.8.81. Do not inherit v8.8.80 green status.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, and case-insensitive mod identity, and recursively propagate the same obligation.
