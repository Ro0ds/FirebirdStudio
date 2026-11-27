#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

version=$(tr -d '\r\n' < VERSION)
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
    echo "VERSION must contain a SemVer version without a leading v." >&2
    exit 1
fi

runtime=linux-x64
package="FirebirdStudio-$version-$runtime-self-contained"
release_dir="$repo_root/releases"
mkdir -p -- "$release_dir"
work_dir=$(mktemp -d "$release_dir/.linux-publish.XXXXXXXX")
trap 'rm -rf -- "$work_dir"' EXIT
publish_dir="$work_dir/$package"

dotnet test FirebirdStudio.sln -c Release
dotnet publish src/FirebirdStudio.Desktop/FirebirdStudio.Desktop.csproj \
    -c Release -r "$runtime" --self-contained true \
    -p:DebugType=None -p:DebugSymbols=false -o "$publish_dir"

for executable in FirebirdStudio.Desktop Updater/FirebirdStudio.Updater; do
    if [[ ! -f "$publish_dir/$executable" ]]; then
        echo "Expected executable missing from publish output: $executable" >&2
        exit 1
    fi
    chmod +x -- "$publish_dir/$executable"
done

cp -- LICENSE "$publish_dir/LICENSE"
tar -C "$work_dir" -czf "$work_dir/$package.tar.gz" "$package"
mv -f -- "$work_dir/$package.tar.gz" "$release_dir/$package.tar.gz"
printf '\nPackage ready: %s\n' "$release_dir/$package.tar.gz"
