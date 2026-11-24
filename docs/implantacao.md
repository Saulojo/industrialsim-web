# Implantação do servidor (VM Linux no Hyper-V)

Roteiro da Etapa 9A: criar a VM, instalar, atualizar, voltar versão e testar. Ele vale para o PC do laboratório (provisório, desde 24/11/2026) e para o computador definitivo. Também vale, com outra forma de criar a VM, para um servidor da universidade ou da nuvem. Decisão de arquitetura: D10 no [descritivo](descritivo.md).

## Visão geral

```text
Rede do laboratório                        Windows 10/11 (hospedeiro, uso do autor)
┌──────────────┐                           ┌────────────────────────────────────────────────────┐
│ Navegador    │ HTTPS 443                 │ Hyper-V, switch externo "Externo"                  │
│ Mestre Modbus│──────────────────────────►│ ┌────────────────────────────────────────────────┐ │
│ (CODESYS...) │ Modbus TCP 15000–15999    │ │ VM "industrialsim": Ubuntu Server 26.04 LTS    │ │
└──────────────┘                           │ │ 4 vCPU reservadas, 4 GB fixos, IP próprio      │ │
                                           │ │  ufw: 443, 15000–15999 e 22 (só rede local)    │ │
                                           │ │  caddy (443, tls internal) ─► 127.0.0.1:5080   │ │
                                           │ │  industrialsim (systemd) ── Modbus 15000–15999 │ │
                                           │ └────────────────────────────────────────────────┘ │
                                           └────────────────────────────────────────────────────┘
```

| Onde | O quê |
| --- | --- |
| `/opt/industrialsim/releases/<versão>` | Versões instaladas do servidor (as 5 mais recentes) |
| `/opt/industrialsim/current`, `previous` | Links para a versão em uso e a anterior |
| `/etc/industrialsim/industrialsim.env` | Configuração local do servidor (não é sobrescrita pelo `install.sh`) |
| `/etc/industrialsim/caddy.env` | Endereço do site (`INDUSTRIALSIM_SITE`) |
| `/var/lib/industrialsim/recordings` | Gravações de ensaio (apagadas após 14 dias) |
| `journalctl -u industrialsim` | Log do servidor (JSON, retido por 14 dias) |
| `/var/log/caddy/industrialsim-access.log` | Log de acesso do site (14 dias) |
| `/var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt` | Raiz do certificado interno do Caddy (9A) |

Arquivos no repositório: [`deploy/`](../deploy/README.md).

## Hardware de referência

| | PC do laboratório (provisório, 9A) | Meta (RNF07) |
| --- | --- | --- |
| CPU | Core i7-4790 (2014), 4 núcleos, 8 threads | 2015 em diante |
| RAM | 8 GB (2 × 4 GB DDR3 1333) | 16 GB recomendado |
| VM | 4 vCPU reservadas, 4 GB fixos, disco de 40 GB | 4 vCPU, 4 GB |
| Rede | 100 Mbit/s no momento da instalação, rede 10.0.64.0/22 da universidade, DHCP | — |

Com 8 GB, sobram cerca de 4 GB para o Windows: o PC deve ficar dedicado ao servidor enquanto for provisório.

## 1. Preparar o Windows hospedeiro

1. Na BIOS, ligue a virtualização (Intel: *Intel Virtualization Technology*).
2. No PowerShell como administrador, ative o Hyper-V e reinicie:

   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All
   ```

3. Para criar e controlar a VM sem abrir um PowerShell como administrador, ponha o usuário no grupo do Hyper-V. Depois disso, saia e entre de novo no Windows:

   ```powershell
   Add-LocalGroupMember -Group "Administradores do Hyper-V" -Member $env:USERNAME
   ```

4. Energia: o PC nunca suspende nem hiberna (Configurações → Sistema → Energia: "Nunca"). Ajuste o horário ativo do Windows Update, para os reinícios automáticos caírem fora do horário de aula. A VM volta sozinha depois de um reinício (critério 3 da 9A).

## 2. Criar a VM

Na pasta do repositório, com a ISO do Ubuntu Server 26.04 LTS. O .NET 10 suporta oficialmente o Ubuntu 26.04, 24.04 e 22.04.

```powershell
.\deploy\hyperv\create-vm.ps1 -IsoPath "$env:USERPROFILE\Downloads\ubuntu-26.04.1-live-server-amd64.iso" -NetAdapterName "Ethernet 2"
Start-VM -Name industrialsim
vmconnect localhost industrialsim
```

Na primeira vez, o script cria o switch externo `Externo` no adaptador dado. **A rede do Windows cai por alguns segundos.** A VM fica com memória fixa, núcleos reservados, início automático com o Windows e sem pontos de verificação.

Instalação do Ubuntu, no console:

- Idioma do sistema: English (mensagens de erro fáceis de buscar). Teclado: o do PC.
- Rede: DHCP na interface do switch externo. Anote o IP. Se a rede não der IP à VM (alguns cadastros de rede liberam só MACs conhecidos), veja "Problemas comuns".
- Disco: o disco inteiro, com LVM (padrão).
- Usuário: o administrador da VM (ex.: `admin`).
- **Install OpenSSH server**: sim. Não importe chaves agora.
- Snaps adicionais: nenhum.

No fim, reinicie a VM e tire a ISO (no Windows):

```powershell
.\deploy\hyperv\finish-install.ps1
```

## 3. Acesso por ssh com chave

No Windows, crie uma chave (uma vez) e copie para a VM:

```powershell
ssh-keygen -t ed25519
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh admin@<IP-da-VM> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
ssh admin@<IP-da-VM>
```

Com a chave funcionando, desligue o login por senha no ssh, na VM:

```sh
echo 'PasswordAuthentication no' | sudo tee /etc/ssh/sshd_config.d/10-industrialsim.conf
sudo systemctl restart ssh
```

## 4. Gerar o pacote, instalar e pôr no ar

No PC de desenvolvimento (Git Bash, com .NET 10 e Node), na raiz do repositório:

```sh
sh deploy/scripts/publish.sh
scp artifacts/industrialsim-<versão>.tar.gz admin@<IP-da-VM>:
```

O pacote leva o servidor publicado para `linux-x64` em modo self-contained (a VM não precisa do .NET), o web e os cenários, mais a pasta `deploy/`. O CI gera o mesmo pacote a cada push.

Na VM, na primeira vez:

```sh
tar xzf industrialsim-<versão>.tar.gz
sudo sh industrialsim-<versão>/deploy/scripts/install.sh
sudo sh industrialsim-<versão>/deploy/scripts/update.sh
```

O `install.sh` faz o seguinte:

- instala as dependências do .NET e o Caddy (repositório oficial);
- cria o usuário `industrialsim` e as pastas;
- instala o serviço, o `Caddyfile`, a retenção de logs, a regra do sudoers do teste de carga e o firewall;
- descobre sozinho o IP da VM (o endereço do site) e a rede local (de onde o ssh é aceito). Para trocar: `--site`, `--admin-net` e `--admin`.

Rodar de novo aplica mudanças em `deploy/` sem apagar `/etc/industrialsim/industrialsim.env`.

O `update.sh` instala a versão do pacote, troca o link `current`, reinicia o serviço e espera `/api/health`. Se o servidor não responder em 60 s, ele volta sozinho para a versão anterior.

### Confiar no certificado (9A)

Na 9A, o certificado vem da CA interna do Caddy. Para o navegador e as ferramentas aceitarem, copie a raiz para o PC de teste e importe no repositório do usuário (o Windows pede confirmação):

```powershell
ssh admin@<IP-da-VM> "sudo cat /var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt" > industrialsim-root.crt
Import-Certificate -FilePath .\industrialsim-root.crt -CertStoreLocation Cert:\CurrentUser\Root
```

Depois disso, `https://<IP-da-VM>/` abre no Edge e no Chrome sem aviso. Na 9B, com domínio e certificado válido, este passo deixa de existir.

## 5. Atualizar e voltar versão

Atualizar: gere outro pacote, copie e rode só o `update.sh` do pacote novo. Rode também o `install.sh` se algo em `deploy/` mudou.

Voltar para a versão anterior, na VM (rodar de novo desfaz a volta):

```sh
sudo industrialsim-rollback
```

## 6. Operação

| Para | Comando (na VM) |
| --- | --- |
| Estado dos serviços | `systemctl status industrialsim caddy` |
| Log do servidor, ao vivo | `journalctl -u industrialsim -f` |
| Versão no ar | `readlink /opt/industrialsim/current` ou `curl -s http://127.0.0.1:5080/api/health` |
| Firewall | `sudo ufw status verbose` |
| Mudar uma configuração | editar `/etc/industrialsim/industrialsim.env` e `sudo systemctl restart industrialsim` |
| Mudar o endereço do site | editar `/etc/industrialsim/caddy.env` e `sudo systemctl restart caddy` |

## 7. Testes da Etapa 9A

Os comandos rodam no Windows hospedeiro ou em outro PC da rede, na pasta `server/` do repositório, com `--ca` apontando para a raiz copiada no passo 4.

**Carga com 30 plantas e 30 mestres por 30 minutos.** O `--ssh` reinicia o serviço antes (métricas zeradas) e amostra CPU e RAM da VM:

```sh
dotnet run --project src/IndustrialSim.SimRun -c Release -- carga-rede --servidor https://<IP-da-VM> --ca ../industrialsim-root.crt --ssh admin@<IP-da-VM> --plantas 30 --minutos 30 --relatorio ../docs/validacao/etapa-09a/carga-30-plantas-30-min.json
```

**Carga crescente.** Primeiro, suba o limite de plantas na VM (`Simulation__Instances__MaxInstances=300` em `industrialsim.env`, depois `sudo systemctl restart industrialsim`). Cada patamar roda N plantas e N mestres pelo tempo dado, e o teste para no primeiro que falhar:

```sh
dotnet run --project src/IndustrialSim.SimRun -c Release -- carga-rede --servidor https://<IP-da-VM> --ca ../industrialsim-root.crt --ssh admin@<IP-da-VM> --plantas 30,60,100,150,200,300 --minutos 5 --relatorio ../docs/validacao/etapa-09a/carga-crescente.json
```

As metas de cada patamar:

| Meta | Limite |
| --- | --- |
| Deriva | < 0,1% |
| Atraso do passo (p99) | < 5 ms |
| Erros (Modbus, HTTP e plantas em falha) | nenhum |
| CPU média da VM | < 80% |
| RAM disponível | ≥ 15% |

**Varredura de portas, de fora da VM.** Passa se só a 22, a 443 e a faixa 15000–15999 estiverem abertas:

```sh
dotnet run --project src/IndustrialSim.SimRun -c Release -- portas --host <IP-da-VM> --esperadas 22,443,15000-15999 --relatorio ../docs/validacao/etapa-09a/portas.json
```

**Reinício.** Rode o script antes de reiniciar. Para o hospedeiro, rode em outro PC da rede, porque este também reinicia:

```powershell
.\deploy\hyperv\measure-restart.ps1 -Url https://<IP-da-VM>/api/health -CaPath .\industrialsim-root.crt
```

## 8. Capacidade medida

*(Preenchido na Parte C da 9A com o teste de carga crescente: limite de plantas e mestres, e o gargalo.)*

## 9. Acesso de fora da universidade (Etapa 9B, com a TI)

O PC está numa rede privada da universidade (10.0.64.0/22, atrás do NAT e do firewall dela). Por isso, ninguém de fora alcança a VM sem a TI. Não use túneis (ngrok, Cloudflare Tunnel e afins) para contornar isso: eles furam o firewall da universidade sem autorização. Até a TI liberar, os testes são feitos de outros computadores da rede da universidade.

O que pedir à TI:

1. **IP fixo para a VM** (reserva de DHCP para o MAC dela) na rede do laboratório.
2. **IP público** apontando para a VM (NAT 1:1 ou encaminhamento de portas), com **só** a 443/TCP e a faixa 15000–15999/TCP liberadas de fora. Alternativa: acesso só por VPN da universidade.
3. **Nome no DNS** da universidade (ex.: `industrialsim.<dominio-da-universidade>`).
4. **Certificado HTTPS** válido para esse nome. O Caddy obtém um sozinho, pelo ACME, se a 443 estiver aberta de fora. Se a TI preferir, ela fornece um certificado da própria universidade.
5. **Banda de upload**: cerca de 33 kbit/s por IHM aberta (D40) e 61 kbit/s com o monitor ligado (D44). 30 usuários dão cerca de 2 Mbit/s.
6. Confirmar que **as redes que os alunos usam** (Wi-Fi da universidade e de casa) deixam sair conexões para as portas 15000–15999.

## 10. Levar para outro computador

Repita os passos 1 a 4 no computador novo; os dados (gravações) não são essenciais. Para levar a VM inteira, use `Export-VM -Name industrialsim -Path D:\export` no PC antigo e `Import-VM` no novo, e depois rode o `install.sh` de novo, porque o IP muda.

## Problemas comuns

| Sintoma | Causa e solução |
| --- | --- |
| A VM não recebe IP | A rede pode exigir cadastro do MAC. Veja o MAC com `Get-VMNetworkAdapter -VMName industrialsim` e peça à TI, ou use temporariamente um switch NAT do Hyper-V com encaminhamento de portas (o mestre Modbus passa a ver o IP do Windows). |
| `update.sh` volta sozinho | O servidor não subiu. Veja `journalctl -u industrialsim -n 50`. Faltando biblioteca (`libicu`, `libssl`), rode o `install.sh` de novo. |
| Navegador avisa "conexão não particular" | A raiz do Caddy não foi importada (passo 4) ou o IP mudou. Rode o `install.sh` com `--site <IP novo>` e importe de novo. |
| `carga-rede --ssh` falha ao reiniciar | Falta a chave ssh sem senha ou a regra do sudoers. Teste `ssh admin@<IP> sudo -n systemctl restart industrialsim`. |
| ssh recusado depois do firewall | O ssh só é aceito da rede do `--admin-net`. Entre pelo console do Hyper-V e rode `sudo sh firewall.sh <rede>`. |
