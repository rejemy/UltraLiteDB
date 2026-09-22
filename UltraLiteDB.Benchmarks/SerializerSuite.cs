#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
#if NET5_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
#endif

namespace UltraLiteDB.Benchmarks
{
	/// <summary>
	/// End-to-end serializer benchmarks: the direct POCO paths (BsonMapper.DirectSerialize/DirectDeserialize),
	/// the BsonDocument path for reference, and System.Text.Json as a mainstream reference (JIT only).
	/// Also compiled into the Unity IL2CPP benchmark (UnityTest/), so .NET 5+ only APIs stay behind NET5_0_OR_GREATER.
	/// </summary>
	public static class SerializerSuite
	{
		// NativeAOT trims constructors that are only reached through reflection (Activator.CreateInstance),
		// exactly like Unity's managed code stripping (link.xml). Every collection type the mapper has to
		// instantiate on deserialize must be listed here. Add entries when adding models. (Unity IL2CPP keeps
		// these without help at the default Minimal stripping level, since the models construct them directly.)
#if NET5_0_OR_GREATER
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(List<Item>))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(List<FItem>))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(List<string>))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Dictionary<string, int>))]
#endif
		public static void Run(double seconds, Func<string, bool> include)
		{
			var mapper = new BsonMapper();
			var fieldMapper = new BsonMapper { IncludeFields = true };

			var player = Models.CreatePlayer();
			var fieldPlayer = Models.CreateFieldPlayer();

			var bytes = mapper.SerializeToBytes(player);
			var fieldBytes = fieldMapper.SerializeToBytes(fieldPlayer);

			Console.WriteLine($"Document size: {bytes.Length} bytes (properties model), {fieldBytes.Length} bytes (fields model)");

			VerifyRoundTrip(mapper, player, bytes, fieldMapper, fieldPlayer, fieldBytes);

			var reusedWriter = new ByteWriter(4096);
			var stream = new MemoryStream(4096);
			var readStream = new MemoryStream(bytes);

			Console.WriteLine();
			Console.WriteLine("Properties model:");
			Bench("serialize: SerializeToBytes<T>", () => mapper.SerializeToBytes(player));
			Bench("serialize: SerializeToBytes<T>, reused ByteWriter", () => { reusedWriter.Position = 0; mapper.SerializeToBytes(player, reusedWriter); });
			Bench("serialize: SerializeToStream<T> (MemoryStream)", () => { stream.Position = 0; stream.SetLength(0); mapper.SerializeToStream(player, stream); });
			Bench("deserialize: DeserializeFromBytes<T>", () => mapper.DeserializeFromBytes<Player>(bytes));
			Bench("deserialize: DeserializeFromStream<T>", () => { readStream.Position = 0; mapper.DeserializeFromStream<Player>(readStream); });
			Bench("document: ToDocument + BsonWriter", () => BsonWriter.Serialize(mapper.ToDocument(player)));
			Bench("document: BsonReader + ToObject", () => mapper.ToObject<Player>(BsonReader.Deserialize(bytes)));

			// the two halves of the document path, which is what typed database collections run: insert/update is
			// ToDocument + BsonWriter.Serialize, reads are BsonReader.Deserialize (+ ToObject for typed collections)
			var document = mapper.ToDocument(player);
			Bench("document: BsonWriter.Serialize(doc) only", () => BsonWriter.Serialize(document));
			Bench("document: BsonReader.Deserialize only", () => BsonReader.Deserialize(bytes));
			Bench("document: mapper ToDocument only", () => mapper.ToDocument(player));
			Bench("document: mapper ToObject(doc) only", () => mapper.ToObject<Player>(document));

			Console.WriteLine();
			Console.WriteLine("Fields model (IncludeFields = true):");
			Bench("serialize: SerializeToBytes<T> (fields)", () => fieldMapper.SerializeToBytes(fieldPlayer));
			Bench("deserialize: DeserializeFromBytes<T> (fields)", () => fieldMapper.DeserializeFromBytes<FPlayer>(fieldBytes));

#if NET5_0_OR_GREATER
			Console.WriteLine();
			Console.WriteLine("System.Text.Json reference (properties model, UTF-8):");

			if (RuntimeFeature.IsDynamicCodeSupported)
			{
				var options = new System.Text.Json.JsonSerializerOptions();
				var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(player, options);

				Bench("stj: SerializeToUtf8Bytes", () => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(player, options));
				Bench("stj: Deserialize", () => System.Text.Json.JsonSerializer.Deserialize<Player>(json, options));
			}
			else
			{
				Console.WriteLine("  (skipped: reflection-based System.Text.Json is disabled under NativeAOT)");
			}
#endif

			void Bench(string name, Action action)
			{
				if (include(name)) Harness.Run(name, action, seconds);
			}
		}

		/// <summary>
		/// Guards against benchmarking a broken serializer: the numbers are meaningless if the round trip isn't exact.
		/// </summary>
		private static void VerifyRoundTrip(BsonMapper mapper, Player player, byte[] bytes, BsonMapper fieldMapper, FPlayer fieldPlayer, byte[] fieldBytes)
		{
			var back = mapper.DeserializeFromBytes<Player>(bytes);

			if (back.Name != player.Name ||
				back.Inventory!.Count != player.Inventory!.Count ||
				back.Inventory[7].Weight != player.Inventory[7].Weight ||
				back.Stats!["luck"] != player.Stats!["luck"] ||
				back.Scores![49] != player.Scores![49] ||
				back.Position!.Z != player.Position!.Z ||
				back.LastLogin != player.LastLogin ||
				back.AccountId != player.AccountId ||
				back.FavoriteKind != player.FavoriteKind)
			{
				throw new InvalidOperationException("Properties model round-trip mismatch");
			}

			var bsonBytes = BsonWriter.Serialize(mapper.ToDocument(player));

			if (!bsonBytes.AsSpan().SequenceEqual(bytes))
			{
				throw new InvalidOperationException("Direct serializer output differs from the BsonDocument path");
			}

			var fieldBack = fieldMapper.DeserializeFromBytes<FPlayer>(fieldBytes);

			if (fieldBack.Name != fieldPlayer.Name || fieldBack.Inventory!.Count != fieldPlayer.Inventory!.Count || fieldBack.Inventory[7].Weight != fieldPlayer.Inventory[7].Weight)
			{
				throw new InvalidOperationException("Fields model round-trip mismatch");
			}
		}
	}
}
