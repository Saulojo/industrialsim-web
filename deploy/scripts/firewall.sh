#!/bin/sh
# Firewall da VM do servidor (RNF06, D10, Etapa 9A), com o ufw. Chamado pelo install.sh; pode rodar de novo:
#   sudo sh deploy/scripts/firewall.sh <rede-administrativa>     ex.: 10.0.64.0/22
#
# Entrada: só 443 (site e WebSocket, pelo Caddy) e 15000-15999 (Modbus das plantas), de qualquer lugar,
# e 22 (ssh) só da rede administrativa. Todo o resto, inclusive a 80 e a 5080 do Kestrel, fica fechado.
# A faixa 16000-16999 (HART-IP) só abre se o HART-IP for adotado (D09).
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "Rode como root (sudo)." >&2
    exit 1
fi

if [ $# -ne 1 ]; then
    echo "Uso: sudo sh firewall.sh <rede-administrativa, ex.: 10.0.64.0/22>" >&2
    exit 2
fi

admin_net=$1

# Recomeça do zero para não sobrar regra antiga; o ssh é liberado antes de ligar o firewall.
ufw --force reset > /dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow from "$admin_net" to any port 22 proto tcp comment 'ssh, so da rede administrativa'
ufw allow 443/tcp comment 'site e WebSocket (Caddy)'
ufw allow 15000:15999/tcp comment 'Modbus TCP das plantas'
ufw --force enable
ufw status verbose
