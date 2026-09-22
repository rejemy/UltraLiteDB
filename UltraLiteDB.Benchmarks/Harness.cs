#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// Minimal dependency-free measurement loop (BenchmarkDotNet can't run under NativeAOT, which is the
	/// runtime we care most about here). Reports the median of 5 timed rounds, and the bytes allocated
	/// per operation on the calling thread.
	/// </summary>
	public static class Harness
	{
		private const int ROUNDS = 5;
		private const int BATCH = 16;

		/// <summary>
		/// Optional replacement for the per-thread allocation counter, for runtimes that don't implement
		/// <see cref="GC.GetAllocatedBytesForCurrentThread"/>. Called once per benchmark after warmup with the
		/// benchmark action; returns bytes allocated per call. The Unity runner installs one if needed.
		/// </summary>
		public static Func<Action, double>? AllocationProbe;

		/// <summary>
		/// Runs <paramref name="action"/> for about <paramref name="seconds"/> (plus warmup) and prints
		/// time and allocation per call. <paramref name="opsPerCall"/> divides both, for calls that do
		/// several operations (e.g. one call touching every member of an object).
		/// </summary>
		public static void Run(string name, Action action, double seconds, int opsPerCall = 1, string unit = "op")
		{
			// warmup: JIT tiering / cache population
			var warm = Stopwatch.StartNew();
			while (warm.Elapsed.TotalSeconds < Math.Min(0.5, seconds / 2)) action();

			var probedBytes = AllocationProbe?.Invoke(action) / opsPerCall;

			var results = new List<(double ns, double bytes)>();

			for (var round = 0; round < ROUNDS; round++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();

				long calls = 0;
				var before = probedBytes.HasValue ? 0 : GC.GetAllocatedBytesForCurrentThread();
				var sw = Stopwatch.StartNew();

				while (sw.Elapsed.TotalSeconds < seconds / ROUNDS)
				{
					for (var i = 0; i < BATCH; i++) action();
					calls += BATCH;
				}

				sw.Stop();
				var allocated = probedBytes.HasValue ? 0 : GC.GetAllocatedBytesForCurrentThread() - before;
				var ops = (double)calls * opsPerCall;

				results.Add((sw.Elapsed.TotalMilliseconds * 1e6 / ops, probedBytes ?? allocated / ops));
			}

			var median = results.OrderBy(r => r.ns).ElementAt(ROUNDS / 2);

			if (median.ns >= 1000)
			{
				Console.WriteLine($"  {name,-52} {median.ns / 1000.0,9:F2} us/{unit} {median.bytes,10:N0} B/{unit}");
			}
			else
			{
				Console.WriteLine($"  {name,-52} {median.ns,9:F1} ns/{unit} {median.bytes,10:N1} B/{unit}");
			}
		}
	}
}
