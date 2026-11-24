#!/bin/sh
# Volta o servidor para a versão anterior (Etapa 9A). Instalado como /usr/local/sbin/industrialsim-rollback:
#   sudo industrialsim-rollback
# Troca /opt/industrialsim/current e /opt/industrialsim/previous, reinicia o serviço e espera /api/health.
# Rodar de novo desfaz a volta.
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "Rode como root (sudo)." >&2
    exit 1
fi

base=/opt/industrialsim
health=http://127.0.0.1:5080/api/health

if [ ! -L "$base/previous" ] || [ ! -d "$(readlink -f "$base/previous")" ]; then
    echo "Não há versão anterior em $base/previous." >&2
    exit 1
fi

current=$(readlink -f "$base/current")
previous=$(readlink -f "$base/previous")

ln -sfn "$previous" "$base/current.new"
mv -T "$base/current.new" "$base/current"
ln -sfn "$current" "$base/previous"
systemctl restart industrialsim

i=0
while [ "$i" -lt 60 ]; do
    if curl -fsS --max-time 2 "$health" > /dev/null 2>&1; then
        echo "No ar: $(basename "$previous") (a anterior agora é $(basename "$current"))"
        exit 0
    fi
    i=$((i + 1))
    sleep 1
done

echo "O servidor não respondeu em $health em 60 s; veja: journalctl -u industrialsim -n 50" >&2
exit 1
