using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace SeamlessMice
{
    // Read-only observer. It never pauses, restarts, reconfigures or injects input into the engine.
    internal static class Observe
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string name, string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryProcessCycleTime(SafeProcessHandle process, out ulong cycles);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
        private static uint Query(IntPtr window, int what)
        {
            UIntPtr result;
            if (SendMessageTimeoutW(window, 0x8008, new IntPtr(what), IntPtr.Zero, 2, 2000, out result) == IntPtr.Zero)
                throw new Exception("The engine did not answer the read-only query.");
            return unchecked((uint)result.ToUInt64());
        }
        private static int Main(string[] args)
        {
            try
            {
                int seconds = args.Length > 0 ? Int32.Parse(args[0], CultureInfo.InvariantCulture) : 30;
                string label = args.Length > 1 ? args[1] : "normal-use";
                if (seconds < 5 || seconds > 300) throw new ArgumentException("Duration must be 5 to 300 seconds.");
                IntPtr window = FindWindowW("SeamlessMice.Engine.v1", null);
                uint processId;
                bool controlVisible = window != IntPtr.Zero;
                if (controlVisible) GetWindowThreadProcessId(window, out processId);
                else
                {
                    Process[] candidates = Process.GetProcessesByName("SeamlessMice");
                    if (candidates.Length != 1) throw new Exception("Expected exactly one running Seamless Mice process.");
                    processId = (uint)candidates[0].Id;
                    candidates[0].Dispose();
                }
                using (Process process = Process.GetProcessById((int)processId))
                using (SafeProcessHandle queryHandle = OpenProcess(0x1000, false, processId))
                {
                    if (queryHandle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot open a read-only process handle.");
                    var executablePath = new StringBuilder(32768); uint pathLength = 32768;
                    string executable = QueryFullProcessImageNameW(queryHandle, 0, executablePath, ref pathLength) ? executablePath.ToString() : "SeamlessMice.exe (path unavailable)";
                    uint? initialState = controlVisible ? (uint?)Query(window, 0) : null;
                    if (initialState.HasValue && initialState.Value != 1) throw new Exception("Correction is not active. Resume it before benchmarking.");
                    string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "live-benchmarks"));
                    Directory.CreateDirectory(directory);
                    string prefix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    process.Refresh();
                    double startCpu = process.TotalProcessorTime.TotalMilliseconds;
                    uint startMoves = controlVisible ? Query(window, 1) : 0, previousMoves = startMoves;
                    ulong startCycles;
                    bool cyclesAvailable = QueryProcessCycleTime(queryHandle, out startCycles);
                    long firstPrivate = process.PrivateMemorySize64, firstWorking = process.WorkingSet64;
                    long minPrivate = firstPrivate, maxPrivate = firstPrivate, minWorking = firstWorking, maxWorking = firstWorking;
                    double previousCpu = startCpu, previousTime = 0;
                    int inactiveSamples = 0;
                    var samples = new List<object>();
                    var csv = new StringBuilder("elapsed_s,cpu_ms_delta,cpu_pct_one_core,cpu_pct_total,move_callbacks,callbacks_per_s,working_set_bytes,private_bytes,handles,threads\r\n");
                    Console.WriteLine("START: {0}; PID {1}; {2}s; control window visible={3}. Recorder never changes cursor input.", label, processId, seconds, controlVisible);
                    Console.Out.Flush();
                    Stopwatch watch = Stopwatch.StartNew();
                    for (int i = 1; i <= seconds; i++)
                    {
                        int remaining = (int)(i * 1000L - watch.ElapsedMilliseconds);
                        if (remaining > 0) Thread.Sleep(remaining);
                        process.Refresh();
                        double elapsed = watch.Elapsed.TotalSeconds, cpu = process.TotalProcessorTime.TotalMilliseconds;
                        uint moves = controlVisible ? Query(window, 1) : 0;
                        double span = elapsed - previousTime;
                        uint count = unchecked(moves - previousMoves);
                        long working = process.WorkingSet64, priv = process.PrivateMemorySize64;
                        minWorking = Math.Min(minWorking, working); maxWorking = Math.Max(maxWorking, working);
                        minPrivate = Math.Min(minPrivate, priv); maxPrivate = Math.Max(maxPrivate, priv);
                        if (controlVisible && i % 5 == 0 && Query(window, 0) != 1) inactiveSamples++;
                        double oneCore = (cpu - previousCpu) / (span * 1000) * 100;
                        csv.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F8},{3:F8},{4},{5},{6},{7},{8},{9}\r\n",
                            elapsed, cpu - previousCpu, oneCore, oneCore / Environment.ProcessorCount,
                            controlVisible ? count.ToString(CultureInfo.InvariantCulture) : "", controlVisible ? (count / span).ToString("F3", CultureInfo.InvariantCulture) : "", working, priv, process.HandleCount, process.Threads.Count);
                        samples.Add(new { elapsedSeconds = elapsed, cpuMilliseconds = cpu - startCpu, callbacks = controlVisible ? (uint?)unchecked(moves - startMoves) : null, workingSetBytes = working, privateBytes = priv });
                        previousCpu = cpu; previousMoves = moves; previousTime = elapsed;
                    }
                    double duration = watch.Elapsed.TotalSeconds;
                    process.Refresh();
                    double cpuMs = process.TotalProcessorTime.TotalMilliseconds - startCpu;
                    uint totalMoves = controlVisible ? unchecked(Query(window, 1) - startMoves) : 0;
                    ulong finalCycles;
                    cyclesAvailable = QueryProcessCycleTime(queryHandle, out finalCycles) && cyclesAvailable;
                    uint? finalState = controlVisible ? (uint?)Query(window, 0) : null;
                    var report = new {
                        label = label, utc = DateTime.UtcNow.ToString("o"), processId = processId,
                        executable = executable, durationSeconds = duration,
                        logicalProcessors = Environment.ProcessorCount, cpuMilliseconds = cpuMs,
                        averageCpuPercentOneCore = cpuMs / (duration * 1000) * 100,
                        averageCpuPercentTotal = cpuMs / (duration * 1000) * 100 / Environment.ProcessorCount,
                        mouseMoveCallbacks = controlVisible ? (uint?)totalMoves : null, callbacksPerSecond = controlVisible ? (double?)(totalMoves / duration) : null,
                        processCycles = cyclesAvailable ? (ulong?)(finalCycles - startCycles) : null,
                        workingSetStartBytes = firstWorking, workingSetMinBytes = minWorking, workingSetMaxBytes = maxWorking,
                        privateStartBytes = firstPrivate, privateMinBytes = minPrivate, privateMaxBytes = maxPrivate,
                        finalHandles = process.HandleCount, finalThreads = process.Threads.Count,
                        initialEngineState = initialState, finalEngineState = finalState, inactiveSamples = inactiveSamples,
                        controlWindowVisible = controlVisible,
                        limitations = "If the app's desktop is inaccessible, engine state and callback counts are unavailable (null), not zero. CPU time has OS accounting granularity. Callback counts include delivered injected movement; they do not identify hardware polling rate. No input-latency or system-wide hook overhead claim. CPU cycles are not converted to elapsed time.",
                        samples = samples
                    };
                    string jsonPath = Path.Combine(directory, prefix + ".json");
                    File.WriteAllText(jsonPath, new JavaScriptSerializer().Serialize(report), Encoding.UTF8);
                    File.WriteAllText(Path.Combine(directory, prefix + ".csv"), csv.ToString(), Encoding.UTF8);
                    Console.WriteLine("END: {0:F2}s; callbacks={1}; {2:F3} CPU ms; {3:F6}% total CPU; {4:F6}% one core; process cycles={5}.",
                        duration, controlVisible ? totalMoves.ToString(CultureInfo.InvariantCulture) : "unavailable", cpuMs, report.averageCpuPercentTotal, report.averageCpuPercentOneCore, report.processCycles);
                    Console.WriteLine("Working set: {0:F2}-{1:F2} MiB; private: {2:F2}-{3:F2} MiB; state={4}; inactive observations={5}.",
                        minWorking / 1048576.0, maxWorking / 1048576.0, minPrivate / 1048576.0, maxPrivate / 1048576.0, finalState, inactiveSamples);
                    Console.WriteLine("Report: " + jsonPath);
                    return (!finalState.HasValue || finalState == 1) && inactiveSamples == 0 ? 0 : 1;
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
