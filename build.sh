#!/usr/bin/env bash
# Build ValheimStatus.dll in a stock .NET SDK container, compiling against the
# game's own assemblies and the installed BepInEx core. Nothing to install on
# the host besides docker.
#
#   VALHEIM_DIR    the dedicated server install (default: ~/valheim); the two
#                  below are derived from it
#   GAME_MANAGED   dir with assembly_valheim.dll, UnityEngine*.dll
#   BEPINEX_CORE   dir with BepInEx.dll
#   RUN_AS         uid:gid for the container (default: caller); the mounts must be
#                  readable and the checkout writable by that uid
#
# Output: out/ValheimStatus.dll
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

VALHEIM_DIR=${VALHEIM_DIR:-$HOME/valheim}
GAME=${GAME_MANAGED:-$VALHEIM_DIR/valheim_server_Data/Managed}
BEP=${BEPINEX_CORE:-$VALHEIM_DIR/BepInEx/core}
RUN_AS=${RUN_AS:-$(id -u):$(id -g)}
IMAGE=${DOTNET_IMAGE:-mcr.microsoft.com/dotnet/sdk:9.0}

[ -f "$GAME/assembly_valheim.dll" ] || { echo "no assembly_valheim.dll in $GAME" >&2; exit 1; }
[ -f "$BEP/BepInEx.dll" ]           || { echo "no BepInEx.dll in $BEP" >&2; exit 1; }

docker run --rm -u "$RUN_AS" \
    -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    -v "$PWD":/src -w /src \
    -v "$GAME":/game:ro \
    -v "$BEP":/bepinex:ro \
    "$IMAGE" \
    dotnet build src/ValheimStatus/ValheimStatus.csproj -c Release -o /src/out "$@"

ls -l out/ValheimStatus.dll
