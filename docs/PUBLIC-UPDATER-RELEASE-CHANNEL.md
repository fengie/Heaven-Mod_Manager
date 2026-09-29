# Public updater release channel

## Goal

Keep the private development repository (`fengie/mhw-mods`) private while allowing packaged clients to update without a GitHub login or Windows Credential Manager token.

## Public release repository

The updater's public feed is:

`fengie/mhw-mod-manager-releases`

That repository is release-only. It should contain no source tree, Heaven runner configuration, agent context, debug logs, private CI material, or development secrets.

Each immutable updater release must use the existing tag convention:

`updater-main-<buildNumber>`

and contain exactly the verified package plus:

- the packaged ZIP named by `update-manifest.json`;
- `update-manifest.json`.

The manifest remains the authority for build number, source SHA, package size, SHA-256, product-manifest SHA-256, executable path, and minimum updater protocol.

## Client behavior

Packaged clients:

1. query the public release-only repository anonymously;
2. select only immutable, non-draft, non-prerelease releases newer than the installed build;
3. verify the manifest and package exactly as before;
4. download public assets without an Authorization header;
5. fall back to the private `fengie/mhw-mods` release feed only when a Windows Credential Manager token is already configured.

Development/unmanaged installs remain unable to self-update.

## Publication boundary

The private Windows Release Gate remains the only builder/verifier. Publication to the public repository must happen only after the private gate has produced and verified the same immutable package and manifest. Cross-repository publication must use a narrowly scoped credential that can write releases to `fengie/mhw-mod-manager-releases` and nothing broader.

Do not run untrusted public pull-request code on the Heaven self-hosted runner.

## Migration

Existing private updater releases stay valid for authenticated fallback. Once the public release repository exists and contains a mirrored immutable release, newly packaged clients no longer require GitHub credentials for ordinary updates.
