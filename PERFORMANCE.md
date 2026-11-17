# Resource budget and observed results

The live measurements below were recorded against version 0.1. Version 0.2 adds a logo, drag/keyboard editing, installed-mode settings storage, and four-edge physical routing. The background architecture still has no recurring application timer or UI framework, but the old movement measurements must not be treated as a new benchmark of the extended engine. Packaging/design tooling and the SDK validation helpers are not installed with the program.

The goal is a background engine that is effectively idle when there is no input, uses single-digit MiB of working memory, and stays responsive during continuous mouse movement. The fresh native process met the initial memory target, but its working set after interactive use is about 12 MiB and exceeds that target. Private memory remains under 2 MiB. Long-run stability, latency and hardware-specific polling-rate behavior remain to be measured.

## Live movement and idle comparison

Recorded on the existing user-launched engine (PID 5404), with its real saved physical layout, on 2026-11-17 UTC. The user was asked to move continuously and cross screens during the movement phase, then leave the mouse still for the idle phase. No cursor input was generated, no memory was trimmed, and the engine was not restarted, paused or reconfigured by the observer.

| Metric | Movement / crossings | Following idle phase |
|---|---:|---:|
| Duration | 30.007 s | 30.01 s |
| Process CPU-time increment | 125.000 ms | 0.000 ms reported |
| Average CPU, total of 24 logical processors | 0.01736% | 0.00000% reported |
| Average CPU, relative to one logical processor | 0.41657% | 0.00000% reported |
| Working set | 11.88 MiB | 11.83-11.88 MiB |
| Private bytes | 1.91 MiB | 1.79-1.91 MiB |
| Process cycle increment | 1,489,073,404 | 111,571,693 |

125 ms over 30 seconds is about 4.17 ms of process CPU time per second. The raw process-cycle counts show that the idle phase was not literally free of execution despite the reported zero CPU-time increment. CPU time has accounting granularity. Cycles are not converted to milliseconds because clock frequency and scheduling vary.

The observer could read process counters but could not see the user app's hidden control window from its restricted desktop. Engine-state and mouse-callback fields are therefore **unavailable**, not zero. The movement phase is user-assisted; it is not a verified 1,000/8,000 Hz workload. These measurements do not capture all system-wide hook delivery costs or establish input latency. They do not prove crossings occurred at any particular frequency or that their alignment was correct.

Raw reports (each with a matching per-second CSV):

- `artifacts\live-benchmarks\20261117-032648.json` - movement/crossings.
- `artifacts\live-benchmarks\20261117-032737.json` - following idle phase.

Running engine SHA-256: `F630DBF022CA1EEEDF05721D722D255841C3DBA4EBE832CC394326C70EC48517`.

Run `bin\SeamlessMice.Observe.exe 30 label` to repeat. The read-only observer runs outside the measured process. The initial recorder checks failed on desktop visibility/default process-handle access; the final recorder uses `PROCESS_QUERY_LIMITED_INFORMATION` and records unavailable fields explicitly. Only completed samples above are used for this comparison.

## Native engine observation

Built with MSVC from Visual Studio Build Tools 2026, optimized x64 with the C runtime statically linked. Measured with the real low-level mouse hook installed and the settings editor closed. No memory trimming or synthetic mouse input was used.

| Metric | Observed |
|---|---:|
| Duration | 15.01 s |
| Mouse-move callbacks during observation | 0 |
| Reported process CPU-time increment | 0.000 ms |
| Working set | 8.71 MiB |
| Private bytes | 1.61 MiB |
| Handles | 120 |
| Threads | 4 |
| Logical processors | 24 |
| Windows Forms loaded in engine | No |

Compared with the initial managed observation below, the native process used about 54% less working memory and 85% fewer private bytes. This is a comparison between this project's two engines; it is not a benchmark against LittleBigMouse or physmouse. Zero reported CPU time is limited by OS accounting resolution and does not establish cost during movement.

The raw native observation is recorded in `artifacts\native-validation\metrics.txt`. The background engine is a roughly 158 KiB executable. Only the separate editor requires the .NET Framework already included in Windows 11.

## Managed baseline observation

Measured with the real low-level mouse hook installed, settings editor closed, two attached displays, and no synthetic input. This is a short observation on one machine, not a stability or high polling rate benchmark.

| Metric | Observed |
|---|---:|
| Duration | 15.01 s |
| Mouse-move callbacks during observation | 0 |
| Reported process CPU-time increment | 0.000 ms |
| Working set | 19.07 MiB |
| Private bytes | 10.54 MiB |
| Handles | 227 |
| Threads | 9 |
| Logical processors | 24 |
| Windows Forms loaded in engine | No |

Zero reported CPU time is bounded by the OS accounting resolution. It does not mean the cost is mathematically zero. These figures **do not establish CPU use during movement**. Working set and private bytes measure different things; both are included, and neither was artificially reduced by trimming memory.

The C# baseline misses the memory goal. The native engine removes the CLR from the persistent process.

## Managed geometry microbenchmark

Initial optimized x64 run, 10,000,000 iterations each:

- Interior geometry: approximately 10.5 ns/event; zero Gen0 collections during the loop.
- Seam geometry: approximately 23.3 ns/event; zero Gen0 collections during the loop.

This measures the pure coordinate calculation only. It excludes Win32 hook delivery, context switching, `CallNextHookEx`, clipping/full-screen checks, and `SetCursorPos`. It must not be extrapolated into an end-to-end CPU or input-latency claim. Zero collections alone is not proof that every code path allocates nothing; the callback and geometry were also inspected to avoid allocation on ordinary mouse movement.

## Architecture budget

- Block in `GetMessage` when no event is available.
- No recurring application timer, display polling, foreground-window polling, or file watcher.
- One debounced, one-shot timer following display/resume notifications; cancel it after use.
- Keep the settings editor in a separate process that exits when closed.
- Keep display queries, configuration IO, process startup, and UI rendering outside the installed hook.
- Precompute panel scales when loading a layout.
- Use direct struct access for mouse events; no boxing, formatting, logging, allocations, or locks on ordinary movement.
- Only check bypass modifiers, clipping, and full-screen state at internal screen seams.
- Honor injected input and cursor clipping.

## Remaining measurements

1. Record full-engine CPU time, callback count, and latency during real 125/1,000/8,000 Hz movement, including movement along seams.
2. Validate crossings and window dragging in both directions on the actual desk layout.
3. Exercise real unplug/replug, sleep/wake, lock/unlock, full-screen games, pointer clipping, and elevated applications.
4. Run an extended session to assess stability and hook loss.

The native geometry executable passes 8,611 assertions. The common geometry/configuration and real-engine lifecycle harness passes 8,654 assertions. Simulated lifecycle notifications pass, but do not validate actual hardware transitions. Native geometry microbenchmarks exclude Win32 hook delivery and must not be used as an end-to-end CPU or latency claim.

Measurement contracts: [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes), [QueryProcessCycleTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryprocesscycletime).
