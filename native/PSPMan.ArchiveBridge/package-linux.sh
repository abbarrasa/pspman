#!/usr/bin/env bash
set -euo pipefail
bridge_root="$(cd -- "$(dirname -- "$0")" && pwd)"
source_archive="$(realpath "${1:?Usage: package-linux.sh <verified-source-archive>}")"
case "$(uname -m)" in
  x86_64) rid=linux-x64;; aarch64) rid=linux-arm64;; armv7l) rid=linux-arm;; i?86) rid=linux-x86;;
  *) echo 'Unsupported build host architecture; use a matching native toolchain.' >&2; exit 1;;
esac
expected=a4e374c8f864f51df48c2cfd0e1413ec3b2ea030deae852d6a655402ebb61ac4
actual="$(sha256sum "$source_archive" | cut -d' ' -f1)"
test "$actual" = "$expected"
package="$bridge_root/artifacts/runtimes/$rid/native"
mkdir -p "$package/bridge-source"
cp "$bridge_root/out/7z.so" "$bridge_root/out/libpspman_archive_bridge.so" "$package/"
cp "$source_archive" "$package/7z2606-src.tar.xz"
cp "$bridge_root/licenses/"*.txt "$package/"
cp "$bridge_root/../../LICENSE" "$package/LICENSE.PSPMan.txt"
for name in bridge.cpp bridge.h bridge.def build-linux.sh build-windows.ps1 README.md source.lock.json ArchiveRuntime.targets package-linux.sh package-windows.ps1; do
  cp "$bridge_root/$name" "$package/bridge-source/$name"
done
# Static names only: hashes are hex and the RID is selected above, never untrusted JSON fragments.
(
  cd "$package"
  printf '{"Abi":1,"Version":2606,"Rid":"%s","SourceSha256":"%s","Files":{' "$rid" "$expected"
  separator=''
  for name in 7z.so libpspman_archive_bridge.so 7z2606-src.tar.xz LICENSE.7zip.txt COPYING.LGPL.txt LICENSE.unRAR.txt LICENSE.PSPMan.txt \
      bridge-source/bridge.cpp bridge-source/bridge.h bridge-source/bridge.def bridge-source/build-linux.sh bridge-source/build-windows.ps1 bridge-source/README.md bridge-source/source.lock.json bridge-source/ArchiveRuntime.targets bridge-source/package-linux.sh bridge-source/package-windows.ps1; do
    hash="$(sha256sum "$name" | cut -d' ' -f1)"
    printf '%s"%s":"%s"' "$separator" "$name" "$hash"
    separator=','
  done
  printf '}}\n'
) > "$package/runtime.json"
printf 'Packaged %s\n' "$rid"
