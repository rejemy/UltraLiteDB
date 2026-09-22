#nullable enable

using System;
using System.Text;
using UnityEngine;

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// Unity's built-in JsonUtility (native C++ implementation) on the fields model, as the reference
	/// UltraLiteDB is measured against inside Unity. Not an exact equivalent: JsonUtility silently skips
	/// Dictionary, DateTime and Guid fields and writes enums as numbers, so it serializes less data.
	/// </summary>
	public static class JsonUtilitySuite
	{
		public static void Run(double seconds, Func<string, bool> include)
		{
			var player = Models.CreateFieldPlayer();
			var json = JsonUtility.ToJson(player);
			var jsonBytes = Encoding.UTF8.GetBytes(json);

			var back = JsonUtility.FromJson<FPlayer>(json);

			if (back.Name != player.Name || back.Inventory!.Count != player.Inventory!.Count || back.Scores![49] != player.Scores![49])
			{
				throw new InvalidOperationException("JsonUtility round-trip mismatch");
			}

			Console.WriteLine();
			Console.WriteLine($"Unity JsonUtility reference (fields model, {jsonBytes.Length} bytes of JSON; skips the Dictionary, DateTime and Guid fields):");

			Bench("jsonutility: ToJson", () => JsonUtility.ToJson(player));
			Bench("jsonutility: ToJson + UTF-8 bytes", () => Encoding.UTF8.GetBytes(JsonUtility.ToJson(player)));
			Bench("jsonutility: FromJson", () => JsonUtility.FromJson<FPlayer>(json));
			Bench("jsonutility: UTF-8 bytes + FromJson", () => JsonUtility.FromJson<FPlayer>(Encoding.UTF8.GetString(jsonBytes)));

			void Bench(string name, Action action)
			{
				if (include(name)) Harness.Run(name, action, seconds);
			}
		}
	}
}
