# v8.8.65 header selector selected-text repair — integrated source handoff

Canonical MHW product repository: `fengie/mhw-mods`
Global reusable toolbox/training authority: `fengie/heaven-toolbox@main`
Current branch: `main`
Verified PR head: `236d3604f1df245f9c224eda3f21f2177979cfd2`
Merge commit: `c3f238cbe4f1850763736bac999af0577a4606c5`
Issue: #556 remains open for installed-client visual acceptance

## Integrated change

Installed UI evidence showed that v8.8.64 templated dropdown rows but the closed header selector could still render raw `GameProfile { ... }` text.

v8.8.65 fixes the actual shared ComboBox selected-content path:

- the app-owned ComboBox template keeps `SelectionBoxItem` as the selected content;
- the closed presenter reuses `ItemTemplate`, `ItemTemplateSelector`, and `ItemStringFormat`;
- the header's existing `DisplayName` template therefore applies to both dropdown rows and the selected game;
- focused regression coverage rejects the broken `SelectionBoxItemTemplate` fallback.

## Verification state

Exact PR #562 head `236d3604f1df245f9c224eda3f21f2177979cfd2` passed all required gates:

- Workflow Feature PR Gate run `36963458484`;
- MHW Product Security Gate run `36963458384`;
- Heaven Toolbox Ownership Gate run `36963458431`.

The verified head merged to canonical `main` as `c3f238cbe4f1850763736bac999af0577a4606c5`. Remote-main readback confirms the selected presenter uses `ItemTemplate`/`ItemTemplateSelector`/`ItemStringFormat`, no longer uses `SelectionBoxItemTemplate`, and the product version is 8.8.65.

The first Workflow Feature attempt `36963151154` is preserved as useful evidence: all builds/tests passed, but the handoff preflight correctly rejected missing explicit unresolved-risk wording. That metadata failure was repaired before the final exact-head run.

## Unresolved risks and remaining work

- **Unresolved risk:** installed Windows/WPF visual acceptance is still missing for the closed selector. Source/build/test evidence does not substitute for confirming the installed app actually shows a human-readable selected game such as `Monster Hunter: World`.
- Issue #556 must remain open until that installed-client acceptance is recorded.
- Live remote-thumbnail acceptance from v8.8.64 remains a separate UI evidence gap.
- #558/#559 remain the next catalog breadth/discovery lanes and must not regress the selector contract.
- The local Heaven/Agent Control dispatch route was not exposed in the chat session that integrated this source fix; do not weaken the signed Heaven Bridge/HMAC boundary to work around unavailable routing.

### Ordered continuation

1. Install/run a build containing v8.8.65 on Windows and verify the closed header selector shows `GameProfile.DisplayName`, then open the dropdown and switch games to check both surfaces.
2. Record the installed-client evidence and close #556 only if the visual/interaction check passes.
3. If it still fails, attach the new screenshot plus `MHW-DEBUG-ALL.log` and treat that as a new runtime reproduction rather than reopening the already-correct source contract blindly.
4. Continue #558/#559 after #556 acceptance without weakening provider/acquisition safety.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` in full, refresh live ownership/state before mutation, preserve the continuity constitution and active learned rules, and recursively propagate this obligation. **Do not break the chain.**

The successor must propagate this continuity obligation to the next agent.
