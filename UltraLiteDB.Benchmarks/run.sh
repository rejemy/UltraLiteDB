#!/bin/bash
#
# Runs the UltraLiteDB benchmarks. See README.md.
#
#   ./run.sh [jit|aot|both] [serializer|accessors|all] [--seconds N] [--filter text]
#
# Defaults: both runtimes, serializer suite, 3 seconds per benchmark.

set -e
cd "$(dirname "$0")"

mode="both"
case "$1" in
	jit|aot|both) mode="$1"; shift ;;
esac

if [ "$mode" = "jit" ] || [ "$mode" = "both" ]; then
	echo "=== JIT (CoreCLR) ==="
	dotnet run -c Release -- "$@"
fi

if [ "$mode" = "aot" ] || [ "$mode" = "both" ]; then
	echo "=== NativeAOT (IL2CPP stand-in) ==="
	# -p:Aot=true (not PublishAot) so the netstandard2.1 library isn't asked to AOT-compile; see the .csproj
	dotnet publish -c Release -p:Aot=true -o bin/aot -v quiet -nologo | grep -E "error" || true
	./bin/aot/UltraLiteDB.Benchmarks "$@"
fi
