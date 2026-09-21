# UltraLiteDB.Benchmarks

Performance benchmarks for `BsonMapper`, focused on the direct POCO ⇄ BSON paths
(`SerializeToBytes`/`SerializeToStream` in `BsonMapper.DirectSerialize.cs`,
`DeserializeFromBytes`/`DeserializeFromStream` in `BsonMapper.DirectDeserialize.cs`).
The `ToDocument`/`ToObject` path and System.Text.Json are measured alongside for reference.

UltraLiteDB's real target is **Unity IL2CPP**, which can't be run from here, so every benchmark runs on
two runtimes:

- **JIT (CoreCLR)**: the normal `dotnet run` runtime.
- **NativeAOT**: the closest local stand-in for IL2CPP. See [How NativeAOT relates to IL2CPP](#how-nativeaot-relates-to-il2cpp).

## Running

```bash
./run.sh                                  # serializer suite, JIT then NativeAOT
./run.sh jit                              # JIT only (fast to iterate)
./run.sh aot accessors                    # accessor suite, NativeAOT only
./run.sh both all --seconds 5             # everything, longer runs for steadier numbers
./run.sh jit --filter deserialize         # only benchmarks whose name contains "deserialize"
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

**What doesn't:**
- IL2CPP translates IL to C++, and its reflection (`MethodInfo.Invoke`, `FieldInfo.GetValue`), boxing and
  allocation (Boehm GC) are typically much slower than NativeAOT's. Reflection- and allocation-heavy code
  therefore looks better here than it will on device. Treat time ratios as directional.
- Unity's BCL is Mono-based, so allocations and costs *inside* BCL calls (`Enum.ToString`, `Encoding`, ...)
  differ.
- **Final validation of any performance change belongs in a Unity IL2CPP player build.**

## Workflow for performance changes

1. Run `./run.sh both all` before changing anything, to get a same-machine baseline.
2. Make the change and run the test suite (`cd ../UltraLiteDB.Tests && dotnet test`). The direct-path tests
   assert byte-equivalence with the `BsonDocument` path, which is the safety net for serializer changes.
3. Run the benchmarks again and compare.
4. Add notable before/after numbers to [Results history](#results-history).

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

As of the 2026-09-20 pass, per serialize of the ~2.8 KB model:
- ~2.8 KB is the returned array (unavoidable with `SerializeToBytes`; use the `ByteWriter` overload to reuse a buffer).
- ~3 KB is **boxing**: `GenericGetter` returns `object`, so every value-type member and every `Dictionary`
  value boxes (24 B each). Deserialize has the same boxing, because values reach the setter as `object`.
- Deserialize allocates ~4.3 KB for the object graph itself, which is necessary.

Removing the boxing needs typed per-member code. Options, all compatible with the no-emit constraint:

1. **Typed delegates**: `Delegate.CreateDelegate` into `Func<TOwner, TValue>`/`Action<TOwner, TValue>`,
   wrapped in a generic accessor class (see `AccessorSuite.Accessor<TTarget, TValue>`, and compare its
   rows). Inside the library the wrapper would have to be built with `MakeGenericType` at runtime. IL2CPP
   supports that for value-type arguments only with full generic sharing (Unity 2022.1+), so verify it on
   the minimum supported Unity version. Keep the current backing-field / `MethodInfo.Invoke` path as a
   fallback, and never let a missing AOT instantiation surface as a runtime failure. Getting zero boxing,
   not just faster access, also means passing typed values through to the writer and reader instead of `object`.
2. **Roslyn source generator**: generates the accessors (or whole per-type serializers) at compile time.
   It's fully AOT-safe and needs no runtime generic instantiation; this is how MessagePack-CSharp and
   System.Text.Json handle AOT. It's a bigger project, and Unity supports source generators from 2021.2+.

## Results history

Apple M2 Max, macOS 26.6, .NET 10.0.3, `--seconds 3`. Time per operation, then bytes allocated per operation.

| Benchmark | JIT before | JIT after | NativeAOT before | NativeAOT after |
|---|---|---|---|---|
| `SerializeToBytes<T>` | 9.70 µs · 24,375 B | 3.44 µs · 5,800 B | 12.48 µs · 24,383 B | 4.83 µs · 5,800 B |
| `SerializeToBytes<T>`, reused `ByteWriter` | 8.91 µs · 13,476 B | 3.12 µs · 2,992 B | 11.90 µs · 13,480 B | 4.81 µs · 2,992 B |
| `DeserializeFromBytes<T>` | 11.83 µs · 22,600 B | 6.00 µs · 7,217 B | 18.02 µs · 22,667 B | 8.38 µs · 7,239 B |
| `SerializeToBytes<T>` (fields) | 9.48 µs · 24,375 B | 3.34 µs · 5,800 B | 11.90 µs · 24,383 B | 4.82 µs · 5,800 B |
| `DeserializeFromBytes<T>` (fields) | 10.60 µs · 18,182 B | 6.21 µs · 7,217 B | 15.95 µs · 18,239 B | 8.26 µs · 7,233 B |
| `ToDocument` + `BsonWriter` (reference) | 18.39 µs · 43,716 B | 19.83 µs · 43,716 B | 29.21 µs · 45,300 B | 28.57 µs · 45,300 B |
| `BsonReader` + `ToObject` (reference) | 18.69 µs · 44,567 B | 17.36 µs · 40,150 B | 28.57 µs · 45,247 B | 27.85 µs · 40,816 B |
| System.Text.Json serialize (reference) | 4.13 µs · 2,720 B | 3.98 µs · 2,720 B | n/a | n/a |
| System.Text.Json deserialize (reference) | 8.26 µs · 5,872 B | 8.29 µs · 5,872 B | n/a | n/a |

- **Before**: commit `839eac0`.
- **After**: the direct-path optimization pass of 2026-09-20. It cached UTF-8 field names and per-type
  dispatch (`Mapper/DirectTypeInfo.cs`), encoded strings, keys and indices straight into the buffer, used
  byte-level field matching on read, cached enum names, added unboxed primitive array/list paths, reused
  per-thread scratch buffers (`Document/Bson/DirectBuffers.cs`), buffered stream I/O, and made auto-properties
  use their backing field instead of `MethodInfo.Invoke`.
- The stream benchmarks were added in the same pass (after: JIT 3.20 µs / 6.02 µs, NativeAOT 4.75 µs /
  8.50 µs for serialize / deserialize).
