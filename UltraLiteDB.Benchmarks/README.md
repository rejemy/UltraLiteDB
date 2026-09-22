# UltraLiteDB.Benchmarks

Performance benchmarks for `BsonMapper`, focused on the direct POCO ⇄ BSON paths
(`SerializeToBytes`/`SerializeToStream` in `BsonMapper.DirectSerialize.cs`,
`DeserializeFromBytes`/`DeserializeFromStream` in `BsonMapper.DirectDeserialize.cs`).
The `ToDocument`/`ToObject` path and System.Text.Json are measured alongside for reference.

UltraLiteDB's real target is **Unity IL2CPP**. The same benchmark code runs on three runtimes:

- **JIT (CoreCLR)**: the normal `dotnet run` runtime. Seconds to run, good for iterating.
- **NativeAOT**: a quick local stand-in for IL2CPP. See [How NativeAOT relates to IL2CPP](#how-nativeaot-relates-to-il2cpp).
- **Unity IL2CPP**: a real IL2CPP player built from `../UnityTest` and run headless. It takes minutes and
  needs Unity; see [Unity IL2CPP](#unity-il2cpp). This is the number that matters.

## Running

```bash
./run.sh                                  # serializer suite, JIT then NativeAOT
./run.sh jit                              # JIT only (fast to iterate)
./run.sh aot accessors                    # accessor suite, NativeAOT only
./run.sh both all --seconds 5             # everything, longer runs for steadier numbers
./run.sh jit --filter deserialize         # only benchmarks whose name contains "deserialize"
./run.sh il2cpp all                       # build a Unity IL2CPP player and run everything in it
```

Arguments after the runtime choice go to the benchmark program:
`[serializer|accessors|all] [--seconds N] [--filter text]`. `--seconds` is the timed duration per
benchmark (default 3), split over 5 rounds. Without the script:

```bash
dotnet run -c Release -- all
dotnet publish -c Release -p:Aot=true -o bin/aot && ./bin/aot/UltraLiteDB.Benchmarks all
```

Use `-p:Aot=true`, not `-p:PublishAot=true`. A global `PublishAot` also flows to the netstandard2.1
library, which then fails to build. NativeAOT needs the platform's native toolchain (Xcode command line
tools on macOS). The ILCompiler package is restored from NuGet on first use.

## Unity IL2CPP

`./run.sh il2cpp [serializer|accessors|all] [--seconds N] [--filter text] [--no-build]` (or `./run-il2cpp.sh`
directly) does the following:

1. Builds `UltraLiteDB.dll` (Release) and copies it to `UnityTest/Assets/Plugins/UltraLiteDB/`, the same
   way a Unity user installs the library.
2. Copies the shared suites (every `*.cs` here except `Program.cs`) and `Unity/*.cs` to
   `UnityTest/Assets/UltraLiteBench/`. It uses a checksum `rsync`, so unchanged files don't trigger a reimport.
3. Builds a macOS player headless with `unity build --target StandaloneOSX`, using the project's own
   player settings (IL2CPP, release, default stripping), to `UnityTest/builds/UltraLiteBench.app`.
4. Runs the player with `-batchmode -nographics`. `Unity/BenchmarkRunner.cs` runs the suites at startup,
   streams results to `bin/il2cpp/results.txt` and the player log, and quits.

`--no-build` reruns the existing player (it takes seconds rather than minutes). `--dll <path>` builds the
player with a different `UltraLiteDB.dll`, for before/after comparisons against another commit:

```bash
git worktree add --detach /tmp/ulite-base <commit>
dotnet build /tmp/ulite-base/UltraLiteDB/UltraLiteDB.csproj -c Release
./run.sh il2cpp all --dll /tmp/ulite-base/UltraLiteDB/bin/Release/netstandard2.1/UltraLiteDB.dll
cp bin/il2cpp/results.txt bin/il2cpp-baseline.txt       # results.txt is overwritten by every run
git worktree remove --force /tmp/ulite-base
./run.sh il2cpp all                                      # rebuild with the current library afterwards
```

Afterwards, rebuild without `--dll` so UnityTest and the player don't keep the old library. Logs are in
`bin/il2cpp/`: `build.log` for the Editor build, `player.log` for the player, and `library-build.log`.
The first build takes several minutes (IL2CPP converts and compiles the whole player); later builds are incremental.

**Profiling:** `./run-il2cpp.sh --profile "<benchmark name>" [--no-build]` runs that one benchmark in the player,
samples it for 8 seconds with macOS `sample`, and prints the functions with the most self time. Idle waits
on other threads are filtered out, and the full report goes to `bin/il2cpp/profile.txt`. Managed methods
show up under their IL2CPP names (`DirectBsonReader_FindMember`, ...), and IL2CPP runtime work under
`il2cpp::vm::...`. This is how the IL2CPP-specific costs in [Remaining costs and ideas](#remaining-costs-and-ideas)
were found; reading IL2CPP numbers without a profile is mostly guesswork.

**Requirements:** the Unity CLI (`unity`), the Editor version in `UnityTest/ProjectSettings/ProjectVersion.txt`
with Mac build support, an active license (`unity license status`), and **no Editor open on UnityTest**,
because the batch-mode build needs the project lock. The script checks for an open Editor and stops if it finds one.

**What differs from the .NET runs:**
- The Unity run adds a reference suite for **`JsonUtility`**, Unity's native C++ JSON serializer, on the
  fields model (`Unity/JsonUtilitySuite.cs`). It's not an exact equivalent: `JsonUtility` skips the
  `Dictionary`, `DateTime` and `Guid` fields and writes enums as numbers, so it serializes less data than
  BSON does. There are rows for plain string and for UTF-8 bytes, since a save file needs bytes.
- There is no System.Text.Json reference.
- Allocation measurement: IL2CPP doesn't implement `GC.GetAllocatedBytesForCurrentThread` (it returns 0;
  the runner checks at startup). So each benchmark's allocations are measured in a separate pass of 256
  calls with the GC disabled, using `GC.GetTotalMemory`. That's approximate but lines up well: a boxed
  `int` shows as the expected 32 B (IL2CPP's 16-byte object header plus Boehm's 16-byte granules, vs 24 B
  on CoreCLR). The header line of the results says which method was used.

**Source layout:** all benchmark code lives in this folder. `Unity/` holds the Unity-only files and is
excluded from the console project (`<Compile Remove="Unity/**" />`). The copies in UnityTest are
gitignored (`UnityTest/.gitignore`), so the committed UnityTest stays an empty shell that opens cleanly
before anything is synced. Shared files must compile under Unity's C# and netstandard2.1 profile: keep
.NET 5+ APIs behind `#if NET5_0_OR_GREATER`, as `SerializerSuite.cs` does.

## Suites

**`serializer`** (default): end-to-end serialize/deserialize of a ~2.8 KB game-save-like object
(`Models.cs`: scalars of every common type, a nested object, a `List` of 20 small objects, an `int[50]`,
a `Dictionary<string, int>`, enums). It runs twice: once with properties (default mapping) and once with
public fields (`IncludeFields = true`, which is what Unity's `JsonUtility` serializes). Before timing, it
checks that the object round-trips and that the direct output is byte-identical to the
`ToDocument` + `BsonWriter` output. It throws if not, because numbers from a broken serializer are meaningless.

**`accessors`**: per-member cost of the ways to read and write a property without runtime code generation.
The `library ... (current)` rows measure whatever `Reflection.CreateGenericGetter/Setter` currently
produce, so they track changes to the mapper. The other rows are candidate strategies:
`MethodInfo.Invoke`, backing-field `FieldInfo` access, and typed delegates (see [Ideas](#remaining-costs-and-ideas)).

## Reading the output

```
  serialize: SerializeToBytes<T>, reused ByteWriter        3.12 us/op      2,992 B/op
```

- **Time**: median of 5 rounds after warmup, per operation (or per member in the accessor suite).
- **Bytes**: bytes allocated on the calling thread per operation (`GC.GetAllocatedBytesForCurrentThread`).
  `SerializeToBytes<T>` includes its ~2.8 KB result array; the *reused ByteWriter* row is pure garbage.
- Only compare numbers from the **same machine and session**, and run twice if a result looks surprising.
  Allocation counts are deterministic; times vary a few percent between runs.

A small hand-rolled harness (`Harness.cs`) is used instead of BenchmarkDotNet so the identical code runs
under JIT and NativeAOT with no extra dependencies.

## How NativeAOT relates to IL2CPP

**What carries over:**
- Both are ahead-of-time compiled with no JIT, so `Reflection.Emit` and `Expression.Compile` are unavailable.
  The library must never depend on them.
- Reflection invoke runs without JIT-generated stubs.
- Both strip code that is only reached through reflection. NativeAOT needs `[DynamicDependency]` (see
  `SerializerSuite.Run`), and Unity needs `link.xml`. For example, a `List<Item>` constructor that is only
  called through `Activator.CreateInstance` gets trimmed.
- Creating generic instantiations over value types at runtime (`MakeGenericType`) is restricted.
- Allocations made by the library code itself are the same on both.

**What doesn't** (measured with the Unity 6000.3 IL2CPP run, same machine):
- IL2CPP was **5–6x slower** than NativeAOT on the serializer benchmarks until its specific costs were
  profiled and removed; it's now about 2x (serialize 9.4 µs vs 4.7 µs). Much of the gap was costs that
  NativeAOT simply doesn't have (next bullets, and the pitfalls under [Remaining costs](#remaining-costs-and-ideas)).
- Reflection is **~10x slower**: `FieldInfo.GetValue` is ~100 ns vs 10 ns, `MethodInfo.Invoke` ~180 ns vs 14 ns,
  and `Activator.CreateInstance` 150 ns vs 33 ns. Reflection-heavy code looks much better on NativeAOT than on device.
- `typeof(X)`, `GetType()` and the generic span helpers are cheap on NativeAOT but locked hash lookups on IL2CPP.
- Allocation is more expensive (Boehm GC) and objects are bigger: a boxed `int` is 32 B vs 24 B.
- Runtime generic instantiation differs. `MakeGenericType` over a value type **works on IL2CPP** (full
  generic sharing). **NativeAOT** only has the instantiations it precompiled, so there the library's typed
  accessors work for some members (those matching its probe's instantiations, e.g. `int` properties) and fall
  back to reflection for the rest. The `accessors` suite's MakeGenericType rows only mean something on IL2CPP.
- Unity's BCL is Mono-based, so allocations and costs *inside* BCL calls (`Enum.ToString`, `Encoding`, ...)
  differ, and `GC.GetAllocatedBytesForCurrentThread` isn't implemented.
- Treat NativeAOT as the fast inner loop for allocation work and relative comparisons.
  **Final validation of any performance change belongs in the IL2CPP run (`./run.sh il2cpp`).**

## Workflow for performance changes

1. Run `./run.sh both all` before changing anything, to get a same-machine baseline.
2. Make the change and run the test suite (`cd ../UltraLiteDB.Tests && dotnet test`). The direct-path tests
   assert byte-equivalence with the `BsonDocument` path, which is the safety net for serializer changes.
3. Run the benchmarks again and compare. Iterate with JIT/NativeAOT, which take seconds.
4. Confirm on IL2CPP (`./run.sh il2cpp all`, a few minutes), with a `--dll` baseline run if the "before"
   numbers aren't already in the history. IL2CPP can rank changes differently: reflection costs ~9x more there.
5. Add notable before/after numbers to [Results history](#results-history).

## Adding a benchmark

- New scenario on the existing model: add a `Bench("group: description", () => ...)` line in
  `SerializerSuite.Run`. Prefix the name with a group word (`serialize:`, `deserialize:`, ...) so
  `--filter` can select it.
- New model: add it and a factory method to `Models.cs`, and extend `VerifyRoundTrip`. If deserializing it
  requires creating a collection type through reflection (any `List<T>`/`Dictionary<K,V>` member), add a
  `[DynamicDependency]` for that type on `SerializerSuite.Run`, or the NativeAOT run fails with
  "No parameterless constructor defined".
- New suite: add a `XxxSuite.Run(double seconds, Func<string, bool> include)` and a case in `Program.cs`.

## Remaining costs and ideas

State after the 2026-09-21 pass, IL2CPP, per call on the ~2.8 KB model:

- **Serialize** (9.4 µs with a reused `ByteWriter`, ~1 KB of garbage): what's left of the garbage is boxing
  of the 21 enum members and the 10 `Dictionary<string, int>` values. An unboxed enum path would need the
  numeric value without boxing, which takes `Unsafe.As` (not in netstandard2.1) or per-enum typed code.
- **Deserialize** (15.1 µs, ~5.9 KB, of which ~4.3 KB is the object graph itself): object creation through
  `Activator.CreateInstance` costs ~150 ns per object vs ~34 ns for `new`, about 3.5 µs for the ~24 objects
  per document. A generic `new T()` factory built with MakeGenericMethod barely helps (131 ns), because
  IL2CPP implements `new T()` for shared generics the same way. Profile further with `--profile`.
- **Public-fields models** (`IncludeFields = true`) don't benefit from typed accessors. Without code
  generation, fields can only be reached through `FieldInfo` (~100 ns per access on IL2CPP), so they
  serialize at 25 µs and deserialize at 30 µs, vs 9.4 / 15 µs for properties. Auto-properties are the fast
  path for data classes.
- **Roslyn source generator**: generates the accessors (or whole per-type serializers) at compile time.
  It's fully AOT-safe, needs no runtime generic instantiation, and would cover fields and enums too; this
  is how MessagePack-CSharp and System.Text.Json handle AOT. It's a bigger project, and Unity supports
  source generators from 2021.2+.

**BsonDocument codec** (`BsonWriter`/`BsonReader`, what the database engine runs on every insert, update
and read). After the 2026-09-21 codec pass, `BsonWriter.Serialize` is close to the direct writer (8 µs on
IL2CPP). `BsonReader.Deserialize` (30 µs) is now dominated by the `BsonDocument` object model rather than
parsing: about 40% of its samples are GC and allocation (a `BsonValue` plus a boxed value per field, a
dictionary per document), and about 25% are case-insensitive key hashing. `BsonDocument` uses
`StringComparer.OrdinalIgnoreCase`, and in Unity's BCL that hash uppercases a copy of every key. A custom
comparer with exactly `OrdinalIgnoreCase` semantics would remove the latter, but Unicode casing edge cases
make it delicate. The bigger lever for typed collections is skipping `BsonDocument` entirely: index-based
finds could hand stored bytes to `DeserializeFromBytes<T>` (15 µs vs ~100 µs today for `BsonReader` + `ToObject`).

**IL2CPP pitfalls found by profiling.** Avoid these in hot paths; each was a measurable share of time:
- **`typeof(X)` and `obj.GetType()`** go through `il2cpp::vm::Reflection::GetTypeObject`, a hash lookup
  behind a mutex, on every call. Cache `Type`s in `static readonly` fields, and compare an object's type
  with `Type.GetTypeHandle(obj)` against a cached `RuntimeTypeHandle` (a pointer read).
- **Generic span helpers** in Unity's BCL (`MemoryExtensions.IndexOf`/`SequenceEqual`, `Span<T>.CopyTo`)
  do `typeof(T)` checks inside, so they pay that lookup per call. For short keys a plain loop over the
  `byte[]` is far faster. They're vectorized and fast on CoreCLR, which is why this doesn't show up there.
- **`UTF8Encoding.GetBytes`** has noticeable per-call overhead in Unity's BCL. An ASCII fast path (copy
  chars below 0x80 directly, and hand the rest to the encoder) pays off for keys and typical game strings.
- **`Array.CreateInstance`, `Activator.CreateInstance`, reflection invoke**: ~150–190 ns each (see above).
- On NativeAOT (not IL2CPP), **`Delegate.Target`** is computed through thunk checks. Resolve it once, not
  per member (the library caches it in `MemberMapper.PrimitiveGetter/PrimitiveSetter`).

## Results history

Apple M2 Max, macOS 26.6, `--seconds 3`. Time per operation, then bytes allocated per operation.

### Unity IL2CPP

Unity 6000.3.24f1, macOS arm64 player, IL2CPP release, default stripping. Allocations are approximate
(see [Unity IL2CPP](#unity-il2cpp)). Columns: `839eac0` (before any of this work), `2729010` (the
direct-path pass), and the typed-accessor pass (typed accessors, unboxed primitive paths, IL2CPP hot-path fixes).

| Benchmark | `839eac0` | `2729010` | typed accessors |
|---|---|---|---|
| `SerializeToBytes<T>` | 66.54 µs · 47,296 B | 25.69 µs · 7,968 B | 10.49 µs · 5,120 B |
| `SerializeToBytes<T>`, reused `ByteWriter` | 60.77 µs · 28,560 B | 24.37 µs · 3,856 B | 9.36 µs · 1,024 B |
| `SerializeToStream<T>` | 63.18 µs · 28,736 B | 24.47 µs · 3,856 B | 9.40 µs · 1,024 B |
| `DeserializeFromBytes<T>` | 100.51 µs · 31,264 B | 51.59 µs · 8,880 B | 15.13 µs · 5,904 B |
| `DeserializeFromStream<T>` | 104.77 µs · 31,504 B | 52.28 µs · 8,976 B | 15.21 µs · 6,000 B |
| `SerializeToBytes<T>` (fields) | 56.13 µs · 40,608 B | 23.96 µs · 7,936 B | 24.83 µs · 7,984 B |
| `DeserializeFromBytes<T>` (fields) | 87.21 µs · 24,624 B | 50.24 µs · 8,864 B | 30.08 µs · 8,800 B |
| `ToDocument` + `BsonWriter` (reference) | 132.84 µs · 73,664 B | 122.82 µs · 67,088 B | 109.30 µs · 67,072 B |
| `BsonReader` + `ToObject` (reference) | 143.91 µs · 59,312 B | 131.90 µs · 52,800 B | 115.86 µs · 52,688 B |
| `JsonUtility.ToJson` (reference) | 17.52 µs · 8,192 B | 17.38 µs · 8,192 B | 16.96 µs · 8,192 B |
| `JsonUtility.ToJson` + UTF-8 bytes (reference) | 19.08 µs · 12,288 B | 19.02 µs · 12,288 B | 17.98 µs · 12,288 B |
| `JsonUtility.FromJson` (reference) | 14.07 µs · 3,360 B | 14.22 µs · 3,376 B | 13.67 µs · 3,344 B |
| UTF-8 bytes + `JsonUtility.FromJson` (reference) | 16.31 µs · 11,552 B | 16.49 µs · 11,568 B | 16.44 µs · 11,536 B |
| Library getter, per member | 190.1 ns · 80 B | 92.2 ns · 32 B | 34.8 ns · 32 B (0 B unboxed paths) |
| Library setter, per member | 203.2 ns · 48 B | 84.5 ns · 0 B | 14.5 ns · 0 B |

`JsonUtility` serializes less data (see [Unity IL2CPP](#unity-il2cpp)). At `839eac0` UltraLiteDB was 3.8x
slower than it to serialize and 7.1x slower to deserialize. Now it serializes 1.8x faster (9.4 µs vs 17.0 µs,
with 1 KB of garbage vs 8 KB) and deserializes within 10% of `FromJson`; from UTF-8 bytes, which is what a
save file holds, it's faster (15.1 µs vs 16.4 µs).

**BsonDocument codec** (the engine's path). The `BsonWriter`/`BsonReader` code didn't change from `839eac0`
until the codec pass, so their "before" applies to all earlier states. The two combined rows' "before" is the
typed-accessor state (their mapper half had already improved).

| Benchmark | before | codec pass |
|---|---|---|
| `BsonWriter.Serialize(doc)` | 44.68 µs · 26,400 B | 8.09 µs · 4,096 B |
| `BsonReader.Deserialize` | 45.39 µs · 43,264 B | 29.60 µs · 24,768 B |
| `ToDocument` + `BsonWriter` (typed-collection insert) | 105.94 µs · 67,088 B | 62.49 µs · 43,792 B |
| `BsonReader` + `ToObject` (typed-collection read) | 115.77 µs · 52,640 B | 102.86 µs · 34,336 B |

The typed-accessor pass, step by step (serialize with a reused writer / deserialize): typed accessors
24.4 → 14.5 / 51.6 → 39.3 µs; unboxed primitive paths → 10.2 / 36.6 µs; the profiled IL2CPP fixes
→ 9.4 / 15.1 µs. Removing the span helpers' hidden `typeof` lookups was worth 20 µs of deserialize.

### .NET (JIT and NativeAOT)

.NET 10.0.3.

| Benchmark | JIT `839eac0` | JIT `2729010` | JIT typed accessors | NativeAOT `839eac0` | NativeAOT `2729010` | NativeAOT typed accessors |
|---|---|---|---|---|---|---|
| `SerializeToBytes<T>` | 9.70 µs · 24,375 B | 3.44 µs · 5,800 B | 2.72 µs · 3,608 B | 12.48 µs · 24,383 B | 4.83 µs · 5,800 B | 4.76 µs · 4,792 B |
| `SerializeToBytes<T>`, reused `ByteWriter` | 8.91 µs · 13,476 B | 3.12 µs · 2,992 B | 2.58 µs · 800 B | 11.90 µs · 13,480 B | 4.81 µs · 2,992 B | 4.65 µs · 1,984 B |
| `DeserializeFromBytes<T>` | 11.83 µs · 22,600 B | 6.00 µs · 7,217 B | 4.39 µs · 5,024 B | 18.02 µs · 22,667 B | 8.38 µs · 7,239 B | 7.65 µs · 6,221 B |
| `SerializeToBytes<T>` (fields) | 9.48 µs · 24,375 B | 3.34 µs · 5,800 B | 3.66 µs · 5,800 B | 11.90 µs · 24,383 B | 4.82 µs · 5,800 B | 5.13 µs · 5,800 B |
| `DeserializeFromBytes<T>` (fields) | 10.60 µs · 18,182 B | 6.21 µs · 7,217 B | 5.40 µs · 7,217 B | 15.95 µs · 18,239 B | 8.26 µs · 7,233 B | 7.94 µs · 7,227 B |
| `ToDocument` + `BsonWriter` (reference) | 18.39 µs · 43,716 B | 19.83 µs · 43,716 B | 18.35 µs · 43,716 B | 29.21 µs · 45,300 B | 28.57 µs · 45,300 B | 27.58 µs · 45,272 B |
| `BsonReader` + `ToObject` (reference) | 18.69 µs · 44,567 B | 17.36 µs · 40,150 B | 17.13 µs · 40,150 B | 28.57 µs · 45,247 B | 27.85 µs · 40,816 B | 25.88 µs · 40,790 B |
| System.Text.Json serialize (reference) | 4.13 µs · 2,720 B | 3.98 µs · 2,720 B | 3.90 µs · 2,720 B | n/a | n/a | n/a |
| System.Text.Json deserialize (reference) | 8.26 µs · 5,872 B | 8.29 µs · 5,872 B | 8.03 µs · 5,872 B | n/a | n/a | n/a |

NativeAOT gains less from typed accessors: only the members matching its precompiled instantiations
(`int` properties here) get them, as described above.

BsonDocument codec, before → after the codec pass: `BsonWriter.Serialize(doc)` JIT 9.20 → 2.12 µs
(13,528 → 2,808 B, which is just the result array), NativeAOT 14.57 → 3.39 µs. `BsonReader.Deserialize` JIT 7.71 → 6.13 µs
(33,848 → 20,104 B), NativeAOT 9.80 → 7.51 µs.

- **`839eac0`** (all tables): before any of this work. The IL2CPP column was produced with `--dll` (see above).
- **`2729010`**: the direct-path optimization pass of 2026-09-20. It cached UTF-8 field names and per-type
  dispatch (`Mapper/DirectTypeInfo.cs`), encoded strings, keys and indices straight into the buffer, used
  byte-level field matching on read, cached enum names, added unboxed primitive array/list paths, reused
  per-thread scratch buffers (`Document/Bson/DirectBuffers.cs`), buffered stream I/O, and made auto-properties
  use their backing field instead of `MethodInfo.Invoke`.
- The stream benchmarks were added in the same pass (after: JIT 3.20 µs / 6.02 µs, NativeAOT 4.75 µs /
  8.50 µs for serialize / deserialize).
- **Codec pass** (2026-09-21): `BsonWriter.Serialize` builds in one pass into the reusable per-thread
  buffer (lengths back-filled, cached document/array lengths updated as the old sizing pass left them)
  using the direct writer's value encoding. `BsonReader` gets an in-memory fast path for `ByteReader` sources:
  shared key strings (`Document/Bson/BsonKeyCache.cs`), index keys skipped, arrays pre-sized, no temporary
  arrays for `ObjectId`/`Guid`, and shared `true`/`false`/small `int` values (`BsonValue` is immutable).
  Sizing no longer allocates (ASCII byte counting, index-key digit counting). The `BsonSerializer` stream
  methods buffer one document and make a single stream call. The forward-only `WriteDocument(IByteWriter)`
  is kept for fixed buffers (index pages) and `SerializeTo`.
- **Typed accessors** (2026-09-21): typed-delegate property accessors built with `MakeGenericType`, behind
  a runtime capability probe and per-accessor verification, falling back to reflection
  (`Mapper/Reflection/TypedAccessor.cs`, `Reflection.CreateTypedAccessor`). Also unboxed read/write of
  `int`/`long`/`double`/`float`/`bool`/`DateTime`/`Guid` properties in the direct serializer, and the
  IL2CPP hot-path fixes listed under [Remaining costs and ideas](#remaining-costs-and-ideas).
