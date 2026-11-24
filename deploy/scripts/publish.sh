#!/bin/sh
# Gera o pacote de implantação do IndustrialSim Web (Etapa 9A), no PC de desenvolvimento (Git Bash no Windows)
# ou no CI (Linux):
#   sh deploy/scripts/publish.sh [versao]
#
# Faz o build do web para o wwwroot, publica o servidor self-contained para linux-x64 (a VM não precisa do .NET
# instalado) e empacota com a pasta deploy/:
#   artifacts/industrialsim-<versao>.tar.gz
#     industrialsim-<versao>/app/      servidor publicado, com o web e os cenários (VERSION diz a versão)
#     industrialsim-<versao>/deploy/   scripts e arquivos de configuração da VM
# Versão padrão: <data UTC>-<commit curto>, com "-dirty" se houver mudanças não commitadas.
set -eu

root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"

if [ $# -ge 1 ]; then
    version=$1
else
    version="$(date -u +%Y%m%d-%H%M)-$(git rev-parse --short HEAD)"
    if [ -n "$(git status --porcelain)" ]; then
        version="$version-dirty"
    fi
fi

name="industrialsim-$version"
out="$root/artifacts"
stage="$out/$name"
rm -rf "$stage" "$out/$name.tar.gz"
mkdir -p "$stage"

echo "== Web (npm ci, build para o wwwroot do servidor)"
(cd web && npm ci && npm run build)

echo "== Servidor (linux-x64, self-contained, versões travadas)"
dotnet publish server/src/IndustrialSim.Server \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained \
    -p:RestoreLockedMode=true \
    --output "$stage/app"

# Símbolos ficam no build; o pacote leva só o que roda.
find "$stage/app" -name '*.pdb' -delete
printf '%s\n' "$version" > "$stage/app/VERSION"

test -f "$stage/app/IndustrialSim.Server"
test -f "$stage/app/wwwroot/index.html"
test -f "$stage/app/scenarios/caldeira/scenario.json"

echo "== Arquivos de implantação"
cp -R deploy "$stage/deploy"

tar -C "$out" -czf "$out/$name.tar.gz" "$name"
rm -rf "$stage"
echo "Pacote: artifacts/$name.tar.gz"
