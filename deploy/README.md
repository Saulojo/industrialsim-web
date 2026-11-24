# Implantação

Arquivos de implantação do servidor numa VM Linux (Ubuntu Server LTS) no Hyper-V do PC do laboratório (D10, Etapa 9A). O roteiro completo está em [docs/implantacao.md](../docs/implantacao.md).

| Arquivo | Vai para (na VM) | Para quê |
| --- | --- | --- |
| `scripts/publish.sh` | (PC de desenvolvimento e CI) | Gera `artifacts/industrialsim-<versão>.tar.gz`: servidor self-contained `linux-x64`, web, cenários e esta pasta |
| `scripts/install.sh` | — | Prepara a VM: dependências, Caddy, usuário, pastas, serviços, logs, sudoers e firewall (pode rodar de novo) |
| `scripts/update.sh` | — | Instala a versão do pacote, troca o link `current` e volta sozinho se o servidor não subir |
| `scripts/rollback.sh` | `/usr/local/sbin/industrialsim-rollback` | Volta para a versão anterior |
| `scripts/firewall.sh` | — | ufw: 443, 15000–15999 e 22 (só da rede local) |
| `scripts/sample-resources.sh` | (mandado pelo ssh) | Amostra CPU e RAM da VM para o `simrun carga-rede` |
| `systemd/industrialsim.service` | `/etc/systemd/system/` | Serviço do servidor, com reinício automático |
| `systemd/caddy-industrialsim.conf` | `/etc/systemd/system/caddy.service.d/industrialsim.conf` | Endereço do site e reinício automático do Caddy |
| `config/industrialsim.env` | `/etc/industrialsim/` (só na primeira vez) | Configuração local do servidor |
| `config/caddy.env` | `/etc/industrialsim/` | Endereço do site (`INDUSTRIALSIM_SITE`) |
| `caddy/Caddyfile` | `/etc/caddy/Caddyfile` | HTTPS na 443 (`tls internal` na 9A) e proxy para `127.0.0.1:5080` |
| `journald/industrialsim.conf` | `/etc/systemd/journald.conf.d/` | Logs por 14 dias, até 1 GiB |
| `tmpfiles/industrialsim.conf` | `/etc/tmpfiles.d/` | Gravações de ensaio apagadas após 14 dias |
| `sudoers/industrialsim-loadtest` | `/etc/sudoers.d/` | Reinício do serviço sem senha, para o teste de carga por ssh |
| `hyperv/create-vm.ps1` | (Windows) | Cria a VM no Hyper-V |
| `hyperv/finish-install.ps1` | (Windows) | Tira a ISO depois de instalar o Ubuntu |
| `hyperv/measure-restart.ps1` | (Windows) | Mede quanto o site leva para voltar depois de um reinício |
