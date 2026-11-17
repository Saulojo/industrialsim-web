# Validação — Etapa 6: Modbus TCP por planta com pareamento

- Branch: `etapa-06-modbus`
- Pull request: [Saulojo/industrialsim-web#7](https://github.com/Saulojo/industrialsim-web/pull/7)
- Data: 17/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 (máquina `SAULO`) |
| .NET SDK | 10.0.404 (fixado em `global.json`); runtime 10.0.15 |
| CI | GitHub Actions: testes .NET em `windows-latest` e `ubuntu-latest` |
| Mestre 1 | Python 3.15.2 com `pymodbus` 3.16.2 (BSD-3) |
| Mestre 2 | CODESYS V3.5 SP22 Patch 4 (3.5.22.40) com CODESYS Control Win V3 x64 |
| Mestre dos testes automáticos | FluentModbus 5.3.2 (MIT), só nos projetos de teste |
| Tentado | OpenPLC Editor 4.3.5 com o OpenPLC Simulator (não faz Modbus TCP; ver abaixo) |

Pacote novo: **FluentModbus 5.3.2** (MIT), só em `IndustrialSim.Modbus.Tests` e `IndustrialSim.Server.Tests`, como mestre independente do servidor do projeto. O produto não ganhou dependência nova.

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `server/src/IndustrialSim.Modbus` (projeto novo) | Servidor Modbus TCP escrito no projeto (D36): cabeçalho MBAP; funções 1, 2, 3, 4, 5, 6, 15 e 16 lendo e escrevendo direto na `IoImage`; exceções 01, 02, 03 e 0B (Unit ID diferente, D37); autor (IP) e horário da última escrita em cada coil e holding register (`WriteLog`). `ModbusTcpServer`: uma porta por planta, conexões seguradas pelo pareamento (`IConnectionGate`) com as requisições descartadas enquanto pendentes, fechamento por IP, limite de conexões e keepalive TCP. |
| B | `server/src/IndustrialSim.Sessions/Modbus` | `PortAllocator` (faixa 15000–15999, rodízio, pula portas ocupadas), `MasterPairing` (D38) e `PlantModbus`. Cada `PlantInstance` nasce com o seu Modbus; sem porta livre, a planta não é criada e nenhuma gravação é aberta. Mestres autorizados contam como clientes da planta (ela não expira). Seção `Simulation:Modbus` no appsettings. |
| C | `IndustrialSim.Server`, `IndustrialSim.Contracts` | API REST do Modbus por planta e testes dos critérios 1 a 4. |
| D | `docs/guias`, `tools/modbus-master`, `tools/codesys`, `tools/openplc` | Guias (base comum, Python, CODESYS, OpenPLC, TIA Portal); malha de nível LIC-101 em Python, ST para CODESYS e ST para OpenPLC. |

### API REST (novas rotas)

| Método e rota | Resposta |
| --- | --- |
| `GET /api/plants/{id}/modbus` | Porta, Unit ID, IPs autorizados, pedidos pendentes (com prazo), conexões e contadores de requisições e exceções |
| `GET /api/plants/{id}/modbus/writes` | Autor e horário da última escrita em cada endereço, nas duas notações |
| `POST /api/plants/{id}/modbus/pairing/{pedido}/accept` | 204; 404 `pairing-request-not-found` |
| `POST /api/plants/{id}/modbus/pairing/{pedido}/refuse` | 204; 404 `pairing-request-not-found` |
| `POST /api/plants/{id}/modbus/masters` `{ "address" }` | 204 (autoriza um IP antes de ele conectar); 400 `invalid-request` |
| `DELETE /api/plants/{id}/modbus/masters/{ip}` | 204 (fecha as conexões do IP); 404 `master-not-found` |
| `PUT /api/plants/{id}/modbus/unit-id` `{ "unitId" }` | 204; 400 `invalid-request` fora de 1–255 |

`POST /api/plants` passa a devolver `modbusPort` e, sem porta livre, 503 `modbus-ports-exhausted`.

### Configuração (`Simulation:Modbus`)

| Chave | Produção (`appsettings.json`) | Desenvolvimento |
| --- | --- | --- |
| `BindAddress` | `0.0.0.0` | `127.0.0.1` (o padrão do código também é só loopback) |
| `PortRangeStart` / `PortRangeEnd` | 15000 / 15999 | igual |
| `DefaultUnitId` | 1 | igual |
| `PairingTimeout` | 60 s | igual |
| `RefusalCooldown` | 60 s | igual |
| `MaxPendingRequests` / `MaxConnections` | 8 / 8 por planta | igual |

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | Um cliente Modbus escreve em FV-101 e a posição real começa a mudar em até 2 passos; LT-101 coerente com a planta | **Passo a passo:** `PlantModbusTests.AWriteToFV101_MovesTheValveWithinTwoSteps_AndTheLT101ReadingMatchesThePlant`, com socket real e passos dados um a um. **De ponta a ponta:** `ModbusApiTests.AMasterWritesFV101_…`, com o servidor inteiro, o laço de tempo real e a FluentModbus | ✅ A posição mudou **1 passo** depois de a escrita chegar (ex.: escrita na imagem no passo 21, posição publicada no passo 22). LT-101: 13837 contagens lidas, planta em 0,60059 m = 13837,5 contagens (diferença < 1) |
| 2 | 30 plantas com 30 clientes simultâneos escrevendo valores distintos: nenhuma recebe valor de outra | `ModbusApiTests.ThirtyPlantsWithThirtySimultaneousMasters_…`: 30 plantas, 30 portas distintas, 30 mestres (FluentModbus, um por thread) por 3 s. Cada ciclo escreve FV-101 e FV-102 distintos e um heartbeat com o número da planta, e relê. No fim, confere a imagem, a entrada aplicada na planta e o contador de requisições de cada uma | ✅ 0 divergências; cerca de 200 a 224 mil ciclos de escrita e leitura em 3 s (6–7,8 mil por mestre); 0 exceções |
| 3 | Numa planta autorizada só para 127.0.0.1, uma conexão de 127.0.0.2 é recusada | `PlantModbusTests.OnAPlantAuthorizedOnlyFor127001_AConnectionFrom127002IsRefused` (socket com origem 127.0.0.2) e `ModbusApiTests.Pairing_ThroughTheApi` | ✅ A conexão de 127.0.0.2 não recebe resposta, vira pedido pendente (prazo de 60 s) e é fechada ao ser recusada; por 60 s, novas conexões do IP são fechadas na hora. O mestre de 127.0.0.1 segue atendido. Também verificados: fechamento sem resposta em 60 s, aceitar, retirar autorização e limite de pedidos. Roda no Windows e no Linux do CI |
| 4 | Leitura fora do mapa retorna a exceção 02 e a conexão continua válida | `ModbusTcpServerTests.ReadOutsideTheMap_ReturnsException02_AndTheConnectionStaysUsable` (FluentModbus e bytes) e `ModbusDeviceTests.AddressOutsideTheMap_ReturnsException02` (as 8 funções) | ✅ `0x84 0x02` para o input register 200, seguido de leitura normal na mesma conexão |
| 5 | Testado com pelo menos dois mestres reais diferentes, com capturas | **`pymodbus`** (script `tools/modbus-master/level_loop.py`) e **CODESYS V3.5 SP22** (Control Win), os dois fechando a malha de nível LIC-101 e passando pelo pareamento | ✅ Detalhes abaixo |
| 6 | ~~Com CLP real ou PLCSIM Advanced: o `MB_CLIENT` lê o nível e comanda a válvula~~ | Retirado do plano em 17/11/2026, por decisão do autor: o `MB_CLIENT` é um cliente Modbus TCP padrão, e o servidor já foi validado com dois mestres reais e com a FluentModbus (função 16 e Unit ID diferente incluídos) | — Retirado. O teste com CLP real também saiu da Etapa 9. Guia `docs/guias/tia-portal.md` sem teste |

### Mestre real 1: `pymodbus`

1. Planta criada pela API (porta 15000). O script conectou, e a planta segurou a conexão sem responder ([`api-modbus-pendente.json`](etapa-06/api-modbus-pendente.json)). O script repetia "Aguardando o pareamento…" ([`pymodbus-pareamento.log`](etapa-06/pymodbus-pareamento.log)).
2. Pedido aceito por `POST …/pairing/{pedido}/accept`: 204. O script passou a ser atendido ([`api-modbus-autorizado.json`](etapa-06/api-modbus-autorizado.json): 127.0.0.1 autorizado, 101 requisições, 0 exceções).
3. Ensaios de degrau de setpoint 0,60 → 0,55 m aos 20 s, período de 100 ms:

| Sintonia | Arquivo | Mínimo do nível | Comportamento |
| --- | --- | --- | --- |
| Kp 4, Ti 40 s | [`pymodbus-degrau-kp4-ti40.csv`](etapa-06/pymodbus-degrau-kp4-ti40.csv) | 0,527 m | Oscila devagar em torno do setpoint |
| Kp 20, Ti 250 s (padrão do exemplo) | [`pymodbus-degrau-kp20-ti250.csv`](etapa-06/pymodbus-degrau-kp20-ti250.csv) e [`.log`](etapa-06/pymodbus-degrau-kp20-ti250.log) | 0,537 m | Sobressinal de cerca de 26 %, volta ao setpoint sem oscilar (dentro de ±10 mm aos 280 s) |

A sintonia padrão segue a regra SIMC para processo integrador (Skogestad, S. *Simple analytic rules for model reduction and PID controller tuning*. Journal of Process Control, v. 13, p. 291–309, 2003): Kc = 1/(k·(τc+θ)) e Ti = 4·(τc+θ). O ganho k ≈ 0,0007 %/s por % foi medido no fechamento da válvula (de igual porcentagem, o ganho na abertura é cerca de 3 vezes maior), com θ ≈ 2 s (constante de tempo da válvula). O sobressinal em degrau de setpoint é o esperado para um PI num processo integrador.

Um degrau de 0,6 → 0,7 m satura FV-101 em 100 %: com a válvula toda aberta, o nível só sobe cerca de 0,0005 m/s. É a física da planta, não do mestre.

### Mestre real 2: CODESYS V3.5 SP22

Projeto montado seguindo o guia [`docs/guias/codesys.md`](../guias/codesys.md), com o programa [`tools/codesys/PLC_PRG.st`](../../tools/codesys/PLC_PRG.st):

| Captura | O quê |
| --- | --- |
| [`codesys-servidor-geral.png`](etapa-06/codesys-servidor-geral.png) | Modbus TCP Server: 127.0.0.1, porta 15000 |
| [`codesys-servidor-parametros.png`](etapa-06/codesys-servidor-parametros.png) | Parâmetros: Unit-ID 1 |
| [`codesys-canais.png`](etapa-06/codesys-canais.png) | Canais: FC 04 (0, 6), FC 06 (0, 1), FC 06 (10, 1), FC 05 (0, 1), a cada 100 ms |
| [`codesys-mapeamento.png`](etapa-06/codesys-mapeamento.png) | Mapeamento: `Medicoes` em `%IW0`, `FV101_CMD` em `%QW0`, `HEARTBEAT` em `%QW1` e `P101_RUN` |
| [`codesys-online-1.png`](etapa-06/codesys-online-1.png), [`codesys-online-2.png`](etapa-06/codesys-online-2.png) | PLC_PRG online: `started = TRUE`, LT-101 = 13818–13825 contagens (0,6 m), PI corrigindo FV-101 em torno de 82 % |

Log do servidor ([`codesys-servidor.log`](etapa-06/codesys-servidor.log)):

```text
20:53:13.139 Conexão Modbus 1 aberta por 127.0.0.1:55270 na porta 15000.
20:53:13.150 Planta 90c5a37d…: pedido de pareamento 34554794… de 127.0.0.1.
20:53:16.360 Planta 90c5a37d…: pedido de pareamento 34554794… de 127.0.0.1 aceito.
20:53:16.361 Conexão Modbus 1 de 127.0.0.1:55270 autorizada; 3 requisições recebidas antes da autorização foram descartadas.
20:53:16.451 Conexão Modbus 1 de 127.0.0.1:55270 fechada depois de 0 requisições.
20:53:16.636 Conexão Modbus 2 aberta por 127.0.0.1:55274 na porta 15000.
20:53:16.637 Conexão Modbus 2 de 127.0.0.1:55274 autorizada; 0 requisições recebidas antes da autorização foram descartadas.
```

O CODESYS, com "Reconexão automática", trocou de conexão depois dos time-outs do período pendente, e a nova conexão foi atendida na hora.

Estado da planta registrado pela API a cada 1 s, por 4 min depois do aceite ([`codesys-estado.csv`](etapa-06/codesys-estado.csv)): nível entre 0,5972 e 0,6033 m (setpoint 0,6 m), comando de FV-101 entre 77,4 e 87,3 %, sem nenhuma exceção Modbus. Os autores das escritas ([`codesys-api-escritas.json`](etapa-06/codesys-api-escritas.json)) mostram 127.0.0.1 na coil 0 (1) e nos holding registers 0 (40001) e 10 (40011).

### OpenPLC: tentado e descartado como mestre nesta etapa

O projeto [`tools/openplc/caldeira-lic101`](../../tools/openplc/caldeira-lic101) foi montado no OpenPLC Editor 4.3.5 ([`openplc-remote-device.png`](etapa-06/openplc-remote-device.png)) e compila para o OpenPLC Simulator. O programa rodou (heartbeat subindo), mas o nível ficou em 0 ([`openplc-simulador-sem-leitura.png`](etapa-06/openplc-simulador-sem-leitura.png)), e nenhuma conexão chegou à porta da planta. O fonte do Editor (source maps de `simulator-module.ts`) mostra que o Simulator emula um ATmega2560 com `avr8js`, só com timers e serial, e que a configuração de mestre do Remote Device é gerada para o Runtime v4 (Linux). A documentação do Editor diz que o Remote Device vale "no Runtime v4 e no Simulator"; para TCP, o Simulator não confirma isso. O guia [`openplc.md`](../guias/openplc.md) registra a limitação.

## Testes automáticos

| Projeto | Testes |
| --- | --- |
| `IndustrialSim.Contracts.Tests` | 2 |
| `IndustrialSim.Core.Tests` | 189 |
| `IndustrialSim.IO.Tests` | 58 |
| `IndustrialSim.Modbus.Tests` (novo) | 45 |
| `IndustrialSim.Sessions.Tests` | 46 |
| `IndustrialSim.Server.Tests` | 16 |
| `IndustrialSim.SimRun.Tests` | 34 |
| **Total** | **390** |

Rodados 5 vezes seguidas no Windows local (`dotnet test -c Release`), 390 de 390 em todas. CI verde no Windows e no Linux.

## Comandos

```bash
dotnet build server/IndustrialSim.sln -c Release -warnaserror
dotnet test -c Release        # em server/
dotnet run --project server/src/IndustrialSim.Server --launch-profile http
python tools/modbus-master/level_loop.py --port 15000 --step-at 20 --step-to 0.55 --duration 300 --csv ensaio.csv
```

```powershell
$api = "http://localhost:5080/api"
$p = Invoke-RestMethod -Method Post "$api/plants" -ContentType "application/json" -Body '{"scenarioId":"caldeira"}'
$m = Invoke-RestMethod "$api/plants/$($p.id)/modbus"
Invoke-RestMethod -Method Post "$api/plants/$($p.id)/modbus/pairing/$($m.pendingRequests[0].id)/accept"
```

## Problemas encontrados e corrigidos

1. **Corrida logo depois do aceite (achada pelo CI, no Linux e no Windows).** A decisão do pareamento chega à conexão de forma assíncrona; uma requisição mandada logo depois do aceite chegava antes e era descartada como anterior à autorização. Agora a conexão pendente espera até 50 ms pela decisão antes de descartar (`ModbusTcpServer.DecisionGrace`). Teste: `ARequestSentRightAfterTheDecision_IsAnswered`, repetido 20 vezes.
2. **Encerramento que prendia o pool de threads.** O `Dispose` do servidor esperava as conexões com `Task.WaitAll` (até 2 s), e elas precisavam do pool para terminar. Agora ele fecha o listener e os sockets na hora, sem bloquear.
3. **Testes sensíveis à carga da suíte inteira.** O critério 1 de ponta a ponta contava o atraso da rede como passos; passou a usar o passo em que a escrita já está na imagem. O teste do temporizador da Etapa 5 passou a medir numa thread `AboveNormal`, a prioridade do laço: com os 30 mestres em paralelo, uma thread normal acordava atrasada pelo escalonador. No CI Windows (VM de 4 núcleos), o teste de 30 mestres saturou a máquina: um mestre começou depois do prazo de 3 s e não fez nenhum ciclo, e o teste do laço da Etapa 5 contou 112 passos para um limite fixo de 110 (o `Task.Delay` de 1 s dormiu além da conta). Agora cada mestre faz pelo menos 20 ciclos e o teste confere isso, e o limite do teste do laço sai do tempo de relógio medido. Uma falha isolada de `EndingAPlant_ClosesPendingConnections` (1 em cerca de 30 rodadas, com o CODESYS rodando junto) não se repetiu em 15 rodadas isoladas e 8 completas; as esperas dos testes agora dizem qual condição não foi atingida.

## Decisões propostas para o descritivo

- **D36** (17/11/2026, Etapa 6): Servidor Modbus TCP escrito no projeto (`IndustrialSim.Modbus`), sobre a `IoImage`, em vez de NModbus ou FluentModbus. Motivo: nenhuma das duas expõe o IP de quem escreveu, nem deixa segurar a conexão durante o pareamento; a FluentModbus reserva 65.536 endereços por área (o problema do protótipo); a NModbus aceita todo cliente sem filtro. O protocolo de servidor é pequeno (MBAP e 8 funções). A FluentModbus (MIT) fica só nos testes, como mestre independente. Descartado: biblioteca com uma cópia da imagem sincronizada e um proxy na frente para o pareamento.
- **D37** (17/11/2026, Etapa 6): Unit ID configurável de 1 a 255 (padrão 1). Uma requisição com outro Unit ID recebe a exceção 0x0B (gateway target device failed to respond), sem ler nem escrever. Motivo: o erro fica visível no mestre e no log, em vez de um time-out. Descartado: ignorar a requisição; aceitar qualquer Unit ID.
- **D38** (17/11/2026, Etapa 6): Pareamento por IP. A conexão de IP não autorizado fica aberta e sem resposta, e as requisições recebidas são descartadas (salvo as que chegam até 50 ms antes de a autorização alcançar a conexão). Um pedido por IP, com prazo de 60 s: aceitar autoriza o IP; recusar fecha e bloqueia o IP por 60 s, sem novo pedido; sem resposta, fecha, e a próxima conexão abre outro pedido. Retirar a autorização fecha as conexões do IP. Por planta: até 8 pedidos pendentes e 8 conexões. Mestres autorizados contam como clientes (a planta não expira). As portas da faixa são entregues em rodízio, para a porta de uma planta encerrada não ir logo para outra. Até as contas (Etapa 10), quem tem o identificador da planta aceita e recusa. Motivo: comandos antigos não são executados depois da autorização; um mestre que reconecta sozinho não enche a IHM de pedidos. Descartado: responder com exceção enquanto pendente; executar os comandos guardados depois do aceite.

## Pendências e observações

- **Critério 6** (`MB_CLIENT`): retirado do plano; o guia do TIA Portal foi escrito pela ajuda do TIA Portal e não foi testado.
- **OpenPLC Runtime v4** (Linux, WSL ou Docker): projeto pronto, não testado.
- **Firewall do Windows:** a faixa 15000–15999 precisa de regra de entrada no PC do laboratório (Etapa 9). No desenvolvimento, o servidor escuta só em 127.0.0.1.
- A tela de pareamento chega na Etapa 7; até lá, aceitar e recusar é pela API.
- Os pedidos pendentes e o log de comunicação (requisições por segundo, exceções) ficam disponíveis pela API para o monitor da Etapa 8.
