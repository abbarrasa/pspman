#!/usr/bin/env bash
set -euo pipefail
# Caller supplies an extracted, unmodified official 26.06 source tree.
# CC/CXX may name wrappers; no tool or source download is performed here.
source_root="$(realpath "${1:?Usage: build-linux.sh <7zip-source-root> [output-directory]}")"
bridge_root="$(cd -- "$(dirname -- "$0")" && pwd)"
output_root="${2:-$bridge_root/out}"
mkdir -p -- "$output_root"
output_root="$(realpath "$output_root")"
cc_command="${CC:-cc}"
cxx_command="${CXX:-c++}"
make -C "$source_root/CPP/7zip/Bundles/Format7zF" -f makefile.gcc \
  -j"${BUILD_JOBS:-2}" O="$output_root/engine-objects" \
  CC="$cc_command" CXX="$cxx_command" CFLAGS_WARN_WALL='-Wall -Wextra'
# Use compiler wrapper files when compiler arguments are required.
"$cxx_command" -std=c++17 -shared -fPIC -O2 -Wall -Wextra -Werror \
  -fvisibility=hidden -I "$source_root" \
  "$bridge_root/bridge.cpp" "$source_root/CPP/Common/MyWindows.cpp" \
  -ldl -o "$output_root/libpspman_archive_bridge.so"
cp -- "$output_root/engine-objects/7z.so" "$output_root/7z.so"
cp -- "$source_root/DOC/License.txt" "$output_root/LICENSE.7zip.txt"
cp -- "$source_root/DOC/copying.txt" "$output_root/COPYING.LGPL.txt"
cp -- "$source_root/DOC/unRarLicense.txt" "$output_root/LICENSE.unRAR.txt"
