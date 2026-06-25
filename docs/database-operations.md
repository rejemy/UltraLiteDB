# Database & Collections Guide

UltraLiteDB is a small embedded document store: a single file (or stream) holding
collections of BSON documents, with skip‑list indexes, primary‑key lookups, a composable
query API, and ACID transactions at the operation level. It's a trimmed fork of LiteDB 4
aimed at Unity/IL2CPP, so there is **no LINQ, no expression compilation, and no cross‑
collection references** — queries are built from string field names and a `Query` factory.

This guide covers the database and collection API (everything under
`UltraLiteDB/Database`) and the query engine (`UltraLiteDB/Engine/Query`), with a tour of
the backing storage engine, a complexity cheat sheet, and the rough edges worth knowing.

It is the companion to the [BSON Serialization Guide](bson-serialization.md), which covers
how your C# objects become the documents stored here. If you haven't read it, the short
version is: a `BsonMapper` turns POCOs into `BsonDocument`s and back, and that's the unit
this database stores.

```csharp
using UltraLiteDB;
```

---

## Table of contents

1. [Opening a database](#1-opening-a-database)
2. [Collections](#2-collections)
3. [`_id` keys and auto‑id](#3-_id-keys-and-auto-id)
4. [CRUD operations](#4-crud-operations)
5. [Indexes](#5-indexes)
6. [The query system](#6-the-query-system)
7. [Aggregates: Count, Exists, Min/Max](#7-aggregates-count-exists-minmax)
8. [Database management](#8-database-management)
9. [Inside the engine](#9-inside-the-engine)
10. [Complexity cheat sheet](#10-complexity-cheat-sheet)
11. [Rough edges and gotchas](#11-rough-edges-and-gotchas)
12. [Quick reference](#12-quick-reference)

---

## 1. Opening a database

`UltraLiteDatabase` is the entry point. It implements `IDisposable` — always dispose it
(or use `using`) so pending pages are flushed and the file lock released.

```csharp
// File-backed
using (var db = new UltraLiteDatabase("game.db"))
{
    var col = db.GetCollection<SaveGame>("saves");
    // …
}

// Connection string with options
using var db2 = new UltraLiteDatabase("Filename=game.db;Password=secret;Journal=true");

// In-memory (or any Stream)
using var mem = new UltraLiteDatabase(new MemoryStream());
```

Connection‑string keys (all optional except `Filename`):

| Key | Default | Meaning |
|---|---|---|
| `Filename` | — | Path to the data file |
| `Password` | none | AES encryption password for the file |
| `Journal` | `true` | Write‑ahead journaling for crash recovery |
| `CacheSize` | `5000` | Max pages held in memory before a checkpoint flush |
| `Timeout` | `00:01:00` | Lock‑wait timeout |
| `InitialSize` | `0` | Pre‑allocate the file to this many bytes |
| `LimitSize` | `long.MaxValue` | Maximum file size |
| `Async` | `false` | Async disk writes |
| `Flush` | `false` | Force OS flush to disk on write |

You can pass a configured `BsonMapper` to the constructor; otherwise the database uses
`BsonMapper.Global`. The mapper is fixed for the life of the database, so configure it
*before* opening (see the [BSON guide](bson-serialization.md#9-mapper-configuration)).

```csharp
var mapper = new BsonMapper { IncludeFields = true };
mapper.Entity<SaveGame>().Id(s => s.Slot, autoId: false);
using var db = new UltraLiteDatabase("game.db", mapper);

db.Mapper;   // the mapper in use
db.Engine;   // the low-level engine (raw BsonDocument access, lazily initialized)
db.Log;      // diagnostic logger
```

> The engine is **lazily initialized** — the file isn't opened until the first operation
> that needs it. Simply constructing the database and disposing it without touching a
> collection won't necessarily create or open the file.

---

## 2. Collections

A collection is a named bucket of documents. You get a typed view or an untyped
(`BsonDocument`) view:

```csharp
// Typed: documents are mapped to/from SaveGame via the BsonMapper
UltraLiteCollection<SaveGame> saves = db.GetCollection<SaveGame>("saves");

// Name inferred from the type (here: "SaveGame")
UltraLiteCollection<SaveGame> saves2 = db.GetCollection<SaveGame>();

// Untyped: work directly with BsonDocuments
UltraLiteCollection<BsonDocument> raw = db.GetCollection("saves");
```

How the two relate:

- **Typed collections** run every document through the `BsonMapper` — `Insert`/`Update`
  call `ToDocument`, `Find` calls `ToObject<T>`. Your POCO's id member maps to `_id`, and
  the id type drives the auto‑id strategy (see [§3](#3-_id-keys-and-auto-id)).
- **`BsonDocument` collections** skip mapping entirely. You read and write
  `BsonDocument`s directly, and you choose the auto‑id type via the `autoId` parameter of
  `GetCollection(name, autoId)`.

Both are just *views* over the same on‑disk collection — `GetCollection<SaveGame>("saves")`
and `GetCollection("saves")` address the same data. Pick whichever representation suits the
call site.

A few things to know:

- **Collections are created lazily, on first insert.** `GetCollection` never creates
  anything on its own; querying a not‑yet‑created collection just returns empty results.
- **Collection names are case‑insensitive** (`"Saves"` == `"saves"`).
- `GetCollection<T>()` (no name) resolves the name from the type via
  `BsonMapper.ResolveCollectionName` — by default the type's name (or, for a collection
  type, its element type's name). Renaming the class therefore renames the collection.

---

## 3. `_id` keys and auto-id

Every document has a unique primary key in the `_id` field. The `_id` index always exists,
is always unique, and cannot be dropped.

### Which member is the id

For a typed collection, the mapper picks the id member by convention — a property named
`Id` or `<ClassName>Id`, or one marked `[BsonId]`, or one configured with
`.Entity<T>().Id(...)`. See the [BSON guide](bson-serialization.md#mapping-rules) for the
rules. That member is stored as `_id`.

### Auto-generated ids

When you insert a document whose id member is still at its **type default**, the engine
generates one. The strategy is chosen from the id member's CLR type:

| `_id` CLR type | Auto‑id strategy | Generated value |
|---|---|---|
| `int` / `int?` | `Int32` | per‑collection incrementing sequence |
| `long` / `long?` | `Int64` | per‑collection incrementing sequence |
| `Guid` / `Guid?` | `Guid` | `Guid.NewGuid()` |
| anything else (incl. `ObjectId`, `string`) | `ObjectId` | `ObjectId.NewObjectId()` |

For `BsonDocument` collections there's no id member to inspect, so the strategy is the
`autoId` argument of `GetCollection(name, autoId)` — `BsonAutoId.ObjectId` by default.

"Type default" means: `0` for `Int32`/`Int64`, `Guid.Empty` for `Guid`,
`ObjectId.Empty` or null for `ObjectId`. When the id is one of these, the engine treats it
as "please generate one." After a single `Insert(entity)`, the generated id is **written
back** into your object's id property and also returned:

```csharp
var save = new SaveGame { PlayerName = "Aria" };   // Slot == 0 (default)
BsonValue id = saves.Insert(save);                  // engine assigns, returns it
// save.Slot now holds the generated id too
```

To use an explicit id, set it before inserting, or use the `Insert(id, entity)` overload:

```csharp
saves.Insert(42, save);          // explicit _id = 42, no generation
```

> The `Int32`/`Int64` sequence is stored per collection. Inserting an explicit id larger
> than the current sequence bumps the sequence up to it, so a later auto‑id won't collide.
> `_id` may never be `null`, `MinValue`, or `MaxValue` — those throw `InvalidDataType`.

---

## 4. CRUD operations

All examples use a typed collection `var col = db.GetCollection<SaveGame>("saves");`.
Every write runs in its own transaction (see [§9](#9-inside-the-engine)).

### Create

```csharp
BsonValue id = col.Insert(save);            // returns/back-fills the _id
col.Insert(99, save);                        // explicit _id
int n = col.Insert(manySaves);              // batch insert, returns count
int m = col.InsertBulk(hugeSequence);       // batched transactions (default 5000/batch)
```

`Insert` of a single entity assigns and back‑fills an auto‑id when needed. `InsertBulk`
commits in batches to bound memory on very large loads — prefer it for tens of thousands of
documents. A duplicate `_id` (or a duplicate key in any **unique** index) throws and rolls
the transaction back.

### Read

```csharp
SaveGame one  = col.FindById(42);                       // by _id, default(T) if missing
SaveGame first = col.FindOne(Query.EQ("PlayerName", "Aria"));
IEnumerable<SaveGame> all = col.FindAll();              // ordered by _id

// Query with pagination — results stream lazily
foreach (var s in col.Find(Query.GTE("Level", 10), skip: 0, limit: 20))
    Process(s);
```

`Find` returns a **lazily** enumerated sequence: documents are read from disk and
deserialized as you iterate, in batches. Don't hold the enumerator open across other writes
to the same collection. See [§6](#6-the-query-system) for building queries.

### Update

```csharp
bool ok = col.Update(save);            // matches on save's _id; false if _id not found
col.Update(42, save);                   // update the doc with _id = 42
int count = col.Update(manySaves);     // returns number actually updated
```

`Update` matches by `_id` and replaces the whole document — there are no partial/field
updates. It returns `false` (or a lower count) for ids that don't exist; **it does not
insert.** If you want insert‑or‑replace semantics, use `Upsert`.

### Upsert

```csharp
bool inserted = col.Upsert(save);          // true = inserted, false = updated
int insertedCount = col.Upsert(manySaves); // number inserted (not updated)
col.Upsert(42, save);                       // explicit _id
int x = col.UpsertBulk(hugeSequence);      // batched
```

### Delete

```csharp
bool removed = col.Delete(42);                       // by _id
int deleted = col.Delete(Query.LT("Level", 5));      // by query, returns count
```

Deleting removes the document's data and every index node that pointed at it.

---

## 5. Indexes

Indexes make queries fast. Without one, a query on a field falls back to a **full scan**
that deserializes every document (it still works — it's just O(n); see
[§11](#11-rough-edges-and-gotchas)).

```csharp
col.EnsureIndex("PlayerName");                 // non-unique
col.EnsureIndex("Email", unique: true);        // unique — rejects duplicate values

foreach (IndexInfo ix in col.GetIndexes())     // includes the _id PK
    Console.WriteLine($"{ix.Field} unique={ix.Unique}");

col.DropIndex("PlayerName");
```

Key facts:

- **`EnsureIndex` is idempotent and backfills.** Creating an index reads every existing
  document to populate it — an O(n log n) one‑time cost. Create indexes early, before the
  collection grows large. It returns `false` if an index on that field already exists.
- **The `_id` index always exists**, is unique, and can't be dropped. `EnsureIndex("_id")`
  is a no‑op returning `false`.
- **16 index slots per collection**, one taken by the `_id` PK → **up to 15 secondary
  indexes**. Exceeding that throws `IndexLimitExceeded`.
- **Index keys are capped at 512 bytes.** A longer key value throws `IndexKeyTooLong`.
- **Unique indexes** reject duplicate keys at insert/update time (throws
  `IndexDuplicateKey`, rolling back the transaction).
- Indexes are maintained automatically on every insert, update, and delete.
- Index field names are matched **case‑sensitively** — `EnsureIndex("name")` will *not* be
  used by `Query.EQ("Name", …)`. (Note the asymmetry: collection names and `BsonDocument`
  keys are case‑*insensitive*, but index field lookup is case‑sensitive.)

> **Top‑level fields only.** Despite some doc comments mentioning dot notation
> (`"Address.City"`), this fork resolves only a single top‑level field name — nested paths
> do **not** work. See [§11](#11-rough-edges-and-gotchas). This is by design for the Unity
> build, which dropped the expression engine.

---

## 6. The query system

Queries are built with static factory methods on `Query`, then passed to `Find`, `Delete`,
`Count`, or `Exists`. There's no LINQ — you name fields as strings.

### The building blocks

| Factory | Meaning | Strategy* |
|---|---|---|
| `Query.All(order)` | every document, ordered by an index | Scan |
| `Query.EQ(field, value)` | `field == value` | **Seek** |
| `Query.LT/LTE/GT/GTE(field, value)` | `<`, `<=`, `>`, `>=` | Seek + Scan |
| `Query.Between(field, a, b)` | range (inclusive bounds configurable) | Seek + Scan |
| `Query.StartsWith(field, prefix)` | string prefix | **Seek** then forward walk |
| `Query.Contains(field, substr)` | substring | Scan (no seek possible) |
| `Query.In(field, values…)` | matches any value in a set | Seek per value |
| `Query.Not(field, value)` | `field != value` | Scan |
| `Query.Not(query)` | negation of another query | Scan |
| `Query.Where(field, predicate)` | `Func<BsonValue,bool>` over index keys | Scan |
| `Query.And(a, b)` / `And(params)` | intersection | see below |
| `Query.Or(a, b)` / `Or(params)` | union | see below |

\* *Strategy* names match what the engine prints in `query.ToString()` and the log:
**Seek** = jump straight to matching index nodes (≈ O(log n)); **Scan** = walk a run of
index nodes (works on the compact index keys, no document deserialization); **Filter** =
full scan that deserializes every document because no usable index exists.

```csharp
// Simple
col.Find(Query.EQ("PlayerName", "Aria"));
col.Find(Query.Between("Level", 10, 20));
col.Find(Query.StartsWith("PlayerName", "Ar"));
col.Find(Query.In("Faction", "Alliance", "Horde"));

// Composite
col.Find(Query.And(Query.GTE("Level", 10), Query.EQ("Faction", "Alliance")));
col.Find(Query.Or(Query.EQ("Faction", "Alliance"), Query.GT("Level", 50)));

// Predicate over index keys (no document deserialization)
col.Find(Query.Where("Level", v => v.AsInt32 % 2 == 0));
```

### How execution is chosen

When a query runs, the engine looks for an index on its field:

- **Index present** → it executes against the index. `EQ`, `StartsWith`, and `Between`
  *seek*; `Contains`, `Where`, `Not`, and friends *scan* the index range. Either way the
  engine walks compact index keys and only deserializes the documents it actually returns.
- **No index** → the query is marked **Filter**: the engine walks the `_id` index, reads and
  deserializes *every* document, and tests your condition in memory. Correct, but O(n).

`And` and `Or` compose these:

- **`And`** gives the **left** side index preference; the right side is forced to filter
  the candidates the left produced. As a special case, `GT`/`GTE` and `LT`/`LTE` on the
  **same field** are automatically folded into a single `Between` seek. If neither side has
  an index, the two filtered sets are intersected. *Put the more selective, indexed
  condition on the left.*
- **`Or`** unions both sides (deduplicating by document). It can only stay index‑only if
  *both* sides use an index; if either side filters, the union does too.

### skip / limit

`Find(query, skip, limit)` paginates. The optimization that matters: when the query runs
**purely on an index** (Seek/Scan, no Filter), skip and limit are applied *before*
documents are read — skipped documents are never deserialized. In a **Filter** query the
documents must be deserialized and tested first, so skip/limit only bounds the output, not
the work. Another reason to index the fields you paginate on.

### Result ordering

Results come back in the traversal order of the index that drove the query (ascending by
default; `Query.All(field, Query.Descending)` and the `order` parameters reverse it).
There is no general multi‑field `ORDER BY` — order follows the single index in play.

---

## 7. Aggregates: Count, Exists, Min/Max

```csharp
int total   = col.Count();                          // O(1): stored document count
int matches = col.Count(Query.EQ("Faction", "Horde"));
long big    = col.LongCount();                       // 64-bit variants
bool any    = col.Exists(Query.GT("Level", 90));     // short-circuits at first match

BsonValue lo = col.Min("Level");   // requires an index on "Level"
BsonValue hi = col.Max("Level");
```

- `Count()` / `LongCount()` with no query return the collection's **stored** document count
  in O(1) — they don't walk anything.
- `Count(query)` and `Exists(query)` run the query but avoid deserializing documents when
  the query is index‑only (`Exists` also stops at the first hit). A `Filter` query still
  has to deserialize to test the condition.
- `Min`/`Max` read the first/last node of the field's index in O(1). **They require an
  index** on the field — with none (or an empty collection) they return the sentinel
  `BsonValue.MinValue` / `BsonValue.MaxValue` rather than throwing, which is easy to
  misread as a real value.

---

## 8. Database management

```csharp
db.GetCollectionNames();              // IEnumerable<string>
db.CollectionExists("saves");
db.RenameCollection("saves", "slots");
db.DropCollection("saves");           // drops all data + indexes

long bytesFreed = db.Shrink();        // compact the file, reclaim unused space
long bytesFreed2 = db.Shrink("newPassword");  // compact and re-key encryption

db.Engine.UserVersion = 3;            // a ushort you can use for schema versioning
```

`Shrink` rewrites the database to remove fragmentation left by deletes and updates. For a
file database it builds a temp file and swaps; for a stream database it uses a
`MemoryStream`. It can also change the encryption password in the same pass. Run it
occasionally after heavy churn, not on every close.

---

## 9. Inside the engine

You don't need this to use the database, but it explains the performance characteristics
and the constraints. `UltraLiteDatabase` is a thin POCO‑mapping layer over
`UltraLiteEngine`, which works purely in `BsonDocument`s.

**Paged storage.** The file is a sequence of fixed **4 KB pages**. Page types: a single
*Header* page; *Collection* pages (one per collection, holding metadata and the 16 index
slots); *Index* pages (skip‑list nodes); *Data* pages (serialized BSON); *Extend* pages
(overflow for documents larger than a page); and *Empty* pages (a free list for reuse). A
document lives as BSON bytes in a **DataBlock**; each index node points at the DataBlock of
the document it indexes.

**Skip‑list indexes.** Every index — including the `_id` PK — is a probabilistic skip list
(up to 32 levels). That's what gives `EQ`/`FindById` their ≈ O(log n) seek and lets ranges,
`Min`/`Max`, and ordered traversal be cheap. Node levels are chosen by a coin flip on
insert; there's no rebalancing pass.

**Services.** The engine wires together a `PageService` (allocates/reads/writes pages), a
`CacheService` (in‑memory page cache), an `IndexService` (the skip list), a `DataService`
(document bytes ↔ data pages), a `CollectionService`, and a `TransactionService`.

**Transactions & durability.** Every write operation is wrapped in a transaction: on
success the dirty pages are persisted and a checkpoint runs; on any exception the dirty
pages are discarded, rolling the operation back so the file stays consistent. With
journaling enabled (the default), changes are written to a write‑ahead journal first, and
if the process crashes mid‑write the journal is **replayed on next open** to recover. This
is the "ACID at the document/operation level" the README refers to. The page cache is
bounded by `CacheSize`; when it fills, a checkpoint flushes (only strictly enforced when
journaling is on).

**Encryption.** Supplying a password encrypts data pages with AES; the header stores a
salt and a SHA‑1 password hash so the wrong password is rejected on open.

**Single‑threaded by design.** This fork removed LiteDB's thread/file‑locking machinery to
shrink the footprint for Unity. **Access a database instance from one thread at a time**,
and don't open the same file from multiple processes. There's no shared/concurrent access
model here.

**LiteDB compatibility.** The on‑disk format is compatible with LiteDB 4, so files can be
inspected with LiteDB tooling — but the query/index features here are the top‑level subset
(no expressions).

---

## 10. Complexity cheat sheet

Let **n** = documents in the collection, **k** = documents matched/returned, **m** =
number of secondary indexes on the collection, **d** = average document size. "Seek" costs
are skip‑list operations (≈ O(log n)); deserialization is O(d) per document returned.

| Operation | Cost | Notes |
|---|---|---|
| `Insert` (one) | ≈ O((1 + m)·log n + d) | PK + each secondary index node, plus serialize |
| `InsertBulk` | ≈ above × count | batched transactions bound memory |
| `Update` (one) | ≈ O(log n + d + m·log n) | seek by `_id`, reserialize, reconcile indexes |
| `Delete` (one by id) | ≈ O(log n + m·log n) | seek + unlink each index node |
| `FindById` / `EQ` (indexed) | ≈ O(log n + k·d) | seek, then read matches |
| `EQ`/`Find` (no index) | **O(n·d)** | Filter: deserializes every document |
| `Between`/`GT`/`LT`/`StartsWith` (indexed) | ≈ O(log n + k·d) | seek to start, walk the run |
| `Contains` / `Where` / `Not` (indexed) | O(n) keys + O(k·d) | scans **all** index keys (cheap per key), reads matches |
| `In` (indexed) | ≈ O(values · log n + k·d) | a seek per value |
| `Count()` (no query) | **O(1)** | stored counter |
| `Count(query)` / `Exists(query)` | query cost, no deserialize if index‑only | `Exists` stops at first match |
| `Min` / `Max` (indexed field) | **O(1)** | head/tail of the skip list |
| `EnsureIndex` | **O(n·log n + n·d)** | one‑time backfill reads every document |
| `DropIndex` / `DropCollection` | O(pages) | frees index/data pages |
| `Shrink` | O(file) | rewrites the whole database |

Practical takeaways: index the fields you filter, paginate, or take `Min`/`Max` on; keep
`Contains`/`Where`/`Not` for cases where a linear key scan is acceptable; lean on
`FindById` and `EQ` for the hot paths; and create indexes before bulk‑loading where you can.

---

## 11. Rough edges and gotchas

1. **Dot‑notation / nested fields don't work.** Some XML comments mention indexing
   `"Address.City"`, but the field resolver does a single top‑level `doc[field]` lookup —
   it never splits on `.`. A query or index on a nested path silently matches nothing (or
   looks for a literal top‑level key that happens to contain a dot). Queries and indexes
   are **top‑level fields only**. Flatten the fields you need to query, or store a derived
   top‑level copy.

2. **Querying a non‑indexed field silently does a full scan.** There's no error or warning
   — the query just becomes a `Filter` that deserializes every document (O(n)). On a large
   collection this is the usual cause of "why is this slow?" Check `query.ToString()` (or
   the log): `Seek`/`Scan` means indexed, `Filter` means full scan.

3. **Index field names are case‑sensitive.** `EnsureIndex("name")` then
   `Query.EQ("Name", …)` will *not* use the index (it full‑scans). This is surprising given
   that collection names and document keys are case‑*insensitive*. Match the exact field
   name you indexed.

4. **`EnsureIndex` won't change an existing index.** If an index on the field already
   exists, `EnsureIndex` returns `false` and ignores any difference — including a different
   `unique` flag. To change uniqueness you must `DropIndex` then `EnsureIndex` again.

5. **15 secondary indexes max** (16 slots − the `_id` PK). The 16th throws
   `IndexLimitExceeded`. And **index keys are capped at 512 bytes** — long string keys
   throw `IndexKeyTooLong`.

6. **`Update` does not insert.** A non‑matching `_id` just returns `false`/0. Use `Upsert`
   for insert‑or‑replace. Also, updates replace the **entire** document — there are no
   partial field updates.

7. **Array fields aren't expanded into multi‑key indexes.** An indexed field holding a
   `BsonArray` is stored as a single composite key, not one index entry per element, so
   `Query.EQ` won't match individual elements of the array. (Some internal comments mention
   "multi‑key support," but the field resolver yields a single value.)

8. **`Min`/`Max` return sentinels, not null.** With no index on the field, or an empty
   collection, you get `BsonValue.MinValue` / `BsonValue.MaxValue` back — not an error and
   not null. Guard for it.

9. **Single‑threaded, single‑process.** No internal locking: use one database instance from
   one thread, and don't open the same file from multiple processes. Concurrency is the
   caller's responsibility.

10. **`GetCollection` doesn't create the collection.** It's created on the first insert.
    Querying a never‑written collection returns empty / `Count() == 0`, which can look like
    data loss if you expected creation on access.

11. **`GetCollection<T>()` ties the collection name to the class name.** Renaming the type
    (without an explicit name) points you at a different, empty collection. Pass an explicit
    name if the class might be renamed/refactored.

12. **Auto‑id back‑fill only happens for "empty" ids.** If your id member holds any
    non‑default value, the engine uses it verbatim — even `1` is "explicit." Mixing manual
    and auto integer ids is fine (the sequence jumps past manual ids), but be deliberate.

13. **Lazy enumeration interleaved with writes.** `Find` streams results as you iterate.
    Modifying the same collection while a `Find` enumerator is open (without materializing
    first, e.g. `.ToList()`) is asking for trouble — buffer the results if you need to write
    during iteration.

---

## 12. Quick reference

```csharp
// Open / collections
using var db = new UltraLiteDatabase("game.db");
var col = db.GetCollection<SaveGame>("saves");      // typed
var raw = db.GetCollection("saves");                 // BsonDocument

// CRUD
BsonValue id = col.Insert(save);                     // auto-id, back-filled
col.InsertBulk(manySaves);
bool updated  = col.Update(save);
bool inserted = col.Upsert(save);                    // true=insert, false=update
col.Delete(id);
col.Delete(Query.LT("Level", 5));

// Read
var one  = col.FindById(id);
var some = col.Find(Query.GTE("Level", 10), skip: 0, limit: 20);
var all  = col.FindAll();

// Indexes
col.EnsureIndex("PlayerName");
col.EnsureIndex("Email", unique: true);
col.DropIndex("PlayerName");

// Aggregates
int  total = col.Count();                            // O(1)
bool any   = col.Exists(Query.EQ("Faction", "Horde"));
var  hi    = col.Max("Level");                       // needs an index

// Management
db.GetCollectionNames();
db.RenameCollection("saves", "slots");
db.DropCollection("slots");
db.Shrink();
```

| Query | Builds |
|---|---|
| `Query.All()` | everything, by `_id` |
| `Query.EQ/LT/LTE/GT/GTE(field, v)` | comparisons (EQ seeks) |
| `Query.Between(field, a, b)` | range seek |
| `Query.StartsWith / Contains(field, s)` | string prefix / substring |
| `Query.In(field, v…)` | set membership |
| `Query.Not(field, v)` / `Query.Not(query)` | inequality / negation |
| `Query.Where(field, predicate)` | predicate over index keys |
| `Query.And(...)` / `Query.Or(...)` | intersection / union |

---

*This guide documents the database and query layers under `UltraLiteDB/Database` and
`UltraLiteDB/Engine`. For how C# objects become the stored documents, see the
[BSON Serialization Guide](bson-serialization.md). For per‑type API details, see the
generated API reference.*
