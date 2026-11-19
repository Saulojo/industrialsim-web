# Validação — Etapa 7: WebSocket e IHM web

- Branch: `etapa-07-websocket-ihm`
- Pull request: [Saulojo/industrialsim-web#8](https://github.com/Saulojo/industrialsim-web/pull/8)
- Data: 19/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 |
| .NET SDK | 10.0.404 (fixado em `global.json`) |
| Node / npm | 26.13.0 / 11.20.0 |
| Navegador dos testes | Chromium do Playwright 1.65.1 |
| CI | GitHub Actions: .NET em `windows-latest` e `ubuntu-latest`; web; ponta a ponta (job novo) em `ubuntu-latest` |

Pacote novo: **`@playwright/test` 1.65.1** (Apache-2.0), só como dependência de desenvolvimento do web. O produto não ganhou dependência nova: o WebSocket é o do ASP.NET Core, o gerador de tipos é do projeto, e as tendências e o sinóptico são SVG próprio.

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `IndustrialSim.Core/Scenarios`, `scenarios/` | Seção `hmi` do `scenario.json` (D39): distúrbios (`FV-103_command`, 0–100%) e alarmes (`LSLL-101`, `LSHH-101`, `PSH-101`, `PSV-101_open`). Validador, esquema regerado e 13 testes. Plano: o "reset" da Etapa 7 é o reinício da planta, que sai da Etapa 8. |
| A | `IndustrialSim.Contracts/PlantSocket.cs`, `TypeScriptWriter.cs` | Protocolo do WebSocket, versão 1: `hello` → `welcome` ou `incompatible`; `state`, `modbus`, `alarms` e `error`; comandos `setDisturbance`, `restart`, `acceptPairing` e `refusePairing`. Gerador próprio dos tipos TypeScript (D40), por `simrun --contratos` ou `npm run contracts`; um teste falha se `web/src/net/contracts.generated.ts` divergir do contrato. |
| B | `IndustrialSim.IO`, `IndustrialSim.Sessions` | `PlantIo.ReplacePlant` e `PlantInstance.Restart` (D41); `SetDisturbance`; alarmes avaliados a cada passo; `Subscribe`: estado publicado pelo laço a cada 5 passos (20 Hz), numa fila de 1 posição por assinante. Um leitor lento pula estados, mas não atrasa o laço. |
| B | `IndustrialSim.Server/PlantSocket.cs` | WebSocket em `/api/plants/{id}/ws`, na mesma porta do site (nenhuma porta nova). Confere a versão, manda o `welcome` (sinais com unidade, faixa e endereço Modbus nas duas notações), repassa os estados e manda `modbus` e `alarms` só quando mudam. Executa os comandos. A conexão conta como cliente da planta e loga os bytes enviados ao fechar. Seção `Simulation:Socket` no appsettings (`PublicAddress`, `HelloTimeout`, `KeepAliveInterval`). |
| C | `web/src/net`, `web/src/ui` | Cliente WebSocket com reconexão automática (espera de 0,25 s, dobrando até 2 s; conexão sem mensagens por 3 s é refeita) que volta à mesma planta. Estado da IHM e telas: escolha de cenário, dados de conexão (servidor, porta, Unit ID, "Mestre conectado"), diálogo de pareamento, sinóptico 2D da caldeira, tendências (120 s, em % da faixa), alarmes, distúrbio FV-103, reinício e download do CSV. Textos só em `locales/pt-BR`; cores só por tokens (novos: equipamento, água, quatro cores de tendência). |
| D | `web/e2e`, `web/playwright.config.ts`, CI | Um teste Playwright por critério, com o servidor .NET de verdade (porta 5180, Modbus 15500–15599) e um mestre Modbus TCP mínimo em Node (`e2e/modbusMaster.ts`). Job "Ponta a ponta (Playwright)" no CI. |

### Decisões novas (registradas no descritivo: D39 a D41)

- **D39 (Etapa 7):** seção opcional `hmi` no `scenario.json`, com os distúrbios que a IHM pode mudar e as saídas que viram alarme. O watchdog disparado é sempre alarme. Um distúrbio não pode estar numa entrada comandada pelo mapa Modbus.
  - *Motivo:* cenário novo sem código novo (D04); o servidor sabe o que a IHM pode comandar.
  - *Descartado:* listas fixas no código do web; deduzir alarmes das entradas discretas (P-101 em operação não é alarme).
- **D40 (Etapa 7):** tipos TypeScript gerados por código próprio (`TypeScriptWriter`, por reflexão sobre `IndustrialSim.Contracts`), conferidos por teste, como o esquema do cenário (D23). Mensagens do WebSocket em JSON com o campo `type`. O estado vai como listas de números `float32`, na ordem combinada no `welcome`, a 20 Hz.
  - *Motivo:* sem dependência nova; o contrato C# continua a fonte única (D05); a banda fica em cerca de 33 kbit/s.
  - *Descartado:* NSwag e TypeGen (mais uma dependência para poucos tipos); estado com nomes em cada mensagem (cerca de 3 vezes mais banda).
- **D41 (Etapa 7):** reiniciar a planta é remontá-la do cenário, com a mesma semente, mantendo a imagem de I/O, o Modbus, o pareamento e as configurações. Os comandos do mestre valem já no primeiro passo; os distúrbios voltam ao valor inicial. A gravação fecha o arquivo e começa outro (`{id}-{reinício}.csv`). O estado leva o número do reinício (`run`), e a IHM recomeça a tendência.
  - *Motivo:* o mestre não precisa reconectar; o reinício é determinístico (mesmo hash de uma planta nova, teste `Restart_IsDeterministic`); cada arquivo de gravação continua reproduzível do passo 0 (D34).
  - *Descartado:* criar outra planta (mudaria a porta e o pareamento); continuar o mesmo arquivo (a reprodução teria um salto no meio).

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | Playwright: abrir uma planta, ver os valores mudarem, mover o distúrbio e ver vazão de vapor e pressão responderem na tendência | `e2e/plant.spec.ts`, critério 1: abre a planta pela tela inicial e lê o tempo simulado e o LT-101 da legenda da tendência. Leva FV-103 de 50% a 90% pelo teclado e espera FT-102 subir e PT-101 cair. Também: `PlantSocketTests.Disturbance_MovesSteamFlowAndPressure_AndIsRecorded` (.NET) | ✅ Tempo simulado 0,10 → 1,20 s; 10 leituras distintas de LT-101 em 1 s. FT-102 2,005 → 3,299 kg/s; PT-101 9,996 → 9,275 bar; a tendência foi de 84 a 263 pontos. Captura: [img/etapa-07-criterio-1-tendencia.png](img/etapa-07-criterio-1-tendencia.png) |
| 2 | Playwright: um cliente Modbus de teste conecta, o diálogo de pareamento aparece, aceitar mostra "Mestre conectado" e recusar fecha a conexão | Critério 2: um mestre Modbus TCP em Node conecta à porta mostrada na tela. O diálogo pede "Aceitar conexão de 127.0.0.1?"; Aceitar → "Mestre conectado" e uma leitura FC3 respondida. Numa segunda planta, Recusar → o socket do mestre fecha. Também: `PlantSocketTests.PairingRequest_…` e `RefusingThroughTheSocket_…` | ✅ Leitura de FV-101 depois do aceite: 22749 (82,28% em 0–27648). Conexão recusada fechada 27 ms depois do clique. Capturas: [pedido](img/etapa-07-criterio-2-pedido.png) e [mestre conectado](img/etapa-07-criterio-2-mestre-conectado.png) |
| 3 | Após uma queda do WebSocket, a IHM reconecta sozinha em menos de 5 s, sem perder a planta | Critério 3: o Playwright fica entre a página e o servidor (`page.routeWebSocket`) e fecha a conexão do lado da página. O teste mede o tempo até "Conectado" e confere que a planta é a mesma e continuou (tempo simulado maior, reinício 0, tendência mantida). Também: testes Vitest da espera crescente (0,25/0,5/1/2/2 s) e da conexão sem mensagens | ✅ Reconectou em **316 ms**; mesma planta (`460bf978…`), tempo simulado 2,05 → 2,85 s, 2 conexões no total |
| 4 | Um cliente com versão de protocolo diferente recebe mensagem clara de incompatibilidade | Critério 4: a página anuncia a versão 99 no `hello` (alterado no caminho pelo Playwright) e o servidor de verdade responde. Também: `PlantSocketTests.OtherProtocolVersion_GetsIncompatible_AndTheServerCloses` (resposta `incompatible` e fechamento com 1008) e o teste Vitest da página | ✅ A IHM mostra "Versão incompatível" e "Esta página usa a versão 99 do protocolo, mas o servidor usa a versão 1. Recarregue a página (Ctrl+F5) para carregar a versão atual." Não tenta reconectar. Captura: [img/etapa-07-criterio-4-incompativel.png](img/etapa-07-criterio-4-incompativel.png) |
| 5 | O CSV baixado tem os mesmos dados da gravação do servidor | Critério 5: muda o distúrbio, clica em "Baixar ensaio (CSV)", encerra a planta (o servidor completa o arquivo) e compara o SHA-256 do arquivo baixado com o do mesmo número de bytes do início do arquivo do servidor. A gravação continua enquanto a planta roda, então o download é um retrato do que já foi gravado. Também: `PlantSocketTests.Disturbance_…_IsRecorded` | ✅ 14050 bytes baixados (59 linhas de dados, com o `FV-103_command` = 0.7); arquivo do servidor no fim com 14507 bytes. SHA-256 iguais: `94703bf6…59c7b74` |
| 6 | Banda medida por cliente ≤ 100 kbit/s | Critério 6: 60 s com um mestre pareado escrevendo o heartbeat a cada 250 ms e o distúrbio mudando a cada 10 s. O Playwright soma os bytes de cada quadro recebido; para o total, soma 4 bytes de cabeçalho por quadro, o máximo de um quadro do servidor de até 65535 bytes (RFC 6455, §5.2). Também: `PlantSocketTests.Hello_GetsWelcome_ThenStatesAt20Hz_WithinTheBandwidthBudget` | ✅ **33,3 kbit/s** (32,6 kbit/s de dados): 1204 estados em 60,2 s = 20,0 Hz, cerca de 204 bytes por estado. Do cliente para o servidor: 0,7 kbit/s |

Medidas completas, em JSON: [etapa-07/](etapa-07/).

## Comandos e saídas

### .NET (todos os testes)

```bash
cd server
dotnet build IndustrialSim.sln -warnaserror
dotnet test
```

```text
  total: 431
  falhou: 0
```

Novos nesta etapa: `HmiSectionTests` (13), `PlantSocketContractTests` (5), `ReplacePlantTests` (3), `PlantFeedTests` (10) e `PlantSocketTests` (10). Saída do teste de taxa e banda no servidor em memória:

```text
102 estados em 5,03 s = 20,3 Hz; 20186 bytes = 32,1 kbit/s; tempo simulado 5,04 s
exemplo: {"type":"state","step":505,"time":5.05,"run":0,"status":"running","watchdog":false,"inputs":[1,0.82280815,0.5,0.5],"outputs":[0.60072917,999872.4,1.9982349,1.9894087,0.8228075,0.5,0.5,1,0,0,0,0]}
```

### Web (lint, unitários, build)

```bash
cd web
npm run lint
npm test
npm run build
```

```text
All matched files use Prettier code style!
 Test Files  6 passed (6)
      Tests  28 passed (28)
✓ built in 96ms
```

### Ponta a ponta

```bash
cd web
npm run build
EVIDENCE_DIR=../docs/validacao/etapa-07 npx playwright test
```

```text
  ok 1 [chromium] › e2e\plant.spec.ts:65:1 › critério 1: os valores mudam e o distúrbio move vazão de vapor e pressão na tendência (13.5s)
  ok 2 [chromium] › e2e\plant.spec.ts:112:1 › critério 2: o pareamento aparece, aceitar conecta o mestre e recusar fecha a conexão (745ms)
  ok 3 [chromium] › e2e\plant.spec.ts:148:1 › critério 3: depois de uma queda do WebSocket, a IHM reconecta sozinha em menos de 5 s (3.1s)
  ok 4 [chromium] › e2e\plant.spec.ts:191:1 › critério 4: um cliente com outra versão do protocolo recebe a mensagem de incompatibilidade (3.2s)
  ok 5 [chromium] › e2e\plant.spec.ts:215:1 › critério 5: o CSV baixado tem os mesmos dados da gravação do servidor (5.5s)
  ok 6 [chromium] › e2e\plant.spec.ts:253:1 › critério 6: a banda do WebSocket por cliente fica abaixo de 100 kbit/s (1.0m)
  6 passed (1.5m)
```

## Observações

- **Controle de Aplicativo do Windows.** No PC de desenvolvimento, o Controle Inteligente de Aplicativos bloqueou duas vezes um binário recém-compilado: a DLL de testes de `IndustrialSim.Sessions.Tests`, que passou depois de recompilada, e o `IndustrialSim.Server.exe` gerado pelo `dotnet run`. Os testes de ponta a ponta sobem o servidor com `dotnet IndustrialSim.Server.dll`, como o CI. Vale conferir isso no PC do laboratório na Etapa 9: o serviço do Windows também deve rodar pela DLL ou por um executável publicado.
- **Rede.** O WebSocket usa a mesma porta do site (443 atrás do Caddy na Etapa 9). Nenhuma porta nova foi aberta.
- **Sinóptico e tendências** são telas funcionais simples, no estilo ISA-101 (cinza, com cor só no alarme), até a trilha de design (D1 e D2). O sinóptico 2D é específico da caldeira; um cenário sem desenho próprio mostra a tabela de valores.
- **Distúrbio e log.** Cada movimento do controle do FV-103 vai ao servidor e gera uma linha de log `1501` ("distúrbio FV-103_command = …"). Arrastar o controle gera várias linhas; isso fica como registro de ações até o log de comunicação da Etapa 8.
- **Testes instáveis no CI corrigidos.** Dois testes de etapas anteriores falharam uma vez cada no CI deste PR, por medirem o tempo pelo relógio do teste numa VM carregada:
  - `Metrics_ShowTheLoop` (Etapa 5, Windows) viu 39 atrasos registrados para 38 passos. O atraso é registrado antes do passo, e as métricas leem os contadores antes dos histogramas; o teste agora aceita o histograma alguns passos à frente.
  - `AMasterWritesFV101_…` (Etapa 6, Linux) viu a posição mudar 3 passos depois da escrita, porque o teste perdeu passos entre duas leituras da imagem. Agora o passo em que o comando entrou e a posição nesse passo vêm da gravação do ensaio (D34). O resultado é sempre 1 passo depois de a escrita chegar (3 execuções locais), e o critério 1 da Etapa 6 continua o mesmo.
- **CI.** O job de ponta a ponta não guarda o relatório do Playwright como artefato. Se ele falhar no CI, o log do job mostra o passo que falhou.
