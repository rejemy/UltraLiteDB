#!/bin/bash
#
# Builds the benchmarks into a Unity IL2CPP player (../UnityTest) and runs it headless. See README.md.
#
#   ./run-il2cpp.sh [serializer|accessors|all] [--seconds N] [--filter text] [--no-build] [--dll path]
#   ./run-il2cpp.sh --profile "deserialize: DeserializeFromBytes<T>" [--no-build]
#
# --dll uses a prebuilt UltraLiteDB.dll instead of building ../UltraLiteDB (e.g. an older commit, to compare).
# --profile runs one benchmark in the player, samples it with macOS `sample` for 8 seconds and prints the
#   functions with the most self time (full report: bin/il2cpp/profile.txt).
#
# Requires the Unity CLI (`unity`) and the Editor version named in UnityTest/ProjectSettings/ProjectVersion.txt
# with Mac build support. UnityTest must not be open in an Editor: the batch-mode build needs the project lock.
# Results: bin/il2cpp/results.txt (player log: bin/il2cpp/player.log, build log: bin/il2cpp/build.log).

set -euo pipefail
cd "$(dirname "$0")"

bench_dir="$PWD"
repo_dir="$(cd .. && pwd)"
project="$repo_dir/UnityTest"
app="$project/builds/UltraLiteBench.app"
out_dir="$bench_dir/bin/il2cpp"

suite="all"
seconds="3"
filter=""
build=1
dll=""
profile=""

while [ $# -gt 0 ]; do
	case "$1" in
		serializer|accessors|all) suite="$1" ;;
		--seconds) seconds="$2"; shift ;;
		--filter) filter="$2"; shift ;;
		--no-build) build=0 ;;
		--dll) dll="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift ;;
		--profile) profile="$2"; shift ;;
		*) echo "Unknown argument '$1'" >&2; exit 2 ;;
	esac
	shift
done

mkdir -p "$out_dir"

if [ "$build" = 1 ]; then
	if unity status --format json --no-banner 2>/dev/null | grep -q "\"project\": \"$project\""; then
		echo "UnityTest is open in a Unity Editor. Close it first: the batch-mode build needs the project lock." >&2
		exit 1
	fi

	echo "=== Syncing UltraLiteDB.dll and the benchmark sources into UnityTest/Assets ==="
	if [ -z "$dll" ]; then
		if ! dotnet build "$repo_dir/UltraLiteDB/UltraLiteDB.csproj" -c Release -nologo > "$out_dir/library-build.log" 2>&1; then
			cat "$out_dir/library-build.log" >&2
			exit 1
		fi
		dll="$repo_dir/UltraLiteDB/bin/Release/netstandard2.1/UltraLiteDB.dll"
	fi
	echo "Library: $dll"

	# stage, then rsync by checksum: unchanged files keep their timestamps, so Unity doesn't reimport them
	stage="$out_dir/stage"
	rm -rf "$stage"
	mkdir -p "$stage/UltraLiteBench" "$stage/UltraLiteDB"

	for f in *.cs Unity/*.cs; do
		# Program.cs is the console entry point; Unity/BenchmarkRunner.cs replaces it in the player
		[ "$f" = "Program.cs" ] || cp "$f" "$stage/UltraLiteBench/"
	done

	cp "$dll" "$stage/UltraLiteDB/UltraLiteDB.dll"

	mkdir -p "$project/Assets/UltraLiteBench" "$project/Assets/Plugins/UltraLiteDB"
	rsync -rc --delete --exclude '*.meta' "$stage/UltraLiteBench/" "$project/Assets/UltraLiteBench/"
	rsync -rc --delete --exclude '*.meta' "$stage/UltraLiteDB/" "$project/Assets/Plugins/UltraLiteDB/"

	echo "=== Building the IL2CPP player headless (log: bin/il2cpp/build.log) ==="
	if ! unity build "$project" --target StandaloneOSX --output-path "$app" --log-file "$out_dir/build.log" \
		--no-tail --no-provenance --allow-dirty-build --non-interactive --no-banner; then
		echo "Build failed. Errors from bin/il2cpp/build.log:" >&2
		grep -E "error CS[0-9]+|Error building Player|BuildFailedException|Build Finished, Result: Failure" "$out_dir/build.log" | head -40 >&2 || true
		exit 1
	fi
fi

if [ ! -d "$app" ]; then
	echo "No player at $app; run without --no-build first." >&2
	exit 1
fi

exe="$app/Contents/MacOS/$(ls "$app/Contents/MacOS" | head -1)"
results="$out_dir/results.txt"

if [ -n "$profile" ]; then
	echo "=== Profiling \"$profile\" in the IL2CPP player ==="
	report="$out_dir/profile.txt"
	rm -f "$results" "$report"
	"$exe" -batchmode -nographics -logFile "$out_dir/player.log" -benchOutput "$results" -benchSuite all \
		-benchSeconds 120 -benchFilter "$profile" > "$out_dir/player.stdout.log" 2>&1 &
	pid=$!

	# wait for the benchmark to start (the header is written first), then past its warmup
	for _ in $(seq 1 120); do
		grep -q "Allocations:" "$results" 2>/dev/null && break
		sleep 0.5
	done
	sleep 3

	if ! kill -0 "$pid" 2>/dev/null; then
		echo "The player exited before it could be sampled (player log: bin/il2cpp/player.log)." >&2
		exit 1
	fi

	sample "$pid" 8 -mayDie -file "$report" > "$out_dir/sample.log" 2>&1 || true

	# SIGKILL: a player treats SIGTERM as a quit request for the end of the frame, which never comes while
	# the benchmark loop is running on the main thread
	kill -9 "$pid" 2>/dev/null || true
	wait "$pid" 2>/dev/null || true

	if [ ! -f "$report" ]; then
		cat "$out_dir/sample.log" >&2
		exit 1
	fi

	echo "Top self time (samples; idle waits on other threads omitted). Full report: bin/il2cpp/profile.txt"
	sed -n '/Sort by top of stack/,$p' "$report" | tail -n +2 \
		| grep -vE "semaphore_|__workq_kernreturn|mach_msg|__semwait_signal|__psynch_|__pthread_canceled|kevent" \
		| sed -E 's/\(in [^)]*\)//; s/_m[0-9A-F]{40}//; s/  +/ /g' | cut -c1-150 | head -30
	exit 0
fi
args=(-batchmode -nographics -logFile "$out_dir/player.log" -benchOutput "$results" -benchSuite "$suite" -benchSeconds "$seconds")
if [ -n "$filter" ]; then args+=(-benchFilter "$filter"); fi

echo "=== Running the IL2CPP player headless ==="
rm -f "$results"
# the player's own stdout is Unity startup noise; results come from the results file
"$exe" "${args[@]}" > "$out_dir/player.stdout.log" 2>&1 &
pid=$!

# stream results as the player writes them
printed=0
print_new_lines() {
	if [ -f "$results" ]; then
		local total
		total=$(wc -l < "$results" | tr -d ' ')
		if [ "$total" -gt "$printed" ]; then
			sed -n "$((printed + 1)),${total}p" "$results"
			printed=$total
		fi
	fi
}

while kill -0 "$pid" 2>/dev/null; do
	sleep 1
	print_new_lines
done

set +e
wait "$pid"
status=$?
set -e
print_new_lines

if [ "$status" -ne 0 ]; then
	echo "Player exited with code $status (player log: bin/il2cpp/player.log)" >&2
	exit "$status"
fi
