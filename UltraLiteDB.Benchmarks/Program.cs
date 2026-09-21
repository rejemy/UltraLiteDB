using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// Usage: UltraLiteDB.Benchmarks [serializer|accessors|all] [--seconds N] [--filter text]
	/// See README.md.
	/// </summary>
	public static class Program
	{
		public static int Main(string[] args)
		{
			var suite = "serializer";
			var seconds = 3.0;
			string? filter = null;

			for (var i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--seconds":
						seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
						break;
					case "--filter":
						filter = args[++i];
						break;
					case "serializer":
					case "accessors":
					case "all":
						suite = args[i];
						break;
					case "-h":
					case "--help":
						Console.WriteLine("Usage: UltraLiteDB.Benchmarks [serializer|accessors|all] [--seconds N] [--filter text]");
						return 0;
					default:
						Console.Error.WriteLine($"Unknown argument '{args[i]}'. Use --help.");
						return 1;
				}
			}

			bool Include(string name) => filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

			var runtime = RuntimeFeature.IsDynamicCodeSupported ? "JIT (CoreCLR)" : "NativeAOT";
			Console.WriteLine($"Runtime: {runtime}, .NET {Environment.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
			Console.WriteLine();

			if (suite == "serializer" || suite == "all")
			{
				SerializerSuite.Run(seconds, Include);
				Console.WriteLine();
			}

			if (suite == "accessors" || suite == "all")
			{
				AccessorSuite.Run(seconds, Include);
				Console.WriteLine();
			}

			return 0;
		}
	}
}
