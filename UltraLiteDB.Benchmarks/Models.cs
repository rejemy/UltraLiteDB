using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraLiteDB.Benchmarks
{
	// A typical game-save shaped object: scalars of every common type, a nested object, a list of
	// small objects, a primitive array, a string-keyed dictionary and enums. Serializes to ~2.8 KB.

	public enum ItemKind { Weapon, Armor, Potion, Junk }

	public class Vec3
	{
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
	}

	public class Item
	{
		public int Id { get; set; }
		public string? Name { get; set; }
		public ItemKind Kind { get; set; }
		public float Weight { get; set; }
		public int Count { get; set; }
		public bool Equipped { get; set; }
	}

	public class Player
	{
		public int Id { get; set; }
		public string? Name { get; set; }
		public int Level { get; set; }
		public long Experience { get; set; }
		public double Gold { get; set; }
		public float Health { get; set; }
		public bool IsAlive { get; set; }
		public DateTime LastLogin { get; set; }
		public Guid AccountId { get; set; }
		public ItemKind FavoriteKind { get; set; }
		public Vec3? Position { get; set; }
		public List<Item>? Inventory { get; set; }
		public int[]? Scores { get; set; }
		public Dictionary<string, int>? Stats { get; set; }
		public List<string>? Tags { get; set; }
	}

	// The same shape using public fields (what Unity's JsonUtility serializes), mapped with IncludeFields.

	public class FVec3 { public float X; public float Y; public float Z; }

	public class FItem { public int Id; public string? Name; public ItemKind Kind; public float Weight; public int Count; public bool Equipped; }

	public class FPlayer
	{
		public int Id; public string? Name; public int Level; public long Experience; public double Gold; public float Health; public bool IsAlive;
		public DateTime LastLogin; public Guid AccountId; public ItemKind FavoriteKind; public FVec3? Position;
		public List<FItem>? Inventory; public int[]? Scores; public Dictionary<string, int>? Stats; public List<string>? Tags;
	}

	public static class Models
	{
		public static Player CreatePlayer()
		{
			var p = new Player
			{
				Id = 12345,
				Name = "Sir Benchmark the Bold",
				Level = 42,
				Experience = 9876543210,
				Gold = 12345.67,
				Health = 87.5f,
				IsAlive = true,
				LastLogin = new DateTime(2026, 9, 1, 12, 34, 56, DateTimeKind.Utc),
				AccountId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
				FavoriteKind = ItemKind.Armor,
				Position = new Vec3 { X = 1.5f, Y = -20.25f, Z = 300.125f },
				Inventory = new List<Item>(),
				Scores = Enumerable.Range(0, 50).Select(i => i * 37).ToArray(),
				Stats = new Dictionary<string, int>(),
				Tags = new List<string> { "veteran", "guild-leader", "pvp", "founder", "beta" },
			};

			for (var i = 0; i < 20; i++)
			{
				p.Inventory.Add(new Item { Id = 1000 + i, Name = "Item number " + i, Kind = (ItemKind)(i % 4), Weight = i * 0.75f, Count = i + 1, Equipped = i % 3 == 0 });
			}

			foreach (var s in new[] { "str", "dex", "con", "int", "wis", "cha", "luck", "speed", "armor", "resist" })
			{
				p.Stats[s] = s.Length * 7;
			}

			return p;
		}

		public static FPlayer CreateFieldPlayer()
		{
			var s = CreatePlayer();

			return new FPlayer
			{
				Id = s.Id, Name = s.Name, Level = s.Level, Experience = s.Experience, Gold = s.Gold, Health = s.Health, IsAlive = s.IsAlive,
				LastLogin = s.LastLogin, AccountId = s.AccountId, FavoriteKind = s.FavoriteKind,
				Position = new FVec3 { X = s.Position!.X, Y = s.Position.Y, Z = s.Position.Z },
				Inventory = s.Inventory!.Select(i => new FItem { Id = i.Id, Name = i.Name, Kind = i.Kind, Weight = i.Weight, Count = i.Count, Equipped = i.Equipped }).ToList(),
				Scores = s.Scores, Stats = s.Stats, Tags = s.Tags,
			};
		}
	}
}
