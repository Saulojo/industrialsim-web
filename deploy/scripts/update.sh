#!/bin/sh
# Instala a versão do servidor que está neste pacote e a põe no ar (Etapa 9A). Roda na VM:
#   sudo sh industrialsim-<versao>/deploy/scripts/update.sh
#
# Copia app/ para /opt/industrialsim/releases/<versao>, aponta /opt/industrialsim/current para ela (o link anterior
# vira "previous"), reinicia o serviço e espera /api/health. Se o servidor não responder em 60 s, volta sozinho
# para a versão anterior. Guarda as 5 versões mais recentes.
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "Rode como root (sudo)." >&2
    exit 1
fi

package=$(cd "$(dirname "$0")/../.." && pwd)
app="$package/app"
base=/opt/industrialsim
health=http://127.0.0.1:5080/api/health

if [ ! -f "$app/VERSION" ] || [ ! -f "$app/IndustrialSim.Server" ]; then
    echo "Pacote incompleto: falta $app/VERSION ou $app/IndustrialSim.Server." >&2
    exit 1
fi

version=$(cat "$app/VERSION")
target="$base/releases/$version"

wait_health() {
    i=0
    while [ "$i" -lt 60 ]; do
        if curl -fsS --max-time 2 "$health" > /dev/null 2>&1; then
            return 0
        fi
        i=$((i + 1))
        sleep 1
    done
    return 1
}

switch_to() {
    # Troca atômica do link (mv -T sobre o link antigo).
    ln -sfn "$1" "$base/current.new"
    mv -T "$base/current.new" "$base/current"
}

if [ -e "$target" ]; then
    echo "A versão $version já está em $target; para reinstalar, apague a pasta antes." >&2
    exit 1
fi

echo "== Instalando $version"
rm -rf "$target.tmp"
cp -R "$app" "$target.tmp"
chown -R root:root "$target.tmp"
find "$target.tmp" -type d -exec chmod 0755 {} +
find "$target.tmp" -type f -exec chmod 0644 {} +
# O pacote pode ter sido feito no Windows, sem o bit de execução.
chmod 0755 "$target.tmp/IndustrialSim.Server"
if [ -f "$target.tmp/createdump" ]; then
    chmod 0755 "$target.tmp/createdump"
fi
mv "$target.tmp" "$target"

previous=""
if [ -L "$base/current" ]; then
    previous=$(readlink -f "$base/current")
fi

switch_to "$target"
if [ -n "$previous" ]; then
    ln -sfn "$previous" "$base/previous"
fi

systemctl restart industrialsim
if wait_health; then
    echo "No ar: $version"
else
    echo "O servidor não respondeu em $health em 60 s." >&2
    journalctl -u industrialsim -n 30 --no-pager >&2 || true
    if [ -n "$previous" ]; then
        echo "Voltando para $(basename "$previous") ..." >&2
        switch_to "$previous"
        rm -f "$base/previous"
        systemctl restart industrialsim
        wait_health && echo "Versão anterior no ar." >&2
    fi
    exit 1
fi

# Guarda as 5 mais recentes (nunca a atual nem a anterior).
keep_current=$(readlink -f "$base/current")
keep_previous=$(readlink -f "$base/previous" 2>/dev/null || true)
# shellcheck disable=SC2012 # nomes das versões não têm espaços
ls -1dt "$base"/releases/* | tail -n +6 | while read -r old; do
    if [ "$old" != "$keep_current" ] && [ "$old" != "$keep_previous" ]; then
        rm -rf "$old"
        echo "Removida a versão antiga $(basename "$old")"
    fi
done
