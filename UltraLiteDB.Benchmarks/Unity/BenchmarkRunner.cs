#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// Player entry point for running the benchmarks under Unity (IL2CPP). Copied into UnityTest/Assets by
	/// run-il2cpp.sh together with the shared suites. It runs as soon as a built player starts (never in the
	/// Editor), writes the results to the player log and to -benchOutput, then quits with exit code 0/1.
	/// </summary>
	/// <remarks>
	/// UnityTest -batchmode -nographics -logFile player.log -benchOutput results.txt
	///           [-benchSuite serializer|accessors|all] [-benchSeconds N] [-benchFilter text]
	/// </remarks>
	public static class BenchmarkRunner
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Run()
		{
			if (Application.isEditor) return;

			var suite = GetArg("-benchSuite") ?? "all";
			var seconds = double.Parse(GetArg("-benchSeconds") ?? "3", CultureInfo.InvariantCulture);
			var filter = GetArg("-benchFilter");
			var outputPath = GetArg("-benchOutput") ?? Path.Combine(Application.persistentDataPath, "bench-results.txt");

			bool Include(string name) => filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

			// keep the player log readable: no stack trace after every result line
			Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

			var exitCode = 0;
			var originalOut = Console.Out;

			using (var file = new StreamWriter(outputPath, false, new UTF8Encoding(false)) { AutoFlush = true })
			{
				// the shared suites print with Console.WriteLine
				Console.SetOut(new LineWriter(file));

				try
				{
					PrintEnvironment();
					ConfigureAllocationMeasurement();
					Console.WriteLine();

					if (suite == "serializer" || suite == "all")
					{
						SerializerSuite.Run(seconds, Include);
						JsonUtilitySuite.Run(seconds, Include);
						Console.WriteLine();
					}

					if (suite == "accessors" || suite == "all")
					{
						AccessorSuite.Run(seconds, Include);
						Console.WriteLine();
					}

					Console.WriteLine("Benchmarks complete.");
				}
				catch (Exception e)
				{
					Console.WriteLine("Benchmark run failed: " + e);
					exitCode = 1;
				}
				finally
				{
					Console.Out.Flush();
					Console.SetOut(originalOut);
				}
			}

			Application.Quit(exitCode);
		}

		private static void PrintEnvironment()
		{
#if ENABLE_IL2CPP
			var backend = "IL2CPP";
#else
			var backend = "Mono";
#endif
#if DEVELOPMENT_BUILD
			backend += " (development build)";
#endif
			Console.WriteLine($"Runtime: Unity {Application.unityVersion} {backend}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, {SystemInfo.processorType}, {SystemInfo.operatingSystem}");
		}

		/// <summary>
		/// Uses the per-thread allocation counter if this runtime implements it; otherwise measures each
		/// benchmark's allocations in a separate pass with the GC disabled, via GC.GetTotalMemory.
		/// </summary>
		private static void ConfigureAllocationMeasurement()
		{
			long delta;

			try
			{
				var before = GC.GetAllocatedBytesForCurrentThread();
				var probe = new byte[100_000];
				delta = GC.GetAllocatedBytesForCurrentThread() - before;
				GC.KeepAlive(probe);
			}
			catch (Exception)
			{
				delta = -1;
			}

			if (delta >= 100_000 && delta < 200_000)
			{
				Console.WriteLine("Allocations: GC.GetAllocatedBytesForCurrentThread (exact)");
				return;
			}

			Harness.AllocationProbe = ProbeAllocations;
			Console.WriteLine($"Allocations: GC.GetTotalMemory delta with the GC disabled, over {PROBE_CALLS} calls (approximate; per-thread counter unavailable, test delta = {delta})");
		}

		private const int PROBE_CALLS = 256;

		private static double ProbeAllocations(Action action)
		{
			GC.Collect();
			var mode = GarbageCollector.GCMode;
			GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;

			try
			{
				var before = GC.GetTotalMemory(false);
				for (var i = 0; i < PROBE_CALLS; i++) action();
				var after = GC.GetTotalMemory(false);

				return Math.Max(0, after - before) / (double)PROBE_CALLS;
			}
			finally
			{
				GarbageCollector.GCMode = mode;
				GC.Collect();
			}
		}

		private static string? GetArg(string name)
		{
			var args = Environment.GetCommandLineArgs();

			for (var i = 0; i < args.Length - 1; i++)
			{
				if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
			}

			return null;
		}

		/// <summary>
		/// Console replacement that forwards whole lines to the results file and the player log.
		/// </summary>
		private sealed class LineWriter : TextWriter
		{
			private readonly StreamWriter _file;
			private readonly StringBuilder _line = new StringBuilder();

			public LineWriter(StreamWriter file)
			{
				_file = file;
			}

			public override Encoding Encoding => Encoding.UTF8;

			public override void Write(char value)
			{
				if (value == '\n')
				{
					var line = _line.ToString().TrimEnd('\r');
					_line.Clear();
					_file.WriteLine(line);
					Debug.Log(line.Length == 0 ? " " : line);
				}
				else
				{
					_line.Append(value);
				}
			}

			public override void Write(string? value)
			{
				if (value == null) return;
				foreach (var c in value) this.Write(c);
			}

			public override void Write(char[] buffer, int index, int count)
			{
				for (var i = 0; i < count; i++) this.Write(buffer[index + i]);
			}

			public override void Flush()
			{
				if (_line.Length > 0) this.Write('\n');
				_file.Flush();
			}
		}
	}
}
