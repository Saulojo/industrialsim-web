#!/bin/sh
# Prepara a VM Linux do servidor (D10, Etapa 9A). Roda na VM, a partir do pacote extraído; pode rodar de novo
# (para aplicar mudanças nos arquivos de deploy/) sem perder as configurações locais:
#   sudo sh industrialsim-<versao>/deploy/scripts/install.sh [--site <ip-ou-dominio>] [--admin <usuario>] [--admin-net <rede/prefixo>]
#
#   --site       endereço do site no Caddy (padrão: o IP da VM na rota padrão)
#   --admin      usuário que administra a VM e roda o teste de carga por ssh (padrão: quem chamou o sudo)
#   --admin-net  rede de onde o ssh é aceito (padrão: a rede da interface da rota padrão)
#
# Instala as dependências do .NET self-contained e o Caddy (repositório oficial), cria o usuário do serviço e as
# pastas, copia serviços, Caddyfile, retenção de logs e firewall, e liga tudo. A primeira versão do servidor entra
# depois, com o update.sh.
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "Rode como root (sudo)." >&2
    exit 1
fi

deploy=$(cd "$(dirname "$0")/.." && pwd)
site=""
admin="${SUDO_USER:-}"
admin_net=""

while [ $# -gt 0 ]; do
    case "$1" in
        --site) site=$2; shift 2 ;;
        --admin) admin=$2; shift 2 ;;
        --admin-net) admin_net=$2; shift 2 ;;
        *) echo "Opção desconhecida: $1" >&2; exit 2 ;;
    esac
done

iface=$(ip -4 route show default | awk '{ for (i = 1; i < NF; i++) if ($i == "dev") { print $(i + 1); exit } }')
if [ -z "$site" ]; then
    site=$(ip -4 -o addr show dev "$iface" | awk '{ split($4, a, "/"); print a[1]; exit }')
fi
if [ -z "$admin_net" ]; then
    admin_net=$(ip -4 route show dev "$iface" scope link | awk '$1 ~ /\// { print $1; exit }')
fi
if [ -z "$admin" ] || [ "$admin" = root ]; then
    echo "Informe --admin <usuario> (o usuário que entra por ssh na VM)." >&2
    exit 2
fi
if [ -z "$site" ] || [ -z "$admin_net" ]; then
    echo "Não descobri o IP ou a rede da VM; informe --site e --admin-net." >&2
    exit 2
fi

echo "Site: $site; administrador: $admin; ssh aceito de: $admin_net"

echo "== Pacotes: dependências do .NET self-contained (Ubuntu 26.04 e 24.04) e ferramentas"
export DEBIAN_FRONTEND=noninteractive
apt-get update
icu=$(apt-cache pkgnames libicu | grep -E '^libicu[0-9]+$' | sort -V | tail -n 1)
ssl=$(apt-cache pkgnames libssl3 | grep -E '^libssl3(t64)?$' | sort | tail -n 1)
optional=""
if apt-cache show libbrotli1 > /dev/null 2>&1; then
    optional="libbrotli1"
fi
# shellcheck disable=SC2086 # $optional pode ficar vazio
apt-get install -y ca-certificates curl gnupg ufw tzdata zlib1g libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 \
    "$icu" "$ssl" $optional

echo "== Caddy (repositório oficial stable, https://caddyserver.com/docs/install)"
if [ ! -f /etc/apt/sources.list.d/caddy-stable.list ]; then
    apt-get install -y debian-keyring debian-archive-keyring apt-transport-https
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
    chmod o+r /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    chmod o+r /etc/apt/sources.list.d/caddy-stable.list
    apt-get update
fi
apt-get install -y caddy

echo "== Usuário do serviço e pastas"
if ! id industrialsim > /dev/null 2>&1; then
    useradd --system --home-dir /var/lib/industrialsim --no-create-home --shell /usr/sbin/nologin industrialsim
fi
install -d -o root -g root -m 0755 /opt/industrialsim /opt/industrialsim/releases
install -d -o root -g industrialsim -m 0750 /etc/industrialsim

# Configurações locais: só na primeira vez, para não apagar o que foi ajustado na VM.
if [ ! -f /etc/industrialsim/industrialsim.env ]; then
    install -o root -g industrialsim -m 0640 "$deploy/config/industrialsim.env" /etc/industrialsim/industrialsim.env
fi
sed "s|__SITE__|$site|" "$deploy/config/caddy.env" > /etc/industrialsim/caddy.env
chmod 0644 /etc/industrialsim/caddy.env

echo "== Serviços, Caddyfile e retenção de logs"
install -m 0644 "$deploy/systemd/industrialsim.service" /etc/systemd/system/industrialsim.service
install -d -m 0755 /etc/systemd/system/caddy.service.d
install -m 0644 "$deploy/systemd/caddy-industrialsim.conf" /etc/systemd/system/caddy.service.d/industrialsim.conf
if [ -f /etc/caddy/Caddyfile ] && [ ! -f /etc/caddy/Caddyfile.original ]; then
    cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.original
fi
install -m 0644 "$deploy/caddy/Caddyfile" /etc/caddy/Caddyfile
install -d -m 0755 /etc/systemd/journald.conf.d
install -m 0644 "$deploy/journald/industrialsim.conf" /etc/systemd/journald.conf.d/industrialsim.conf
install -m 0644 "$deploy/tmpfiles/industrialsim.conf" /etc/tmpfiles.d/industrialsim.conf

# Reinício do serviço sem senha, só para o administrador (teste de carga por ssh).
sudoers=$(mktemp)
sed "s|__ADMIN__|$admin|" "$deploy/sudoers/industrialsim-loadtest" > "$sudoers"
if command -v visudo > /dev/null 2>&1; then
    visudo -cf "$sudoers"
fi
install -m 0440 "$sudoers" /etc/sudoers.d/industrialsim-loadtest
rm -f "$sudoers"

# Volta de versão sem precisar do pacote.
install -m 0755 "$deploy/scripts/rollback.sh" /usr/local/sbin/industrialsim-rollback

systemctl daemon-reload
systemctl restart systemd-journald
systemd-tmpfiles --create /etc/tmpfiles.d/industrialsim.conf

# Valida como o usuário do serviço: o validate prepara a CA interna e o arquivo de log, e eles têm de ser do caddy.
runuser -u caddy -- env HOME=/var/lib/caddy INDUSTRIALSIM_SITE="$site" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
systemctl enable caddy
systemctl restart caddy

systemctl enable industrialsim
if [ -e /opt/industrialsim/current ]; then
    systemctl restart industrialsim
else
    echo "Nenhuma versão instalada ainda: rode o update.sh do pacote."
fi

echo "== Firewall"
sh "$deploy/scripts/firewall.sh" "$admin_net"

echo
echo "Pronto. Raiz do certificado interno (importe nos PCs de teste):"
echo "  /var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt"
