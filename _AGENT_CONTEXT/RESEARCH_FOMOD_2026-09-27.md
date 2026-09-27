# Research checkpoint 1: FOMOD and Inbox

Researched 2026-09-27 against workflow branch production source (unchanged since the validated implementation). This is a source audit and proposed test plan, not proof that new scenarios pass. Read SESSION_HANDOFF_2026-09-27.md for existing implementation and verification. No production code changed in this research checkpoint.

## Most actionable findings

| ID | Evidence / status | Next action |
|---|---|---|
| INBOX-1 | Source-confirmed: SmartInboxService creates the destination directly in ModsRoot. Its IOException/UnauthorizedAccessException/InvalidDataException handler records failure without cleaning the destination. Cancellation bypasses that handler. | Reproduce cancellation after one copied file and extraction failure after a partial write; verify whether a later catalog scan recognizes the incomplete folder. |
| INBOX-2 | Source-confirmed: success is added to results before MoveToProcessed. If moving fails, the catch adds a second failure result for the same source. | Lock or deny the processed destination; assert one truthful result, retained payload, and idempotent retry. |
| FOMOD-1 | Source-confirmed divergence from MO2: our omitted folder destination becomes empty; MO2 readFileList defaults an omitted destination to the source for files and folders. | Compare a synthetic folder source='textures' with omitted destination against explicit destination='' and a known installer. Decide/document semantics before changing. |
| FOMOD-2 | Our equal-priority different-source destination collisions throw. MO2 byPriority orders equal priorities by original file sequence. | Treat as compatibility policy to resolve, not a security bug. Test selected optional overlays with implicit priority zero; determine intended overwrite behavior using full MO2 install path/schema. |
| FOMOD-3 | Our Plan skips hidden steps before inspecting alwaysInstall/installIfUsable. | Hidden-step semantics remain unresolved; create a false-visible step with alwaysInstall and compare established installers. Do not claim a schema violation yet. |
| FOMOD-4 | Describe evaluates sibling plugin types using flags from earlier steps; selected flags are applied after the step. | Test same-step flag dependency, back navigation, and a selected option becoming hidden. Clarify intended state model before supporting additional semantics. |
| FOMOD-5 | Plan checks exact destination collisions but does not explicitly reject a destination that is a file ancestor of another destination. | Plan two outputs 'x' and 'x/y.tex'; require an actionable preflight error before any copy. Existing InstallAsync catches failure and deletes its newly created destination. |

## Primary implementation comparison

Inspected ModOrganizer2/modorganizer-installer_fomod master tree
`24f07cf7af5273efe52daa90c631a49e7e847c74`, source blob
`ddfc1a59d22e01ff65936d6639dae2d5c621180a`:
https://github.com/ModOrganizer2/modorganizer-installer_fomod/blob/master/src/fomodinstallerdialog.cpp

Reproducible blob endpoint:
https://api.github.com/repos/ModOrganizer2/modorganizer-installer_fomod/git/blobs/ddfc1a59d22e01ff65936d6639dae2d5c621180a

Relevant symbols: readFileList (destination default, priority, XML sequence, installIfUsable and alwaysInstall); byPriority (priority, then XML sequence). This is an implementation reference, not a universal normative specification. Full hidden-page behavior was not established by this comparison. Do not infer it from attribute parsing alone.

A community documentation index was located at https://fomod-docs.readthedocs.io/en/latest/specs.html; its linked schema could not be retrieved through the attempted browser link. Treat it as a discovery aid, not authoritative confirmation of unresolved behavior. Next agent should acquire a versioned schema and record its origin/license before checking in fixtures derived from it.

## Synthetic fixture matrix

Use small original XML and arbitrary text payloads, avoiding redistribution of downloaded mod archives. Add tests to AutomationTests/WorkflowTests.cs or a focused installer test file after examining current coverage.

1. Each group cardinality: ExactlyOne, AtMostOne, AtLeastOne, Any, All; zero/one/multiple choices; Required combined with NotUsable; Recommended default behavior explicitly documented.
2. Flag transitions: earlier page selection toggles later visibility; flags unset on deselection; competing assignments; unknown flag; nested And/Or; unsupported external dependencies must fail closed even inside an otherwise true Or.
3. Copy semantics: file rename; folder merge; omitted versus empty destination; priority ties; higher-priority override; source duplicates; case-only destination collision; file/directory ancestor conflict.
4. XML: UTF-8 BOM, UTF-16 BOM, valid declared encoding, malformed XML, DTD/entity rejection, oversized config, multiple ModuleConfig.xml files, wrapper directory. Do not assume a third-party bug report reproduces here.
5. Paths on Windows: traversal using both separators, rooted/UNC/drive-relative paths, alternate streams, reserved device names, trailing dots/spaces, case normalization, symlink/junction ancestors. Determine what existing PathRules already rejects; do not weaken safety for compatibility.
6. Cancellation/failure: copy one file then cancel; locked destination; insufficient space simulation where available; cleanup failure preserves original failure details; pre-existing destination remains untouched.
7. Saved selections: same installer reopens correctly; reordered steps/options or updated config cannot silently bind old positional identifiers to new choices. Audit how selection identity is persisted before promising cross-version migration.
8. End-to-end: archive quarantine -> chooser -> exact selected payload -> catalog capture -> plan -> apply -> undo. Cancel chooser must leave source archive and live deployment intact.

## Suggested Inbox repair design (not implemented)

Extract/copy into an owned quarantine directory outside the catalog scan root. Detect FOMOD before publication; preserve original Inbox source for interactive handling. Publish only after validation, then report import success once. Moving the original into processed storage is a separate outcome: if it fails after publication, return an imported-with-warning result and prevent duplicate ingestion on retry. Use narrowly scoped cleanup of owned staging directories; never delete a pre-existing user folder. Design cancellation cleanup explicitly. Review Directory.EnumerateDirectories(...AllDirectories) reparse-point behavior in CopyDirectoryAsync; this audit has not established a concrete link-escape exploit.

## Completion and next agent obligation

Research only; no new regression was executed. Existing 179 tests and Windows CI evidence retain their original scope. First implementation task should reproduce INBOX-1/2 and add a focused regression while fixing it. Preserve this distinction between observed control flow, compatibility questions, and executed evidence. Push the fix and updated handoff at that checkpoint; tell your successor to do the same.
