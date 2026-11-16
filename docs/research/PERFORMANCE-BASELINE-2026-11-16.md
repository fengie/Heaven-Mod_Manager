# Synthetic performance baseline — November 16, 2026

## Verified execution

- Repository: `fengie/Heaven-Mod_Manager`, source `main@1b01f41759a84270f9622840cc055524d17c2d50` plus an isolated branch-scoped one-shot workflow commit `9e4543682890cb67f4d8340f88c15914139afee2`; production source is identical to that main revision.
- [Windows BenchmarkDotNet ShortRun #43966858870](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43966858870), job `133648463185`, **SUCCESS**. Ran all 26 benchmark cases.
- Physical runner: `heaven-v2` (`HEAVEN`), AMD Ryzen 9 7945HX, Windows 11, .NET SDK 10.0.401, .NET runtime 10.0.15 x64.
- Each case: one launch, **three warmup and three measured iterations**. Means are useful for first-pass prioritization, *not* production p95 or high-confidence cross-machine comparisons.
- The optional JSON artifact upload timed out (`ETIMEDOUT`); complete summary tables remain in the successful job's logs. No measurements are fabricated from missing artifacts.

## Measured results (baseline)

Times below are BenchmarkDotNet arithmetic means. Allocated is **cumulative managed bytes per operation**, not peak process RAM.

| Area | Workload | Mean time | Managed allocated |
| --- | --- | ---: | ---: |
| Deployment planner | 200 mods, 50k files | 570.9 ms | 973.04 MB |
| Deployment planner | 500 mods, 150k files | **4,181.4 ms** | **5,804.06 MB** |
| Family inference | 100 mods | 58.60 ms | 98.77 MB |
| Family inference | 250 mods | 134.48 ms | 246.73 MB |
| Family inference | 500 mods | 285.69 ms | 493.53 MB |
| Bulk archive routing | 8 ZIPs × 4 KiB | 115.3 ms | 2.84 MB |
| Bulk archive routing | 8 ZIPs × 1 MiB | 218.8 ms | 3.34 MB |
| Bulk archive routing | 64 ZIPs × 4 KiB | 738.3 ms | 22.8 MB |
| Bulk archive routing | 64 ZIPs × 1 MiB | 1,196.7 ms | 26.74 MB |
| Archive inspect | 32 entries | 0.3864 ms | 187.59 KiB |
| Archive inspect | 512 entries | 1.7628 ms | 1,804.17 KiB |
| Archive extract+cleanup | 32 entries | 42.65 ms | 4,646.39 KiB |
| Archive extract+cleanup | 512 entries | 559.19 ms | 72,967.2 KiB |
| Fast XXH3 hash | 1 MiB | 0.4111 ms | 3.13 KiB |
| Fast XXH3 hash | 8 MiB | 2.0813 ms | 3.13 KiB |
| Authoritative hash (two-pass) | 1 MiB | 2.0566 ms | 261.23 KiB |
| Authoritative hash (two-pass) | 8 MiB | 12.2887 ms | 261.21 KiB |
| Game classifier | 100 paths | 0.1356 ms | 459.36 KiB |
| Game classifier | 1k paths | 1.2576 ms | 4,495.3 KiB |
| Game classifier | 10k paths | 19.5483 ms | 44,854.67 KiB |
| Mod boundary discovery | 100 paths | 0.09217 ms | 215.69 KiB |
| Mod boundary discovery | 1k paths | 1.18685 ms | 1,992.99 KiB |
| Mod boundary discovery | 10k paths | 15.91155 ms | 20,244.4 KiB |
| SQLite mod-row read | 100 mods | 0.2554 ms | 60.95 KiB |
| SQLite mod-row read | 1k mods | 2.6796 ms | 567.2 KiB |
| Master method trace | per scope | 106.8 ns | 304 B |

## Evidence-based priority

1. **P0 — Planner dense shared-file overlaps:** 500-mod/150k-file planning costs 4.18 seconds and allocates 5.8 GB. The 500-mod case is ~7.3× slower and ~6× more allocation than the 200-mod/50k-file case despite only 3× as many files. This is an important responsiveness and memory-allocation target. The benchmark intentionally stresses 100 paths with many shared providers.
2. **P1 — Family inference allocations:** 500 mods allocate ~493 MB over 286 ms. This needs profiling after planner, not speculative caches.
3. **P1 — High-entry-count extraction:** 512 entries take ~559 ms; inspection alone takes ~1.76 ms. The measured difference is file creation/copy/cleanup overhead, not merely archive-header parsing.
4. **P2 — Bulk import:** 64 synthetic ZIPs with 1 MiB each take ~1.20 seconds, roughly 53.5 MiB of source payload per second; 64 tiny ZIPs take ~0.74 seconds. Observed scaling from 8 to 64 ZIPs is near-linear. Rewriting the sorted duplicate-receipt JSON per accepted package remains a source-level scalability risk, **not** an established dominant bottleneck at this size.
5. **Good enough initially:** 1,000 mod-row reads ~2.68 ms; 10k-path discovery ~15.9 ms; ZIP inspection ~1.76 ms for 512 files; fast hashing ~2.08 ms for 8 MiB. Allocation in game classification (45 MB for 10k paths) deserves later work but its current wall time is modest.

## Investigation and fix hypothesis

The planner counts every shared provider pair per path. On the dense 500-provider/100-shared-path fixture, that is about **12.5 million** pair observations. It repeatedly calls `ConflictEngine.PairKey`, which lowercases both IDs for each observation. A source candidate pre-normalizes one canonical ID per enabled mod, then constructs already ordered tuple keys in the inner loop. This is a narrowly scoped, semantics-preserving allocation hypothesis; **it is not a verified speedup until the same-runner post-change benchmark and full regression gates pass**.

Keep high-confidence explicit rules and fail-closed conflict behavior unchanged. Do not infer an automatic winner from priority alone.

## Measured candidate comparison — canonical pair IDs

The first candidate reused one lowercase canonical string per enabled mod rather than calling `ConflictEngine.PairKey` inside every dense shared-file pair. [A/B run 43969062226](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43969062226), job `133656081998`, finished **success** on the same `heaven-v2` Ryzen 9 7945HX runner and same BenchmarkDotNet ShortRun settings. Candidate source was `5208e1475a5236343fb70bc6577887235fdb8816` plus an isolated workflow-only commit `65692845af53d1adf48aee8df1afacd4a83347e9`.

| Scenario | Baseline mean | Candidate mean | Time reduction | Allocated baseline → candidate | Allocation reduction |
| --- | ---: | ---: | ---: | ---: | ---: |
| 200 mods / 50k files | 570.9 ms | 322.0 ms | **43.6%** | 973.04 → 259.47 MB | **73.3%** |
| 500 mods / 150k files | 4,181.4 ms | 2,971.7 ms | **28.9%** | 5,804.06 → 1,339.35 MB | **76.9%** |

This confirms a large transient-allocation problem, but a 3-second plan and 1.34 GB allocated remain concerning. Confidence is limited by three measured iterations; the extreme confidence-interval margins mean this is a directional result, not a stable user-facing p95. A follow-up local-per-plan name/token memoization pass was subsequently measured on the same runner, as recorded below. Exact-head source/CI and integration remain independent obligations.


## Measured follow-up — per-plan overlay names and tokens

The additional candidate computes cleaned mod names and token arrays once per planning pass, then reuses them across repeated pairwise high-confidence overlay inference. This is a per-invocation cache only: no cross-snapshot or persistent state can become stale. A previous Windows job [43970907686](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43970907686) **failed before any timing** because strict .NET analyzer CA1859 required concrete `Dictionary` types for two private parameters; no optimizer slowdown or behavior failure was measured. The two private parameter types were corrected at commit `a62e43103fe01ba11f9b5496548066ab4f89457c`, preserving the public API and inference policy.

The corrected code was measured in successful [Windows ShortRun 43973847746](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43973847746), source `a072b8baa4132d2fce30d32f94a11d39535f62c5`, job `133672632427`. Physical runner: `heaven-v2` / AMD Ryzen 9 7945HX, .NET 10; same machine and benchmark fixture as baseline and first optimization. All benchmark entries below were from **three measured iterations**, so these are directional mean comparisons, not end-user p95 or robust statistical bounds.

| Scenario | Baseline mean | Pair-ID cache only | Pair-ID + name/token cache | Baseline allocation | Final allocation | Reduction vs baseline |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 200 mods / 50k files | 570.9 ms | 322.0 ms | **205.1 ms** | 973.04 MB | **186.52 MB** | **64.1% time / 80.8% allocation** |
| 500 mods / 150k files | 4,181.4 ms | 2,971.7 ms | **1,589.6 ms** | 5,804.06 MB | **880.55 MB** | **62.0% time / 84.8% allocation** |

The second pass improved the 500-mod fixture by another **46.5% time / 34.3% allocations** relative to pair-ID caching alone, but it still allocates approximately 881 MB per synthetic plan. Preserve fail-closed winner and explicit-rule logic and investigate remaining dense-pair costs separately. No live installed client benchmark, large real RAR/7z performance, or throughput forecast is implied by these measurements. The temporary branch-specific workflow was retired after dispatch; the permanent opt-in benchmark facility remains on canonical main.

Official analyzer reference: [CA1859](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1859). BenchmarkDotNet measurements follow its [isolated-process and warmup model](https://benchmarkdotnet.org/articles/guides/how-it-works.html).

## Missing evidence

- Real compressed RAR/7z (including solid/encrypted/corrupt variants), real hundreds-mod Sifu/Stellar Blade corpus, slow network/external disks, and duplicate replay after physical installation
- End-user WPF input responsiveness, single-import per-phase timing, Nexus metadata fan-out and actual disk I/O counters
- Repeatable 10+ sample distributions, p95 tails, matched baseline/candidate on the same runner and installed Heaven2 health

No synthetic result establishes that the observed user's original single-RAR import is fast. Continue #791 for real archive acceptance, #735 for installed UI latency/Auto Populate and #793 for isolated updater execution. As documented by Microsoft, `FileOptions.WriteThrough` requests prompt persistence to disk and `Microsoft.Data.Sqlite` async methods execute synchronously; neither protection is a safe speculative removal solely to improve a benchmark.

Sources: [FileOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileoptions), [Windows file caching](https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching), [Microsoft.Data.Sqlite async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).
