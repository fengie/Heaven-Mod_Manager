# v8.8.112 - source verified, publication unverified

Canonical source: `fengie/Heaven-Mod_Manager@main` verified at `98af6dc8982102567430d0484390639e488593d0` (Windows Gate 44047541323); exact immutable release not yet verified. Bootstrap `fengie/heaven-toolbox@main` before other work.

Canonical pre-release source: `fengie/Heaven-Mod_Manager@main` at `62f3dee97e1e6ed56bcc098b50db515e4b08b624`, including #844/#846/#847/#848. This v8.8.112 metadata candidate is **not yet a source-bound immutable public release**; latest published v8.8.111 updater-main-537 targets older source `1e24b351`. Bootstrap `fengie/heaven-toolbox@main` before other work.

**Mandatory recursive continuity:** Every successor must read and inherit the full `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and active `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must propagate these Core Rules and Learned Rules to the next agent, who must require its own successor to do the same. Do not break the chain. Neither prior chat history nor an earlier read substitutes for fresh project state.

## Integrated source and exact evidence

- #844 Inbox progress and diagnostics; #846 MTP Cobertura coverage and product test inventory; #847 disposable hosted-Windows WPF UIA; #848 approved active-game manual bulk import auto-install and Mods refresh. #848 exact candidate `d177254618b8d2dc48c2bd510c01e6932f392f77` passed Windows Feature 44037120853, Security 44037120935, AutoUpdate 44037120876, hosted WPF UI 44037121012 (4/4). Merged in main `62f3dee97e1e6ed56bcc098b50db515e4b08b624`. Large real RAR/7z and installed-device proof remain unresolved.

- #832 planner allocation and overlay name/token caching: source candidate `5cf175d871fe51ac1895378c919c8d2d1d14784c` passed [Windows Feature 43978338548](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43978338548) and AutoUpdate 43978338567, integrated main `550d74b62c9bbc3cef8922a4e019cf5239d90bde`. Synthetic benchmarks do not measure installed-client p95.
- #835 SQLite 11.0.0 and SharpCompress 0.50.9 updates, third-party notices and missing native dependency-CI triggers: candidate `2974738085def5dc4183d7c7ad055b6545922c14` passed Windows Feature 43979469737, Product Security 43979469676, Updater Publication 43979469883, AutoUpdate 43979469626; integrated main `b9acd9b2fca75ccf5eda7627f9b10b88cc93ec56`.
- #836 deterministic bulk ZIP fault, family order-invariance and optional replay/topology benchmark sources merged at `main@8ed0a73ca4a320c49c579b8620a3b1869595d2b3` after exact-head Windows Feature 43982862135 and AutoUpdate 43982863098 passed on candidate `9cd082d94a9fcd9dc8f3993085d380ce7ad66b2c`. Actual user RAR/7z and installed WPF/rollback remain unverified.
- #838 concurrent mod-import receipt commit serialization, durable hash re-read/merge and regression test merged at `main@285ba72396ce942862842c89818cfce90ff64c56`; exact head `bbf8b17f3d6c4869016ad623df48cca3758083f5` passed Windows Feature 43985886173 and AutoUpdate 43985886847. No real installed concurrent-user scenario was observed.
- #839 `GenericFamilyInference` safe strong-evidence early pruning integrated at `main@fefc78110f69e5d3532d6fd424d0e11253adf09c`. Exact PR head `176116f8e1928f6712fd2130888dd14bd61046fc` passed [Feature 43989850199](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43989850199) and AutoUpdate 43989850215; [main gate 43993837451](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993837451) and AutoUpdate 43993837449 passed on merge. 500 shared-root unrelated mods: 281.974 → 223.461 ms / 507.76 → 420.2 MB allocated, three synthetic iterations; not installed UI p95 or real archive corpus.
- Prior immutable [v8.8.110 updater-main-535](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-535) targets `968d9e7d77a7d5872fb94a63eea0d9819586a0b8`, published after 26/26 native [main gate 43989713722](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43989713722); it excludes later #839. Preserve this immutable artifact unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.112 source `98af6dc8982102567430d0484390639e488593d0` passed run `44047541323` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.112-heaven-windows-closure.log`.

Public release status: **NOT VERIFIED** for source `98af6dc8982102567430d0484390639e488593d0`. Successful build or another source's same-version release is not proof of publication.

The tested source remains `98af6dc8982102567430d0484390639e488593d0` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and acceptance

- **Unresolved risk:** [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) still requires actual large RAR/solid RAR5/7z mod packs, deeply nested mixed-game collections and installed WPF Explorer drag/drop/progress/cancel. ZIP-only synthetic tests do not close it.
- [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793) attempted real packaged updater [43993780193](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43993780193) (two safety tests passed, positive update aborted before applying due to occupied shared root), and requires dedicated, least-privileged Windows account/profile with an independently empty test-owned LocalApplicationData updater root, positive packaged update + rollback evidence. Never clear or reuse the shared occupied real updater state. E2E classification success with skipped positive job is not proof.
- [#817](https://github.com/fengie/Heaven-Mod_Manager/issues/817) tracks accurate post-release continuity and artifact proof. Distinguish source verified, artifact published and physically installed.

Preserve security, privacy, publisher ownership, rollback and source-bound verification. Every successor must recursively propagate the continuity protocol and active Learned Rules to the next agent again. Do not break the chain.
