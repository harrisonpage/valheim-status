#!/usr/bin/env bash
# Build the pushed HEAD on a host that has the game installed, tag it, and
# create a *draft* GitHub release with the dll and its sha256. Review the notes
# on GitHub and publish from there.
#
#   BUILD_HOST=rabbit VALHEIM_DIR=/thin/steam/valheim ./release.sh
#
#   BUILD_HOST     ssh host with docker and a dedicated server install (required)
#   VALHEIM_DIR    the install on that host (default: build.sh's default, ~/valheim)
#
# The version comes from Plugin.Version, which must match <Version> in the
# csproj; the tag is v<version>. The build runs in a fresh clone of the commit,
# so the asset is exactly what the tag contains.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

: "${BUILD_HOST:?set BUILD_HOST to an ssh host with docker and the game installed}"
REMOTE_DIR=${VALHEIM_DIR:-'$HOME/valheim'}
REPO=https://github.com/harrisonpage/valheim-status

VERSION=$(sed -n 's/.*const string Version = "\(.*\)";.*/\1/p' src/ValheimStatus/Plugin.cs)
CSPROJ=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/ValheimStatus/ValheimStatus.csproj)
[ -n "$VERSION" ] && [ "$VERSION" = "$CSPROJ" ] || { echo "Plugin.Version ($VERSION) != csproj <Version> ($CSPROJ)" >&2; exit 1; }
TAG=v$VERSION

git fetch -q --tags origin
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "uncommitted changes" >&2; exit 1; }
SHA=$(git rev-parse HEAD)
[ "$SHA" = "$(git rev-parse origin/master)" ] || { echo "HEAD is not origin/master; push first" >&2; exit 1; }
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then echo "$TAG already exists; bump the version" >&2; exit 1; fi
PREV=$(git describe --tags --abbrev=0 2>/dev/null || true)

echo "building $TAG ($(git rev-parse --short HEAD)) on $BUILD_HOST"
OUT=out/release
rm -rf "$OUT" && mkdir -p "$OUT"
ssh "$BUILD_HOST" bash -s -- "$SHA" "$REMOTE_DIR" "$REPO" > "$OUT/built-against" <<'EOF'
set -euo pipefail
sha=$1; dir=$(eval echo "$2"); repo=$3
tmp=$(mktemp -d "$HOME/valheim-status-release.XXXXXX")
trap 'rm -rf "$tmp"' EXIT
git clone -q "$repo" "$tmp" && git -C "$tmp" checkout -q "$sha"
VALHEIM_DIR="$dir" "$tmp/build.sh" >&2
mkdir -p ~/valheim-status-release && cp "$tmp/out/ValheimStatus.dll" ~/valheim-status-release/
game=$(grep -rhom1 "Valheim version: [^ ]*" "$dir"/*.log "$dir"/BepInEx/LogOutput.log 2>/dev/null | head -1 | sed 's/.*: //')
bep=$(grep -hom1 "BepInEx [0-9][0-9.]*" "$dir"/BepInEx/LogOutput.log 2>/dev/null | head -1 | sed 's/BepInEx //')
echo "${game:-unknown} ${bep:-unknown}"
EOF
scp -q "$BUILD_HOST:valheim-status-release/ValheimStatus.dll" "$OUT/"
(cd "$OUT" && shasum -a 256 ValheimStatus.dll > ValheimStatus.dll.sha256)
read -r GAME BEPINEX < "$OUT/built-against"
cat "$OUT/ValheimStatus.dll.sha256"

{
    echo "## What's new"
    echo
    git log --no-merges --format='- %s' ${PREV:+"$PREV..HEAD"}
    echo
    echo "**Built against** Valheim \`$GAME\` (Linux dedicated server) and BepInEx $BEPINEX."
    echo
    echo "**Install**: drop \`ValheimStatus.dll\` in \`BepInEx/plugins/ValheimStatus/\` and restart. Schema and config in the [README]($REPO#readme)."
    echo
    echo "**Verify the download**"
    echo
    echo '```'
    echo 'sha256sum -c ValheimStatus.dll.sha256'
    echo '```'
} > "$OUT/notes.md"

git tag -a "$TAG" -m "ValheimStatus $VERSION" "$SHA"
git push -q origin "$TAG"
gh release create "$TAG" --draft --verify-tag --title "ValheimStatus $VERSION" --notes-file "$OUT/notes.md" \
    "$OUT/ValheimStatus.dll" "$OUT/ValheimStatus.dll.sha256"
echo "draft created; review and publish it on GitHub (gh release edit $TAG --draft=false also works)"
