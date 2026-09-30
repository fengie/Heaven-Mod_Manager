# Startup performance research — 2026-09-29

## Goal

Make Universal Mod Manager start with the smallest practical latency, CPU cost, and working set without weakening recovery, deployment, dependency, or conflict-safety guarantees.

## Evidence used

- Microsoft WPF startup guidance recommends measuring cold and warm startup separately, minimizing disk/network/external-resource work on the startup path, and postponing initialization until after the main window is rendered when that initialization is not required for the first usable UI:
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/application-startup-time
- Microsoft.Data.Sqlite documents that SQLite does not support asynchronous I/O and that its async ADO.NET methods execute synchronously. Parallel Task scheduling around independent SQLite reads therefore does not create true async I/O and can add scheduling/connection/allocation overhead:
  https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
- Sandoval Alcocer, Bergel, and Valente, *Prioritizing versions for performance regression testing: The Pharo case*, Science of Computer Programming 191 (2020), DOI 10.1016/j.scico.2020.102415, shows the value of benchmark-backed regression detection across software revisions.
- Rajan et al., *A Study on the Influence of Software and Hardware Features on Program Energy*, ESEM 2016, DOI 10.1145/2961111.2962593, found execution time, cache accesses, memory instructions, context switches, CPU migrations, and program length among commonly selected energy-relevant features across 58 desktop programs. These overlap the latency/CPU/memory goals of a lightweight desktop app.
- Mertz et al., *Satisfying Increasing Performance Requirements with Caching at the Application Level*, IEEE Software (2020), DOI 10.1109/MS.2020.3033508, and Meloca & Nunes, *A comparative study of application-level caching recommendations at the method level*, Empirical Software Engineering 27 (2022), DOI 10.1007/s10664-021-10089-z, support reuse of expensive computed results where workload evidence justifies it, while emphasizing application-specific invalidation/configuration costs.

## Changes supported by the evidence

1. Defer Nexus metadata refresh from the synchronous application bootstrap. It is optional intelligence and network-adjacent work, not required to recover state or render the initial Mods UI.
2. Generate expensive conflict preview enrichment only when the Conflicts UI is actually selected. Conflict *analysis* remains available for safety; optional artwork/preview enrichment is lazy.
3. Query only existing mod source paths during catalog discovery instead of materializing the full mod/provenance graph when the caller only needs membership testing.
4. Preserve synchronous recovery, game-build validation, automation/dependency maintenance, and deployment safety checks. Moving state-mutating safety work after first interaction would improve a stopwatch at the cost of a race-prone startup state.

## Experiments rejected

### Multicore JIT startup profiling

A trial enabled runtime profile optimization. The combined early candidate improved elapsed time but raised cold peak working set and cold CPU. Because the goal includes lightweight resource use, the JIT-profile experiment was removed before the final candidate.

### Task.WhenAll around Microsoft.Data.Sqlite reads

A trial issued independent startup database reads concurrently. Microsoft.Data.Sqlite explicitly executes async methods synchronously because SQLite has no async I/O. The pseudo-parallel read change was removed rather than retaining extra scheduling/connections without a sound I/O concurrency model.

## Measured pre-final signal

On the Heaven Windows startup gate (three cold-state and three warm-state runs, medians), the cleaned candidate at commit `369dfc334ab94387a4ee33aa7a66bfabc3e1db1a` compared with then-current main `faa1d85c4a475e87f1870377b3645367759acd4b`:

| Metric | Cold main | Cold candidate | Change | Warm main | Warm candidate | Change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Process to ready | 3556.4 ms | 2457.6 ms | -30.9% | 2361.3 ms | 1923.7 ms | -18.5% |
| Main window show | 3052.5 ms | 2064.6 ms | -32.4% | 1910.1 ms | 1543.1 ms | -19.2% |
| CPU time | 3765.6 ms | 2218.8 ms | -41.1% | 2375.0 ms | 1859.4 ms | -21.7% |
| Peak working set | 185.6 MB | 155.3 MB | -16.3% | 155.4 MB | 155.0 MB | -0.3% |
| Main-window initialization | 472.6 ms | 278.5 ms | -41.1% | 348.2 ms | 295.7 ms | -15.1% |

Package size remained 62.6 MB.

These numbers are evidence for the direction, not a substitute for exact-head verification. The final current-main replay must pass the startup comparison and exact release gate before integration.


## Follow-up: right-size WPF preview decoding — 2026-09-30

Microsoft's WPF `BitmapImage` guidance recommends setting `DecodePixelWidth` or `DecodePixelHeight` close to the rendered image size rather than decoding a large source image at full resolution. The documentation specifically notes that this can significantly reduce memory usage. This matters here because the app renders cached screenshots and thumbnails in fixed containers ranging from roughly 76 to 250 device-independent pixels while source artwork can be much larger.

Implementation:
- Preview bindings now pass bounded decode widths sized above their logical display widths to preserve DPI/headroom without decoding the full source resolution.
- `SafeImageSourceConverter` clamps requested decode widths to 64–2048 pixels and applies `BitmapImage.DecodePixelWidth` before decode.
- `BitmapCacheOption.OnLoad` and `BitmapCreateOptions.IgnoreImageCache` remain unchanged so files are fully loaded/unlocked and changed source images are not hidden behind WPF's URI cache.
- No custom process-wide bitmap cache was added. The application-level caching literature cited above supports caching when workload evidence justifies it, but also highlights invalidation/configuration cost; an unbounded strong image cache would work against the lightweight-memory goal.

Primary WPF references:
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-use-a-bitmapimage
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/how-to-use-the-image-element

This change is deliberately treated as a preview-heavy UI memory/CPU optimization rather than a claimed startup-time win: preview work is already demand-loaded off the initial startup path. A regression guard verifies that every current preview binding supplies a bounded decode width.
