# Validação — Etapa 8: Configurações da planta e monitor de comunicação

- Branch: `etapa-08-config-monitor`
- Pull request: [Saulojo/industrialsim-web#9](https://github.com/Saulojo/industrialsim-web/pull/9)
- Data: 20/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300, com o Controle Inteligente de Aplicativos desligado (ver Observações) |
| .NET SDK | 10.0.404 (fixado em `global.json`) |
| Node / npm | 26.13.0 / 11.20.0 |
| Navegador dos testes | Chromium do Playwright 1.65.1 |
| CI | GitHub Actions: .NET em `windows-latest` e `ubuntu-latest`; web; ponta a ponta em `ubuntu-latest` |

Nenhuma dependência nova, nem no servidor nem no web. O PDF do mapa de I/O sai da impressão do próprio navegador (D45).

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `IndustrialSim.Sessions/PlantInstance.cs`, `PlantSettings.cs` | **Pausar e retomar** (situação `Paused`, D43): o laço continua chamando a planta, mas ela não avança; o estado continua sendo publicado para a IHM. **Configurações** (`UpdateSettings`): formato, ordem das palavras, watchdog e tempo limite, bloco de diagnóstico, Unit ID e mestres permitidos. Tudo é conferido antes de aplicar: ou todas as mudanças valem, ou nenhuma. **Forçar e liberar** pelo nome do sinal; com a planta pausada, o valor forçado é publicado na hora. |
| A | `IndustrialSim.Sessions/PlantEventLog.cs` | **Log de comunicação**: os últimos 500 eventos, cada um com tipo, argumentos e horário. Exceções, recusas e limite de conexões repetidos viram uma linha só, com contagem (janela de 60 s). |
| A | `IndustrialSim.Sessions/Modbus/MasterPairing.cs` | **Mestres permitidos** (D42): no modo "um por vez", aceitar ou autorizar outro IP retira o anterior e fecha as conexões dele. Pedir esse modo com dois IPs autorizados é recusado. Cada decisão do pareamento vai para o log, com o autor. |
| A | `IndustrialSim.Modbus/IModbusServerObserver.cs`, `ModbusTcpServer.cs` | O servidor Modbus avisa as conexões autorizadas, recusadas e fechadas, o limite de conexões e as respostas de exceção, com o endereço pedido. `PlantModbus` mede as requisições por segundo, amostradas a cada 1 s. |
| A | `IndustrialSim.IO/IoMap.cs`, `PlantIo.cs` | Cada linha do mapa diz o que o ponto é (`IoPointKind`). `DecodeEngineering` calcula o valor de engenharia que o mestre obteria de cada registrador, em qualquer formato e com a faixa atual. `GetForced` lista os valores forçados; `RequestedSettings` mostra uma troca ainda pendente. |
| B | `IndustrialSim.Contracts/PlantSocket.cs`, `Plants.cs` | **Protocolo versão 2.** Comandos: `updateSettings`, `forceInput`, `releaseInput`, `pause`, `resume`, `revokeMaster` e `subscribeMonitor`. Mensagens: `settings`, `forced`, `monitorLayout`, `monitor` e `log`; contadores em `modbus`; situação `paused`. Tipos TypeScript regerados. |
| B | `IndustrialSim.Server/PlantSocket.cs`, `MonitorFeed.cs`, `ContractMapping.cs` | O WebSocket manda configurações, forçados e Modbus só quando mudam, e o log a cada 250 ms. O monitor vai a 10 Hz, só para quem o ligou (D44); as escritas vão quando o autor muda ou a cada 1 s. Até as contas (Etapa 10), o autor das ações da IHM é o IP do navegador. |
| B | `IndustrialSim.Server/IoMapApi.cs` | `GET /api/plants/{id}/io-map` (JSON) e `io-map.csv`. O mapa sai no formato e com as faixas atuais, com área, endereços nas duas notações, registradores ocupados, acesso, tipo, bruto em 4 e 20 mA, faixa e unidade. |
| C | `web/src/ui/plant`, `web/src/ui/IoMapPrintPage.tsx`, `web/src/net/plantStore.ts` | A página da planta ganhou abas: Processo, Comunicação, Monitor e Log, além de Pausar/Retomar e do aviso de planta pausada. **Comunicação**: painel de configurações (só o que mudou vai em "Aplicar") e mestres autorizados com "Retirar", conexões e requisições por segundo. **Monitor**: valor bruto e de engenharia, última escrita (IP e horário), base 0 ou notação clássica (guardada no navegador), forçar e liberar. **Forçados** destacados no monitor, no sinóptico e num painel próprio. **Log** traduzido. Mapa de I/O em CSV e página de impressão para PDF. O diálogo de pareamento avisa qual mestre sai no modo "um por vez". |
| C | `web/src/design`, `web/src/locales/pt-BR` | Tokens novos `--color-forced` e `--color-forced-surface`, nos dois temas. Contraste calculado (WCAG 2.2) do texto sobre o fundo de forçado: 6,35:1 no claro e 7,05:1 no escuro, acima do AA (4,5:1). Todos os textos novos estão no arquivo de idioma. |
| D | `web/e2e/communication.spec.ts`, `web/e2e/evidence.ts` | Um teste Playwright por critério, com o servidor de verdade e o mestre Modbus de teste (que ganhou as leituras de input register). Os helpers de evidência foram para um módulo comum aos dois specs. |

### Decisões novas (a registrar no descritivo: D42 a D45)

- **D42 (Etapa 8):** mestres permitidos por planta, "vários ao mesmo tempo" (padrão) ou "um por vez".
  - No modo um por vez, aceitar ou autorizar outro IP retira a autorização do anterior e fecha as conexões dele; o diálogo de pareamento avisa antes.
  - Pedir o modo um por vez com mais de um IP autorizado é recusado (`several-masters-authorized`), e o usuário escolhe quem retirar.
  - *Motivo:* evita dois mestres escrevendo na mesma planta sem perder o fluxo de pareamento da D38; mantém o comportamento das Etapas 6 e 7 como padrão.
  - *Descartado:* manter as autorizações e recusar a conexão de um segundo IP enquanto o primeiro estiver conectado (um mestre que reconecta sozinho ficaria preso sem explicação).
- **D43 (Etapa 8):** pausar deixa a planta no laço com a situação `Paused`.
  - O passo não avança; o tempo simulado, o ruído e o watchdog param (o watchdog conta tempo simulado, D32).
  - O estado continua sendo publicado a 20 Hz, e o mestre continua lendo e escrevendo; os comandos valem no primeiro passo depois de retomar.
  - Forçar ou liberar com a planta pausada publica as medições na hora.
  - Reiniciar uma planta pausada a mantém pausada.
  - *Motivo:* retomar continua do mesmo estado, bit a bit (mesmo hash de uma planta que nunca pausou); a IHM e o monitor continuam vivos durante a pausa.
  - *Descartado:* tirar a planta do laço (perderia a thread e a publicação do estado).
- **D44 (Etapa 8):** o monitor de registradores e o log vão pelo WebSocket da planta (protocolo versão 2).
  - O monitor vai só para as conexões que o ligaram, a cada 2 estados (10 Hz). Leva as linhas do mapa quando o formato muda, os valores brutos e de engenharia (calculados no servidor), as marcas de forçado e a última escrita de cada linha (reenviada quando muda o autor ou a cada 1 s).
  - O log vai a cada 250 ms, só com os eventos novos. Os eventos levam tipo e argumentos estáveis, traduzidos pela IHM (RNF11); exceções e recusas repetidas são agrupadas.
  - O log guarda os últimos 500 eventos por planta, em memória (a persistência vem com a Etapa 10).
  - *Motivo:* a escrita aparece em cerca de 90 ms; com o monitor ligado e um mestre escrevendo, a banda fica em 61 kbit/s; a IHM não converte contagens (sem cálculo no cliente, D01).
  - *Descartado:* monitor a 20 Hz (cerca de 100 kbit/s só ele); API REST consultada pela IHM; texto pronto no log do servidor.
- **D45 (Etapa 8):** mapa de I/O exportado pelo servidor em CSV (`io-map.csv`: colunas em inglês, números na cultura invariante, pronto para planilha ou script) e em PDF pela impressão do navegador.
  - A página `/plants/{id}/io-map` mostra a mesma lista traduzida, com o botão "Imprimir ou salvar em PDF".
  - O mapa sai no formato e com as faixas atuais da planta.
  - *Motivo:* sem dependência nova e já traduzido pelos arquivos de idioma do web.
  - *Descartado:* jsPDF (mais uma dependência no cliente); gerar o PDF no servidor (exigiria os textos traduzidos também no .NET).

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | Mudar o formato analógico ou o Unit ID na tela altera o que o mestre lê no passo seguinte, sem reiniciar | **Passo a passo** (`PlantCommunicationTests.FormatAndUnitIdChanges_ApplyToTheMasterAtTheNextStep_WithoutRestarting`, laço parado e passos dados pelo teste): logo depois da troca para 0–10000, o mestre ainda lê 0–27648; depois de 1 passo, lê 0–10000; o reinício continua 0. Trocar o Unit ID para 5 faz o Unit ID 1 receber a exceção 0x0B na requisição seguinte. **Pelo WebSocket**: `PlantSettingsSocketTests.FormatAndUnitId_FromTheSocket_…`. **Pela tela** (Playwright, critério 1): o mestre lê LT-101 em contagens, a tela troca para `float32` e o Unit ID para 17 | ✅ Antes: 13828 contagens (0,600 m). O mestre leu `float32` = 0,601 m **17 ms** depois do clique em "Aplicar". Unit ID 1 recusado com exceção 11 (0x0B) 1 ms depois do segundo clique; com Unit ID 17, leu 0,600 m. Reinícios: 0; o tempo simulado continuou (0,30 → 0,45 s). Captura: [img/etapa-08-criterio-1-formato.png](img/etapa-08-criterio-1-formato.png) |
| 2 | Uma escrita de um cliente de teste aparece no monitor em até 200 ms, com o IP de origem | **Pela tela** (Playwright, critério 2): 20 escritas de FV-101 pelo mestre de teste. Cada uma é medida do envio até a linha do monitor mostrar o valor; a página anota o instante com um `MutationObserver`, e o teste e a página usam o mesmo relógio do PC. **Pelo WebSocket**: `PlantSettingsSocketTests.AMasterWrite_ShowsUpInTheMonitor_Within200ms_WithItsAddress`, também com 20 escritas | ✅ Mínimo 19 ms, mediana **91 ms**, máximo **93 ms**; autor `127.0.0.1` e horário na coluna "Última escrita". Captura: [img/etapa-08-criterio-2-monitor.png](img/etapa-08-criterio-2-monitor.png) |
| 3 | Um valor forçado aparece destacado na IHM e no monitor, e deixa de valer ao ser liberado | **Pela tela** (Playwright, critério 3): força LT-101 em 0,3 m pelo monitor. Confere a linha destacada com "FORÇADO", o painel "Valores forçados" e a marca no sinóptico, e o mestre lendo o valor forçado. Libera pelo painel e confere que as marcas somem e o mestre volta a ler a planta. **Na planta e no WebSocket**: `PlantCommunicationTests.ForcingAndReleasing_…` (com a planta pausada, efeito imediato) e `PlantSettingsSocketTests.AForcedValue_IsFlagged_AndStopsWhenReleased`. **Na IHM**: testes Vitest do monitor, do sinóptico e do painel | ✅ Forçado: o mestre leu **6912** (0,3 m de 1,2 m = 25% de 27648). Liberado: o mestre leu **13815** (0,6 m, o valor da planta). Capturas: [monitor](img/etapa-08-criterio-3-monitor-forcado.png), [monitor no tema escuro](img/etapa-08-criterio-3-monitor-forcado-escuro.png) e [processo](img/etapa-08-criterio-3-ihm-forcado.png) |
| 4 | O mapa de I/O exportado bate com o `scenario.json` (teste automático) | **CSV, nos 4 formatos**: `PlantSettingsSocketTests.TheExportedIoMap_MatchesTheScenarioFile`. O teste compara cada linha do CSV com a esperada, montada pelo `ExpectedIoMap` a partir do `scenario.json` lido com `JsonDocument`, sem o carregador do servidor: endereço (2a no `float32`, D27), notação clássica, registradores, acesso, tipo, bruto em 4 e 20 mA, faixa × fator de unidade e unidade. **Pela tela** (Playwright, critério 4): baixa o CSV pelo botão e compara área, endereços, registradores e sinal com o `scenario.json` lido pelo teste. Abre "Mapa de I/O (PDF)", confere as mesmas linhas na página de impressão e gera o PDF pelo Chromium | ✅ 21 linhas, iguais nos dois testes. Arquivos gerados: [mapa-io-caldeira.csv](etapa-08/mapa-io-caldeira.csv) e [mapa-io-caldeira.pdf](etapa-08/mapa-io-caldeira.pdf) (46 kB). Captura: [img/etapa-08-criterio-4-mapa-pdf.png](img/etapa-08-criterio-4-mapa-pdf.png) |
| 5 | Pausar congela a planta e o monitor mostra "pausada"; retomar continua do mesmo estado (mesmo hash) | **Hash** (`PlantCommunicationTests.Pause_FreezesThePlant_AndResume_ContinuesFromTheSameState`): duas plantas iguais andam 300 passos; uma é pausada e o laço dá mais 1000 ciclos. Passo e `StateHash()` não mudam e são iguais aos da outra. Depois de retomar, as duas andam 500 passos e continuam com o mesmo hash. Também: `APauseLongerThanTheWatchdogTimeout_DoesNotTripIt` e `PauseAndResume_ThroughTheSocket`. **Pela tela** (Playwright, critério 5): Pausar, 1,5 s parado, o monitor mostra "Planta pausada", Retomar | ✅ Hash igual ao da planta sem pausa no passo 300 e no passo 800. Pela tela, o tempo simulado ficou em 1,16 s durante 1,5 s; as 6 leituras do mestre (IR 0 a 5) ficaram idênticas, sem nem o ruído andar; 1 s depois de retomar, 2,20 s; reinícios 0. Captura: [img/etapa-08-criterio-5-pausada.png](img/etapa-08-criterio-5-pausada.png) |
| 6 | Toda mudança de configuração aparece no log | **Cada configuração** (`PlantCommunicationTests.EverySettingChange_IsLoggedOnce_WithOldAndNewValuesAndAuthor`, 7 casos): uma linha por mudança, com valor antigo, novo e autor. Repetir o mesmo valor não gera linha; uma mudança inválida não gera nada. **Pelo WebSocket**: `EverySettingChangedThroughTheSocket_ArrivesInTheLog`. **Pela tela** (Playwright, critério 6): muda as 7 configurações num "Aplicar" e o formato de novo noutro, e confere a aba Log | ✅ 8 mudanças, 8 linhas, todas com o autor `127.0.0.1`. Exemplos: "Formato analógico: 0–27648 (Siemens) → 0–10000.", "Tempo limite do watchdog: 2 s → 5 s.", "Mestres permitidos: Vários ao mesmo tempo → Um por vez." Além das configurações, o log tem conexões, pareamentos, exceções Modbus (com o endereço), forçar e liberar, pausa e reinício. Captura: [img/etapa-08-criterio-6-log.png](img/etapa-08-criterio-6-log.png) |

Medidas completas, em JSON: [etapa-08/](etapa-08/).

## Comandos e saídas

### .NET (todos os testes)

```bash
cd server
dotnet build IndustrialSim.sln -warnaserror
dotnet test
```

```text
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)
  total: 484
  falhou: 0
  concluído com êxito: 484
```

Novos nesta etapa (53 testes):

| Projeto | Testes novos |
| --- | --- |
| Sessions | `PlantCommunicationTests` (22) e `PlantEventLogTests` (3) |
| IO | `MonitorDecodingTests` (9) |
| Modbus | `ModbusObserverTests` (6) |
| Server | `PlantSettingsSocketTests` (10) e `ContractConstantsTests` (3) |

Saídas medidas:

```text
Escrita → monitor: mín 85 ms, mediana 100 ms, máx 100 ms         (PlantSettingsSocketTests, 20 escritas, servidor em memória)
10,1 mensagens do monitor por segundo; 61,3 kbit/s no total      (monitor ligado, mestre escrevendo a 10 Hz)
```

### Web (lint, unitários, build)

```bash
cd web
npm run lint
npm run typecheck
npm test
npm run build
```

```text
All matched files use Prettier code style!
 Test Files  9 passed (9)
      Tests  44 passed (44)
✓ built in 101ms
```

Novos: `plantStore.communication.test.ts` (4), `logText.test.ts` (3) e `PlantCommunication.test.tsx` (9).

### Ponta a ponta

```bash
cd web
npm run build
npx playwright test e2e/plant.spec.ts                                     # Etapa 7, sem regravar as evidências dela
EVIDENCE_DIR=../docs/validacao/etapa-08 npx playwright test e2e/communication.spec.ts
```

```text
  ok 1 [chromium] › e2e\communication.spec.ts:40:1 › critério 1: mudar o formato ou o Unit ID na tela altera o que o mestre lê, sem reiniciar (711ms)
  ok 2 [chromium] › e2e\communication.spec.ts:94:1 › critério 2: uma escrita do mestre aparece no monitor em até 200 ms, com o IP de origem (2.5s)
  ok 3 [chromium] › e2e\communication.spec.ts:144:1 › critério 3: um valor forçado aparece destacado na IHM e no monitor, e deixa de valer ao ser liberado (1.2s)
  ok 4 [chromium] › e2e\communication.spec.ts:189:1 › critério 4: o mapa de I/O exportado em CSV e em PDF bate com o scenario.json (834ms)
  ok 5 [chromium] › e2e\communication.spec.ts:268:1 › critério 5: pausar congela a planta e o monitor mostra "pausada"; retomar continua (4.1s)
  ok 6 [chromium] › e2e\communication.spec.ts:306:1 › critério 6: toda mudança de configuração aparece no log (689ms)
  6 passed (12.7s)
```

Os 6 testes da Etapa 7 também passaram (6 passed, 1,5 min), incluindo a banda de 60 s sem o monitor.

## Observações

- **Controle Inteligente de Aplicativos do Windows.** Nesta etapa ele bloqueou, de forma intermitente, o `IndustrialSim.IO.Tests.exe` (os testes rodaram por `dotnet <dll>`) e, de forma persistente, o `IndustrialSim.Core.dll` do build Release, que o servidor dos testes de ponta a ponta carrega. Ele não aceita exceção por arquivo, e o autor o desligou no PC de desenvolvimento. Na Etapa 9, decidir como fica no PC do laboratório. O serviço roda um build publicado sem assinatura e pode ser bloqueado do mesmo jeito; as saídas são deixar o controle desligado ou assinar os binários com certificado reconhecido.
- **Protocolo.** O WebSocket passou para a versão 2. Uma página antiga, em cache, recebe a mensagem de incompatibilidade da Etapa 7 e pede para recarregar.
- **Autor das ações.** Até as contas (Etapa 10), o autor no log é o IP do navegador (ou de quem chamou a API REST). Atrás do Caddy (Etapa 9), será o IP do proxy, a menos que o servidor passe a ler o `X-Forwarded-For`; anotar para a Etapa 9.
- **"Dono" das configurações.** O descritivo diz que o dono altera Unit ID, formato, watchdog e mestres permitidos. Até as contas, quem tem o identificador da planta pode alterar, como no pareamento (D38).
- **Persistência.** Configurações e log valem enquanto a planta existir, como pede o escopo; a persistência vem na Etapa 10.
- **Rede.** Nenhuma porta nova: o monitor e o log usam o WebSocket da planta, e o mapa de I/O usa a API do site.
