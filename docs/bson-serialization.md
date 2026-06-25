# BSON Serialization Guide

UltraLiteDB stores everything as **BSON** — a compact, binary, typed superset of JSON
([bsonspec.org](https://bsonspec.org)). The same serialization system that powers the
database is fully usable on its own: you can build documents by hand, map plain C#
objects (POCOs) to and from documents, convert to bytes for storage or transport, and
round‑trip through JSON.

This guide is a practical manual for that system. It covers the in‑memory value model
(`BsonValue`, `BsonDocument`, `BsonArray`), the type rules for C# values, the POCO
mapper, attributes and custom serializers, JSON, the two serialization paths
(`BsonDocument` vs. *direct‑to‑bytes*), and a frank list of the rough edges you are
likely to hit.

For how those documents are stored, indexed, and queried once they're in the database,
see the companion [Database & Collections Guide](database-operations.md).

Everything here lives in the `UltraLiteDB` namespace:

```csharp
using UltraLiteDB;
```

---

## Table of contents

1. [The value model at a glance](#1-the-value-model-at-a-glance)
2. [Working with `BsonDocument`](#2-working-with-bsondocument)
3. [`BsonArray`](#3-bsonarray)
4. [`BsonValue` — types, conversions, comparison](#4-bsonvalue--types-conversions-comparison)
5. [Supported C# types](#5-supported-c-types)
6. [`ObjectId`](#6-objectid)
7. [Serializing to and from bytes](#7-serializing-to-and-from-bytes)
8. [The POCO ↔ BSON mapper](#8-the-poco--bson-mapper)
9. [Mapper configuration](#9-mapper-configuration)
10. [Attributes](#10-attributes)
11. [Fluent mapping with `EntityBuilder`](#11-fluent-mapping-with-entitybuilder)
12. [Custom type serializers](#12-custom-type-serializers)
13. [Direct serialization vs. `BsonDocument` serialization](#13-direct-serialization-vs-bsondocument-serialization)
14. [JSON support](#14-json-support)
15. [Worked example: a game save](#15-worked-example-a-game-save)
16. [Gotchas and rough edges](#16-gotchas-and-rough-edges)
17. [Cheat sheet](#17-cheat-sheet)

---

## 1. The value model at a glance

Three classes make up the entire in‑memory model:

| Class | Represents | Backed by |
|---|---|---|
| `BsonValue` | A single typed value (number, string, date, …) | a boxed .NET value + a `BsonType` tag |
| `BsonDocument` | An ordered, string‑keyed map of fields (`: BsonValue`) | `Dictionary<string, BsonValue>` (case‑insensitive) |
| `BsonArray` | An ordered list of values (`: BsonValue`) | `List<BsonValue>` |

`BsonDocument` and `BsonArray` both *derive* from `BsonValue`, so a document can be a
field of another document, an element of an array, and so on, to arbitrary depth (up to
a guard limit — see [§16](#16-gotchas-and-rough-edges)).

Every `BsonValue` carries a `BsonType` tag from this enum (the numeric order is also the
cross‑type sort order):

```
MinValue(0) Null(1) Int32(2) Int64(3) Double(4) Decimal(5) String(6)
Document(7) Array(8) Binary(9) ObjectId(10) Guid(11) Boolean(12) DateTime(13) MaxValue(14)
```

`MinValue` and `MaxValue` are sentinels that sort below/above every other value — handy
for range queries.

---

## 2. Working with `BsonDocument`

### Creating documents

```csharp
// Empty, then fill by key
var doc = new BsonDocument();
doc["Name"]     = "John Doe";
doc["Level"]    = 7;
doc["IsActive"] = true;

// Collection initializer
var doc2 = new BsonDocument
{
    ["Name"]  = "Joana",
    ["Level"] = 10,
};

// From a dictionary
var doc3 = new BsonDocument(new Dictionary<string, BsonValue>
{
    ["x"] = 1,
    ["y"] = 2,
});
```

The right‑hand side of `doc["..."] = value` is a `BsonValue`, but you almost never write
`new BsonValue(...)` because the common C# types convert *implicitly*. `7`, `"John"`,
`true`, `3.14`, a `Guid`, a `DateTime`, a `byte[]` — all become a `BsonValue`
automatically.

### Reading fields

The indexer is **null‑safe**: a missing key returns `BsonValue.Null` rather than
throwing.

```csharp
string name = doc["Name"];          // implicit BsonValue -> string
int    level = doc["Level"];        // implicit BsonValue -> int
BsonValue maybe = doc["nope"];      // BsonValue.Null, not an exception

if (doc["IsActive"].AsBoolean) { ... }
```

Implicit conversions cast the underlying value directly, so reading a field as the wrong
type throws `InvalidCastException`. When a field may be absent or the wrong type, prefer
the **typed safe getters**, which check the type and fall back gracefully:

```csharp
string? name  = doc.GetString("Name");                 // null if missing/not a string
string  name2 = doc.GetStringOrDefault("Name", "?");    // default if missing/not a string

int?  lvl   = doc.GetInt32("Level");                    // null if missing/not numeric
int   lvl2  = doc.GetInt32OrDefault("Level", 1);
long  big   = doc.GetInt64OrDefault("Score", 0);
float f     = doc.GetSingleOrDefault("Ratio", 0f);
double d    = doc.GetDoubleOrDefault("Ratio", 0d);
bool  on    = doc.GetBoolOrDefault("IsActive", false);
```

The numeric getters accept **any** numeric field and convert (an `Int64` field read via
`GetInt32` is narrowed), which is convenient but can lose precision.

### Inspecting and mutating

```csharp
doc.ContainsKey("Name");      // bool
doc.Count;                    // field count
doc.Keys;                     // ICollection<string>
doc.Values;                   // ICollection<BsonValue>
doc.Remove("Level");
doc.Clear();

foreach (var kv in doc)       // KeyValuePair<string, BsonValue>
    Console.WriteLine($"{kv.Key} = {kv.Value}");
```

`BsonDocument` implements `IDictionary<string, BsonValue>`, so anything you can do with a
dictionary works here too.

### `_id` and field ordering

By convention the primary key lives in the `_id` field. `GetElements()` yields `_id`
first (when present), then the remaining fields — this is the order used when writing to
the data file and to JSON:

```csharp
foreach (var el in doc.GetElements()) { /* _id first */ }
```

`CopyTo(BsonDocument other)` merges this document's fields into another, overwriting on
key collisions.

### Keys are case-insensitive

The backing dictionary uses `StringComparer.OrdinalIgnoreCase`. `doc["Name"]` and
`doc["name"]` are the **same field** — assigning both leaves you with one. This matches
how the engine looks up `_id` and index fields, but it can surprise you if you intended
two distinct keys.

### Nesting

```csharp
var character = new BsonDocument
{
    ["Name"] = "Aria",
    ["Stats"] = new BsonDocument { ["Str"] = 12, ["Dex"] = 15 },
    ["Inventory"] = new BsonArray { "sword", "torch", "rope" },
};

int str = character["Stats"]["Str"];          // 12
string first = character["Inventory"][0];      // "sword"
```

---

## 3. `BsonArray`

`BsonArray` is an ordered `IList<BsonValue>`. Null elements are stored as
`BsonValue.Null` (never a CLR `null`).

```csharp
var arr = new BsonArray { 1, 2, 3 };
arr.Add("four");                 // arrays are heterogeneous
arr.Insert(0, true);
arr.RemoveAt(0);

int n = arr.Count;
BsonValue first = arr[0];

// Build from existing collections
var fromList = new BsonArray(new List<BsonValue> { "a", "b" });

// AddRange accepts BsonValues, or arbitrary objects (converted via FromObject)
var nums = new BsonArray();
nums.AddRange(new[] { 10, 20, 30 });           // ints -> BsonValues
```

Arrays compare element‑by‑element, then by length, which makes them usable as sortable
composite keys.

---

## 4. `BsonValue` — types, conversions, comparison

### Building values explicitly

You rarely need this thanks to implicit conversions, but the constructors and the
`FromObject` helper exist:

```csharp
var v1 = new BsonValue(42);
var v2 = new BsonValue("hello");
var v3 = BsonValue.FromObject(someInt);   // primitives & collections only
```

> `BsonValue.FromObject` handles primitives, `byte[]`, `Guid`, `ObjectId`, `DateTime`,
> dictionaries and `IEnumerable`s — but **throws `InvalidCastException` for POCOs**. For
> complex objects use the mapper (`BsonMapper.ToDocument`/`ToBsonValue`), covered in
> [§8](#8-the-poco--bson-mapper).

### Type tests and accessors

Every type has an `Is*` test and an `As*` accessor:

```csharp
if (v.IsString)  { string s = v.AsString; }
if (v.IsNumber)  { decimal d = v.AsDecimal; }   // IsNumber = Int32|Int64|Double|Decimal
if (v.IsNull)    { ... }

v.AsInt32; v.AsInt64; v.AsDouble; v.AsDecimal; v.AsSingle;
v.AsString; v.AsBoolean; v.AsDateTime; v.AsGuid; v.AsObjectId;
v.AsBinary;   // ArraySegment<byte>
v.AsArray;    // BsonArray?  (null if not an array)
v.AsDocument; // BsonDocument? (null if not a document)
```

The numeric `As*` accessors go through `Convert.To*`, so they will coerce between numeric
types. The non‑numeric ones (`AsString`, `AsGuid`, …) are hard casts and throw on a type
mismatch — gate them with the matching `Is*` test, or use the document‑level safe getters
from [§2](#2-working-with-bsondocument).

`AsRawValue` exposes the boxed underlying .NET object if you need to bypass the wrapper.

### Comparison and equality

`BsonValue` implements `IComparable<BsonValue>` and `IEquatable<BsonValue>` and overloads
`== != < <= > >=`. Different types compare by their `BsonType` order; two numbers of
different types compare by value (promoted to `decimal`). Strings compare via a
`Collation` (binary by default).

```csharp
new BsonValue(1) == new BsonValue(1);     // true
new BsonValue(1) < new BsonValue(2);      // true
doc["missing"] == BsonValue.Null;          // true — and == null is also true
```

`==` treats a CLR `null` operand and `BsonValue.Null` as equal, so
`doc["missing"] == null` is `true`.

### Arithmetic

The numeric operators `+ - * /` are defined and return `BsonValue.Null` if either operand
is non‑numeric — useful for quick aggregation over documents without unwrapping:

```csharp
BsonValue total = doc["a"] + doc["b"];
```

---

## 5. Supported C# types

When a value flows into BSON (whether you assign it to a document field or the mapper
serializes a POCO property), it lands in one of the native BSON types below.

### Native types — stored as their own `BsonType`

| C# type | BSON type | Notes |
|---|---|---|
| `int` / `Int32` | Int32 | |
| `long` / `Int64` | Int64 | |
| `double` | Double | |
| `decimal` | Decimal | 16 bytes, full precision |
| `string` | String | UTF‑8; see trim/empty options in [§9](#9-mapper-configuration) |
| `bool` | Boolean | |
| `DateTime` | DateTime | stored **UTC**, truncated to **milliseconds** |
| `Guid` | Guid | binary subtype `0x04` |
| `ObjectId` | ObjectId | 12 bytes |
| `byte[]` | Binary | wrapped as `ArraySegment<byte>` internally |
| `ArraySegment<byte>` | Binary | zero‑copy binary |
| `BsonValue` / `BsonDocument` / `BsonArray` | (passthrough) | used as‑is |

### Widened / converted types — no distinct BSON type

These have no dedicated BSON type and are converted on the way in. They round‑trip back to
the original CLR type **only because the mapper knows the target property type** — the raw
BSON only remembers the widened form.

| C# type | Stored as | Round‑trips back via |
|---|---|---|
| `short`, `ushort`, `byte`, `sbyte` | Int32 | `Convert.ChangeType` |
| `uint` / `UInt32` | Int64 | narrowing cast |
| `ulong` / `UInt64` | Int64 (**unchecked bit cast**) | unchecked cast back |
| `float` / `Single` | **Double** | `Convert.ToSingle` |
| `char` | String (length 1) | first char |
| `enum` | **String** (member name) | `Enum.Parse` |

### Composite types

| C# type | BSON type |
|---|---|
| `IDictionary` / `IDictionary<K,V>` | Document (keys become field names via `ToString()`) |
| Arrays, `IList<T>`, `IEnumerable<T>` | Array |
| Any other class/struct (POCO) | Document |

A few CLR types you might expect to be primitive are handled as **registered custom
types** instead (see [§12](#12-custom-type-serializers)): `Uri`, `TimeSpan`,
`DateTimeOffset`, and `Regex` work out of the box.

> See [§16](#16-gotchas-and-rough-edges) for the consequences of the widened types —
> `float` precision, enum‑as‑string, `DateTime` UTC/precision, and `ulong` sign bits all
> have sharp corners.

---

## 6. `ObjectId`

`ObjectId` is a 12‑byte identifier (4‑byte timestamp + 3‑byte machine + 2‑byte PID +
3‑byte counter). It is the default auto‑generated `_id` type for collections, and it is a
first‑class BSON value.

```csharp
var id = ObjectId.NewObjectId();          // generate
var id2 = new ObjectId("507f1f77bcf86cd799439011");  // 24-char hex
DateTime created = id.CreationTime;        // embedded timestamp (UTC)
string hex = id.ToString();                // 24 lowercase hex chars
byte[] raw = id.ToByteArray();             // 12 bytes
ObjectId.Empty;                            // all-zero id
```

`ObjectId` is comparable and sortable; because the timestamp is the high‑order component,
ids sort roughly by creation time.

---

## 7. Serializing to and from bytes

`BsonSerializer` is the static façade for converting a `BsonDocument` to and from the
binary BSON wire format.

```csharp
var doc = new BsonDocument { ["Name"] = "Aria", ["Level"] = 7 };

// Document -> bytes
byte[] bytes = BsonSerializer.Serialize(doc);

// bytes -> Document
BsonDocument back = BsonSerializer.Deserialize(bytes);

// Read from an offset inside a larger buffer
BsonDocument atOffset = BsonSerializer.Deserialize(buffer, offset: 128);

// Zero-copy read from a segment
BsonDocument fromSeg = BsonSerializer.Deserialize(new ArraySegment<byte>(buffer, 128, len));
```

To write into a buffer you already own (avoiding an allocation), use `SerializeTo`, which
returns the new write position:

```csharp
var buffer = new byte[4096];
int written = BsonSerializer.SerializeTo(doc, buffer, offset: 0);
```

The byte layout is standard BSON and is **file‑format compatible with LiteDB**.

If your data starts life as a POCO rather than a `BsonDocument`, you can either map it to
a document first (`mapper.ToDocument(obj)`, then `BsonSerializer.Serialize`) or use the
*direct* path that skips the intermediate document entirely
([§13](#13-direct-serialization-vs-bsondocument-serialization)).

---

## 8. The POCO ↔ BSON mapper

`BsonMapper` converts ordinary C# classes to `BsonDocument`s and back using reflection. A
process‑wide default instance lives at `BsonMapper.Global`; the database uses it unless
you pass your own mapper to the `UltraLiteDatabase` constructor.

```csharp
var mapper = BsonMapper.Global;          // or: new BsonMapper();

var doc = mapper.ToDocument(customer);    // POCO -> BsonDocument
var c   = mapper.ToObject<Customer>(doc); // BsonDocument -> POCO
```

> **Reuse your mapper.** Entity mappings are reflected once and cached per type. Creating
> a fresh `BsonMapper` per call throws that cache away. Use `BsonMapper.Global`, or hold
> a single configured instance.

### Mapping rules

For a class to map cleanly:

- It must be **public** with a **public parameterless constructor**.
- Only **public properties with a getter** are mapped by default. (Fields and non‑public
  members are opt‑in — see [§9](#9-mapper-configuration).)
- A property needs a **setter** to be populated on deserialize. Read‑only properties
  serialize out but are silently skipped when reading back.
- It should have an id member: a property named `Id`, or `<ClassName>Id`, or one marked
  `[BsonId]`. The id maps to the `_id` field.
- No circular references (guarded at depth 20).

```csharp
public class Customer
{
    public int Id { get; set; }            // -> "_id"
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<string> Tags { get; set; }
}
```

### The conversion surface

| Method | Direction | Result |
|---|---|---|
| `ToDocument<T>(T)` / `ToDocument(Type, object)` | POCO → | `BsonDocument` |
| `ToObject<T>(BsonDocument)` / `ToObject(Type, doc)` | → POCO | `T` |
| `ToBsonValue(object)` | any → | `BsonValue` (handles primitives, collections, POCOs) |
| `SerializeObject(object)` | POCO → | `BsonDocument` (always treats input as a complex object) |
| `SerializeToBytes<T>(T)` | POCO → | `byte[]` ([direct path](#13-direct-serialization-vs-bsondocument-serialization)) |
| `DeserializeFromBytes<T>(byte[])` | bytes → | `T` ([direct path](#13-direct-serialization-vs-bsondocument-serialization)) |

`ToDocument`/`ToObject` short‑circuit when the value already *is* a `BsonDocument`, so
passing a document through is cheap and lossless.

### Collections, dictionaries, and nested objects

These all map automatically and recursively:

```csharp
public class Order
{
    public int Id { get; set; }
    public List<LineItem> Items { get; set; }            // -> BSON array of documents
    public Dictionary<string, decimal> Totals { get; set; } // -> BSON document
    public Address ShipTo { get; set; }                  // -> nested document
}
```

Dictionary keys are converted to strings (`key.ToString()`), so non‑string keys work but
become field names. On the way back, keys are parsed to the declared key type (enums via
`Enum.Parse`, everything else via `Convert.ChangeType`).

### `object`-typed members and loose data

A member typed as `object` (or a `Dictionary<string, object>`, or `object[]`) keeps its
shape:

- A nested complex value with no type hint deserializes into a `Dictionary<string, object>`.
- A nested array deserializes into an `object[]`.
- Scalars keep their stored type (`int` stays `int`, `string` stays `string`, …).

This is what lets you store semi‑structured "bag of properties" data without a fixed
schema.

### Polymorphism (storing derived types)

When the **runtime type differs from the declared type** — e.g. a `Dog` stored through a
property typed `Animal`, or any complex value behind an `object` member — the mapper
writes a type discriminator so it can reconstruct the right class on read:

- By default it writes `_type` = the type's full name plus its assembly's simple name,
  e.g. `"MyApp.Dog, MyApp"`. Controlled by `IncludeFullType` (default `true`).
- If you register a **compact id** with `RegisterTypeId`, it writes the smaller `_t`
  field instead.

```csharp
public abstract class Animal { public int Id { get; set; } public string Name { get; set; } }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class Cat : Animal { public int Lives { get; set; } }

// Optional: compact, rename-proof discriminators
mapper.RegisterTypeId(typeof(Dog), "dog");
mapper.RegisterTypeId(typeof(Cat), "cat");

var col = db.GetCollection<Animal>("animals");
col.Insert(new Dog { Name = "Rex", GoodBoy = true });

foreach (Animal a in col.FindAll())
    Console.WriteLine(a is Dog ? "woof" : "meow");   // correct subclass restored
```

> The default `_type` string embeds the namespace and assembly name. If you later move or
> rename the type, **old documents won't deserialize**. `RegisterTypeId` decouples the
> stored discriminator from the CLR name and is the safer choice for long‑lived data.

### Wiring the mapper into the database

The mapper that a collection uses is fixed when the database is opened:

```csharp
// Option A: configure the global mapper before opening any database
BsonMapper.Global.UseCamelCase();

// Option B: pass a dedicated, pre-configured mapper
var mapper = new BsonMapper { SerializeNullValues = true };
mapper.Entity<Customer>().Id(c => c.CustomerKey);
var db = new UltraLiteDatabase("data.db", mapper);

var customers = db.GetCollection<Customer>();   // uses the configured mapper
db.Mapper;                                       // the mapper in use
```

`GetCollection<T>()` uses the document path (`ToDocument`/`ToObject`) internally — the
direct‑to‑bytes path from [§13](#13-direct-serialization-vs-bsondocument-serialization) is
opt‑in and only used when you call it explicitly.

---

## 9. Mapper configuration

All of these are properties (or methods) on a `BsonMapper` instance. Defaults shown.

| Setting | Default | Effect |
|---|---|---|
| `SerializeNullValues` | `false` | Omit members whose value is `null` (except `_id`). Set `true` to keep them. |
| `TrimWhitespace` | `false` | When `true`, `string.Trim()` every string on serialize. |
| `EmptyStringToNull` | `false` | When `true`, store empty strings (after any trim) as `BsonValue.Null`. |
| `IncludeFields` | `false` | Map public **fields**, not just properties. |
| `IncludeNonPublic` | `false` | Map private/protected/internal members too. |
| `IncludeFullType` | `true` | Emit `_type` for derived types (polymorphism). |
| `ResolveFieldName` | identity | `Func<string,string>` mapping property name → field name. |
| `ResolveMember` | no‑op | `Action<Type, MemberInfo, MemberMapper>` callback to tweak each member (set `FieldName = null` to drop it). |
| `ResolveCollectionName` | type/element name | `Func<Type,string>` for default collection naming. |

Naming helpers set `ResolveFieldName` for you and return the mapper for chaining:

```csharp
mapper.UseCamelCase();                 // FirstName -> firstName
mapper.UseLowerCaseDelimiter('_');     // FirstName -> first_name
```

> Strings are stored **verbatim** by default — `TrimWhitespace` and `EmptyStringToNull`
> are both off, so `""` and `"   "` are preserved exactly. Turn them on if you *want* the
> cleanup: with both enabled, a property holding `""` or `"   "` is stored as **null**, and
> — since null members are omitted by default — it comes back as `null` after a round trip.

Example — opt fields in and tidy strings on the way out:

```csharp
var tidy = new BsonMapper
{
    IncludeFields     = true,    // map public fields too
    TrimWhitespace    = true,    // strip surrounding whitespace
    EmptyStringToNull = true,    // collapse blank strings to null
    SerializeNullValues = true,  // but keep those nulls in the document
};
```

---

## 10. Attributes

Decorate your POCO members to control mapping without touching the global config.

### `[BsonId]` — mark the primary key

```csharp
public class Session
{
    [BsonId]
    public Guid Key { get; set; }   // -> "_id"
}
```

`[BsonId]` maps the member to `_id`. By default `AutoId` is **on**, so the engine fills in
a generated id on insert when the value is at its default. Pass `false` to require an
explicit id:

```csharp
[BsonId(false)]
public string Slug { get; set; }   // you must set Slug before Insert
```

If you don't use the attribute, the mapper still finds an id by convention: a member named
`Id` or `<ClassName>Id`.

### `[BsonField]` — rename a field

```csharp
public class Product
{
    public int Id { get; set; }

    [BsonField("sku")]
    public string StockKeepingUnit { get; set; }   // stored as "sku"
}
```

An explicit `[BsonField]` name wins over `ResolveFieldName`/`UseCamelCase`.

### `[BsonIgnore]` — exclude a member

```csharp
public class Account
{
    public int Id { get; set; }
    public string PasswordHash { get; set; }

    [BsonIgnore]
    public string PlaintextPassword { get; set; }   // never serialized
}
```

### `[BsonIndex]` — deprecated

`[BsonIndex]` is obsolete and ignored by the mapper. Create indexes at database‑setup time
with `EnsureIndex` on the collection instead:

```csharp
col.EnsureIndex("Email", unique: true);
```

---

## 11. Fluent mapping with `EntityBuilder`

If you can't (or prefer not to) annotate your classes — third‑party types, a clean domain
model, etc. — configure the mapping fluently via `Entity<T>()`. It is the code‑first
equivalent of the attributes.

```csharp
var mapper = new BsonMapper();

mapper.Entity<Customer>()
    .Id(c => c.CustomerKey, autoId: true)   // choose the _id member
    .Field(c => c.FullName, "name")          // rename a field
    .Ignore(c => c.CachedDisplayName);        // drop a member
```

| Builder method | Equivalent attribute |
|---|---|
| `.Id(expr, autoId)` | `[BsonId]` |
| `.Field(expr, "name")` | `[BsonField("name")]` |
| `.Ignore(expr)` | `[BsonIgnore]` |

Attributes and the builder can be combined; the builder is applied on top of the reflected
(and attribute‑adjusted) mapping.

---

## 12. Custom type serializers

When a type can't be mapped automatically — or you want a more compact / specific
representation — register conversion functions with `RegisterType`. They are honored by
**both** the document path and the direct path.

```csharp
// Store a Color as "#RRGGBB"
mapper.RegisterType<Color>(
    serialize:   c   => $"#{c.R:X2}{c.G:X2}{c.B:X2}",
    deserialize: bson => ParseHexColor(bson.AsString)
);
```

A custom serializer can return any `BsonValue`, including a whole `BsonDocument` for
multi‑field encodings:

```csharp
mapper.RegisterType<Money>(
    m    => new BsonDocument { ["amount"] = m.Amount, ["ccy"] = m.Currency },
    bson => new Money(bson["amount"].AsDecimal, bson["ccy"].AsString)
);
```

### Built-in custom types

These are registered for you in every `BsonMapper` and show the pattern:

| Type | Stored as |
|---|---|
| `Uri` | its `AbsoluteUri` string |
| `TimeSpan` | `Ticks` as Int64 |
| `DateTimeOffset` | its UTC `DateTime` |
| `Regex` | the pattern string, or `{ p, o }` document when options are set |

### Per-member custom serialization

For one‑off control over a single member, set `Serialize`/`Deserialize` on its
`MemberMapper` via the `ResolveMember` callback. These delegates receive the mapper, so
they can recurse:

```csharp
mapper.ResolveMember = (type, memberInfo, member) =>
{
    if (member.MemberName == "Payload")
    {
        member.Serialize   = (value, m) => Compress((string)value);
        member.Deserialize = (bson, m)  => Decompress(bson.AsBinary);
    }
};
```

---

## 13. Direct serialization vs. `BsonDocument` serialization

There are two ways to get from a POCO to bytes:

**Path A — via `BsonDocument` (the default everywhere):**

```
POCO --mapper.ToDocument--> BsonDocument --BsonSerializer.Serialize--> byte[]
```

**Path B — direct to bytes (opt‑in, allocation‑light):**

```
POCO --mapper.SerializeToBytes--> byte[]      (no intermediate BsonDocument)
```

Path A is the readable, flexible default. It builds a full `BsonDocument` object graph you
can inspect, mutate, query, and convert to JSON. The database itself uses Path A.

Path B (`SerializeToBytes` / `DeserializeFromBytes`) skips constructing the intermediate
`BsonDocument` and its per‑value `BsonValue` boxes, writing straight into a byte buffer.
For high‑throughput hot loops (think: persisting thousands of entities per frame) this
**reduces GC pressure** noticeably. The trade‑off is that there's no document to inspect
in between.

```csharp
// Direct write
byte[] bytes = mapper.SerializeToBytes(entity);

// Direct read
var entity2 = mapper.DeserializeFromBytes<MyEntity>(bytes);
var atOff   = mapper.DeserializeFromBytes<MyEntity>(buffer, offset: 64);
var fromSeg = mapper.DeserializeFromBytes<MyEntity>(new ArraySegment<byte>(buffer, 64, len));
```

To reuse a buffer across calls, pass a `ByteWriter` and read back its `Buffer`/`Position`:

```csharp
var writer = new ByteWriter(256);
mapper.SerializeToBytes(entity, writer);
// writer.Buffer[0..writer.Position] now holds the BSON
```

The direct path produces **byte‑for‑byte the same BSON** as Path A, honors the same
config (null handling, string trimming, polymorphic `_type`/`_t`), and uses the same
custom serializers — so the two are interchangeable on the wire. You can write with one
and read with the other.

Choosing between them:

| Use Path A (`ToDocument`) when… | Use Path B (`SerializeToBytes`) when… |
|---|---|
| You need to inspect/modify/query the document | You're in an allocation‑sensitive hot path |
| You want JSON / pretty output | You only need bytes in and out |
| You're using the normal collection API | You're doing manual high‑volume (de)serialization |

Every `SerializeToBytes` overload also accepts a value that already *is* a
`BsonDocument` and falls back to the standard BSON writer — the `byte[]`‑returning forms
return the encoded document, and the `ByteWriter` forms write it straight into your
buffer. So you can mix POCOs and ready‑made documents through the same call without
special‑casing.

---

## 14. JSON support

`JsonSerializer` converts any `BsonValue` (including documents and arrays) to and from
JSON. Because BSON has types JSON lacks, it uses **MongoDB‑style extended JSON**: those
types are encoded as small `{ "$type": "value" }` wrappers.

### Writing JSON

```csharp
string json = JsonSerializer.Serialize(doc);   // compact
string also = doc.ToString();                    // identical — ToString() calls the serializer
```

For pretty output, use `JsonWriter` directly (the static façade always writes compact):

```csharp
var sb = new StringBuilder();
using (var sw = new StringWriter(sb))
{
    var writer = new JsonWriter(sw) { Pretty = true, Indent = 2 };
    writer.Serialize(doc);
}
string pretty = sb.ToString();
```

You can also stream to any `TextWriter`/`StringBuilder` overload of
`JsonSerializer.Serialize`.

### Reading JSON

```csharp
BsonValue value = JsonSerializer.Deserialize(jsonString);
var doc = value.AsDocument;

// Stream a large top-level array without loading it all at once
foreach (BsonValue item in JsonSerializer.DeserializeArray(jsonText))
    Process(item);
```

For tooling that needs to detect duplicate keys or report precise positions, the lower‑
level `JsonReader.DeserializeMembers()` yields each member in source order with its
1‑based line/column (`JsonMember`), instead of merging into a document.

### Extended-type encoding

| BSON type | JSON form |
|---|---|
| ObjectId | `{ "$oid": "507f1f77bcf86cd799439011" }` |
| Guid | `{ "$guid": "…" }` |
| DateTime | `{ "$date": "2026-06-24T12:00:00.0000000Z" }` (ISO‑8601 UTC) |
| Binary | `{ "$binary": "<base64>" }` |
| Int64 | `{ "$numberLong": "123" }` |
| Decimal | `{ "$numberDecimal": "1.23" }` |
| MinValue / MaxValue | `{ "$minValue": "1" }` / `{ "$maxValue": "1" }` |

Plain JSON values map the obvious way: objects → documents, arrays → arrays, `true`/
`false`/`null` → booleans/null, strings → strings.

> **Number parsing is narrow.** A bare JSON number with no decimal point parses as
> **Int32**, and a number with a decimal point parses as **Double**. There is no automatic
> widening: an integer literal larger than `Int32.MaxValue` will **overflow** during
> parsing. To carry an `Int64` or `decimal` through JSON, use the `$numberLong` /
> `$numberDecimal` extended forms (which is exactly what the writer emits).

---

## 15. Worked example: a game save

A realistic model exercising ids, nesting, collections, dictionaries, enums, custom field
names, ignored members, and polymorphism — the kind of save file UltraLiteDB is built for.

```csharp
public enum Faction { Neutral, Alliance, Horde }

public class SaveGame
{
    [BsonId(false)]                       // explicit slot id, no auto-generation
    public int Slot { get; set; }

    public string PlayerName { get; set; }
    public DateTime LastPlayedUtc { get; set; }
    public Faction Faction { get; set; }   // stored as "Alliance" (string)

    public Character Hero { get; set; }
    public List<string> CompletedQuests { get; set; } = new();
    public Dictionary<string, bool> WorldFlags { get; set; } = new();
    public List<Item> Inventory { get; set; } = new();

    [BsonIgnore]
    public bool HasUnsavedChanges { get; set; }   // runtime-only, never persisted
}

public class Character
{
    public int Level { get; set; }
    public Dictionary<string, int> Stats { get; set; } = new();
    public Vector3 Position { get; set; }          // a struct -> nested document
}

public struct Vector3 { public float X, Y, Z; }    // needs IncludeFields = true

// Polymorphic inventory
public abstract class Item { public string Name { get; set; } }
public class Weapon : Item { public int Damage { get; set; } }
public class Potion : Item { public int Healing { get; set; } }
```

Setup and use:

```csharp
var mapper = new BsonMapper
{
    IncludeFields = true,    // so Vector3's X/Y/Z fields map
};
mapper.RegisterTypeId(typeof(Weapon), "w");   // compact, rename-proof discriminators
mapper.RegisterTypeId(typeof(Potion), "p");

var db  = new UltraLiteDatabase("saves.db", mapper);
var col = db.GetCollection<SaveGame>("saves");

var save = new SaveGame
{
    Slot = 1,
    PlayerName = "Aria",
    LastPlayedUtc = DateTime.UtcNow,
    Faction = Faction.Alliance,
    Hero = new Character
    {
        Level = 12,
        Stats = { ["str"] = 14, ["dex"] = 18 },
        Position = new Vector3 { X = 10.5f, Y = 0f, Z = -3.25f },
    },
    CompletedQuests = { "intro", "rats-in-cellar" },
    WorldFlags = { ["bridge_repaired"] = true },
    Inventory =
    {
        new Weapon { Name = "Short Sword", Damage = 8 },
        new Potion { Name = "Minor Heal", Healing = 25 },
    },
};

col.Upsert(save);

// Later…
SaveGame loaded = col.FindById(1);
bool firstItemIsWeapon = loaded.Inventory[0] is Weapon;   // true
```

The resulting document (shown as extended JSON) looks roughly like:

```json
{
  "_id": 1,
  "PlayerName": "Aria",
  "LastPlayedUtc": { "$date": "2026-06-24T18:30:00.0000000Z" },
  "Faction": "Alliance",
  "Hero": {
    "Level": 12,
    "Stats": { "str": 14, "dex": 18 },
    "Position": { "X": 10.5, "Y": 0.0, "Z": -3.25 }
  },
  "CompletedQuests": ["intro", "rats-in-cellar"],
  "WorldFlags": { "bridge_repaired": true },
  "Inventory": [
    { "_t": "w", "Name": "Short Sword", "Damage": 8 },
    { "_t": "p", "Name": "Minor Heal", "Healing": 25 }
  ]
}
```

---

## 16. Gotchas and rough edges

A candid list of behaviors that commonly surprise people. Most are deliberate trade‑offs
for size/speed; a couple are sharp corners.

1. **You can't distinguish "absent" from "null."** `SerializeNullValues` is `false` by
   default, so null members aren't written; on read, missing fields leave the property at
   its CLR default. Turn `SerializeNullValues` on if absence vs. null is meaningful.
   (Related: if you opt into `EmptyStringToNull`, blank strings become null and then get
   omitted too — see [§9](#9-mapper-configuration).)

2. **`float` is stored as `double`.** There is no distinct single‑precision BSON type;
   `IsSingle` is literally an alias for `IsDouble`. Values widen to `double` on the wire
   and are converted back when the target property is `float`. Don't rely on exact float
   bit‑equality across a round trip.

3. **Enums serialize as their *name*, not their number.** `Faction.Alliance` is stored as
   the string `"Alliance"` and read back with `Enum.Parse`. Consequences: **renaming an
   enum member breaks old data**, and a numerically‑encoded enum value won't deserialize
   (the reader expects a string). Keep enum member names stable, or register a custom
   serializer if you want integer storage.

4. **`DateTime` is normalized to UTC and truncated to milliseconds.** On write the value
   is converted to UTC and sub‑millisecond ticks are dropped; on read you get a `Utc`‑kind
   `DateTime`. A `Local` or `Unspecified` time will not come back with its original kind or
   offset. (For offset‑aware values, `DateTimeOffset` is supported — it's stored as UTC
   too.)

5. **`ulong` (`UInt64`) is stored via an unchecked bit cast to `Int64`.** Values above
   `long.MaxValue` appear *negative* in the raw storage and in any tool reading the BSON
   directly. They round‑trip correctly through the mapper (unchecked cast back), but
   cross‑tool consumers will see the signed reinterpretation.

6. **Fields aren't mapped by default — only public properties.** Struct‑heavy code (e.g. a
   `Vector3` with public fields) needs `IncludeFields = true`. Non‑public members need
   `IncludeNonPublic = true`.

7. **A parameterless constructor and writable setters are required.** No constructor
   injection: the mapper instantiates the type empty and assigns members. Read‑only
   properties serialize out but are skipped on read.

8. **Document keys are case‑insensitive.** `"Name"` and `"name"` are the same field. Two
   POCO members that differ only in case will collide into one field.

9. **JSON number parsing is narrow.** Bare integers parse as `Int32` (and **overflow**
   beyond its range); bare decimals parse as `Double`. Use `$numberLong` / `$numberDecimal`
   extended JSON to carry `long`/`decimal` losslessly.

10. **Depth is capped at 20.** Circular references and very deep graphs throw
    `UltraLiteException` (document max depth) rather than looping forever. Break cycles or
    `[BsonIgnore]` the back‑reference.

11. **Default polymorphic `_type` is name/assembly‑coupled.** The discriminator is
    `"Namespace.Type, AssemblyName"`. Moving or renaming the type breaks deserialization of
    existing data. Prefer `RegisterTypeId` for stable, compact `_t` discriminators on
    persisted data.

12. **`BsonValue.FromObject` is primitives‑and‑collections only.** It throws
    `InvalidCastException` for POCOs. Reach for the mapper (`ToBsonValue`/`ToDocument`)
    for complex objects.

---

## 17. Cheat sheet

```csharp
// ---- Documents ----
var doc = new BsonDocument { ["Name"] = "Aria", ["Level"] = 7 };
string name = doc.GetStringOrDefault("Name", "?");
int    lvl  = doc.GetInt32OrDefault("Level", 1);

// ---- Bytes (Path A) ----
byte[] bytes = BsonSerializer.Serialize(doc);
BsonDocument back = BsonSerializer.Deserialize(bytes);

// ---- POCO mapping ----
var mapper = BsonMapper.Global;
BsonDocument d = mapper.ToDocument(customer);
Customer c     = mapper.ToObject<Customer>(d);

// ---- Bytes (Path B, direct) ----
byte[] b = mapper.SerializeToBytes(entity);
var e    = mapper.DeserializeFromBytes<MyEntity>(b);

// ---- JSON ----
string json = doc.ToString();                       // compact extended JSON
BsonValue v = JsonSerializer.Deserialize(json);

// ---- Configure mapping ----
mapper.UseCamelCase();
mapper.Entity<Customer>().Id(x => x.Key).Field(x => x.FullName, "name").Ignore(x => x.Temp);
mapper.RegisterType<Color>(c => ToHex(c), b => FromHex(b.AsString));
mapper.RegisterTypeId(typeof(Dog), "dog");
```

| Attribute | Purpose |
|---|---|
| `[BsonId]` / `[BsonId(false)]` | Mark `_id`; optionally disable auto‑id |
| `[BsonField("name")]` | Rename the stored field |
| `[BsonIgnore]` | Exclude the member |
| `[BsonIndex]` | Deprecated — use `col.EnsureIndex(...)` |

---

*This guide documents the serialization system in `UltraLiteDB/Document` and
`UltraLiteDB/Mapper`. For per‑type API details, see the generated API reference.*
