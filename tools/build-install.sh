#!/usr/bin/env bash
set -euo pipefail

# Run with Git Bash. Override either path through the environment if needed.
repository_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repository_dir"

if ! command -v cygpath >/dev/null 2>&1; then
    echo 'Git Bash with cygpath is required on Windows.' >&2
    exit 1
fi

ksa_reference_path="${KSA_REFERENCE_PATH:-C:/Jogos/Kitten Space Agency}"
ksa_reference_posix="$(cygpath -u "$ksa_reference_path")"
if [[ ! -f "$ksa_reference_posix/KSA.dll" ]]; then
    echo "KSA reference assemblies not found in: $ksa_reference_path" >&2
    exit 1
fi
ksa_reference_native="$(cygpath -w "$ksa_reference_posix")"

if [[ -n "${KSI2_MOD_DIR:-}" ]]; then
    install_dir="$(cygpath -u "$KSI2_MOD_DIR")"
else
    install_dir="$(cygpath -u "$USERPROFILE")/Documents/My Games/Kitten Space Agency/mods/KSI2"
fi

dotnet restore KSI2.sln "-p:KsaReferencePath=$ksa_reference_native"
dotnet build KSI2.sln --configuration Release --no-restore "-p:KsaReferencePath=$ksa_reference_native"
if [[ "${SKIP_GEOMETRY_CHECKS:-0}" != "1" ]]; then
    dotnet run --project tests/KSI2.GeometryTests --configuration Release --no-build
fi

build_dir="$repository_dir/src/KSI2/bin/Release/net10.0"
for name in KSI2.dll KSI2.deps.json mod.toml KSI2Assets.xml ksi2.layout.defaults.json; do
    [[ -f "$build_dir/$name" ]] || { echo "Missing build output: $name" >&2; exit 1; }
done
[[ -d "$build_dir/assets" ]] || { echo 'Missing build output: assets/' >&2; exit 1; }
[[ -f "$build_dir/assets/optional/0Harmony.dll" ]] || {
    echo 'Missing optional HUD menu library in build output.' >&2
    exit 1
}
[[ -f "$build_dir/assets/optional/KSI2.HudMenuPatch.dll" ]] || {
    echo 'Missing optional HUD menu adapter in build output.' >&2
    exit 1
}

mkdir -p "$install_dir/assets"
for name in KSI2.dll KSI2.deps.json mod.toml KSI2Assets.xml ksi2.layout.defaults.json; do
    cp -f -- "$build_dir/$name" "$install_dir/$name"
done
while IFS= read -r -d '' source_file; do
    relative_path="${source_file#"$build_dir/assets/"}"
    destination_file="$install_dir/assets/$relative_path"
    mkdir -p -- "$(dirname -- "$destination_file")"
    if [[ ! -f "$destination_file" ]] || ! cmp -s -- "$source_file" "$destination_file"; then
        cp -f -- "$source_file" "$destination_file"
    fi
done < <(find "$build_dir/assets" -type f -print0)
cmp -s -- "$build_dir/KSI2.dll" "$install_dir/KSI2.dll"
cmp -s -- "$build_dir/ksi2.layout.defaults.json" "$install_dir/ksi2.layout.defaults.json"
echo "Installed KSI2 in: $install_dir"
echo 'Existing ksi2.settings.json was left in place.'
