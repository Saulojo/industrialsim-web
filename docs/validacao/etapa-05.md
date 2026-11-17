# Validação — Etapa 5: Servidor de tempo real e instâncias

- Branch: `etapa-05-tempo-real`
- Pull request: [Saulojo/industrialsim-web#6](https://github.com/Saulojo/industrialsim-web/pull/6)
- Data: 17/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300, 24 processadores lógicos (máquina `SAULO`) |
| .NET SDK | 10.0.404 (fixado em `global.json`: 10.0.100 com `rollForward: latestFeature`); runtime 10.0.15 |
| CI | GitHub Actions: testes .NET em `windows-latest` e `ubuntu-latest`; integração em `ubuntu-latest` |

O servidor do laboratório passou a ser Windows (D10 alterada em 16/11/2026, primeiro commit da branch). Por isso as medições de tempo real foram feitas no Windows, que é o sistema alvo.

Pacote novo: `Microsoft.Extensions.Logging.Abstractions` 10.0.15 (MIT), a mesma versão do runtime ASP.NET Core, no projeto `IndustrialSim.Sessions`.

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `server/src/IndustrialSim.Sessions` | Laço de tempo real (`RealTime/`): threads dedicadas com passo fixo de 10 ms, grade fixa no relógio monotônico, recuperação de até 10 passos e descarte do excesso, métricas de atraso, custo do passo e deriva; espera por temporizador de alta resolução no Windows. `ScenarioCatalog`, `PlantInstance` (planta, imagem de I/O, dono, participantes e clientes) e `InstanceManager` (criar, listar, encerrar, limite e expiração). Logs estruturados com `LoggerMessage`. CI .NET também no Windows. |
| B | `IndustrialSim.Sessions/Recording`, `IndustrialSim.IO`, `IndustrialSim.SimRun` | `PlantRecorder` (CSV com comandos brutos, configurações, entradas e saídas), `RecordingFile` (fila e escrita fora da thread do laço) e `Replayer`. No `PlantIo`, o observador da fase de comandos e `StepWithCommands`. Comando `simrun reproduzir`. |
| C | `IndustrialSim.Server`, `IndustrialSim.Contracts` | API REST, contratos, serviço hospedado que inicia e para o laço, configuração `Simulation` no `appsettings.json`, log em JSON fora do desenvolvimento e cenários copiados para a pasta do programa. O job de integração do CI encerra o servidor com SIGINT e 5 plantas ativas. |
| D | `IndustrialSim.SimRun` | Comando `simrun carga`: N plantas em tempo real com um mestre de teste e um relatório com as metas. |

### API REST

| Método e rota | Resposta |
| --- | --- |
| `GET /api/scenarios` | Cenários que o servidor pode rodar: id, passo, semente, entradas e saídas |
| `POST /api/plants` `{ "scenarioId", "noiseSeed"?, "record"? }` | 201 com a planta e `Location`; 400 `scenario-not-found` ou `invalid-request`; 503 `instance-limit-reached` |
| `GET /api/plants` | Plantas em execução |
| `GET /api/plants/{id}` | Estado: passos, tempo simulado, watchdog, entradas e saídas em SI, deriva; conta como atividade |
| `DELETE /api/plants/{id}` | 204; 404 `plant-not-found` |
| `GET /api/plants/{id}/recording` | CSV da gravação (`text/csv`); 404 `recording-not-found` |
| `GET /api/simulation/metrics` | Métricas do laço: ciclos, passos, atrasos, descartes, atraso e custo do passo |

Os erros trazem só um código (`{ "code": "..." }`); a interface vai traduzi-lo pelos arquivos de idioma (RNF11).

### Formato da gravação (`industrialsim-recording/1`)

```text
#format=industrialsim-recording/1
#scenario=caldeira
#noise_seed=101
#step_s=0.01
#sample_every_steps=10
step,time_s,coil_0,hr_0,…,hr_10,format,word_order,watchdog,watchdog_timeout_s,diagnostics,P-101_run,…,LT-101,…,watchdog_tripped
1,0.01,1,22748,…
```

- Os coils e holding registers são os valores brutos copiados da imagem no início do passo. Também vão para o arquivo as configurações de I/O em vigor, as entradas da planta em SI (já com o watchdog aplicado), as saídas em SI e o estado do watchdog.
- Há uma linha por passo em que algum comando, configuração ou entrada mudou e uma linha a cada 10 passos (100 ms). `step` é o número de passos concluídos, e `time_s` é o tempo simulado no fim do passo.
- A reprodução monta a planta com a mesma semente e aplica, em cada passo, os comandos, as configurações e as entradas gravados (`PlantIo.StepWithCommands`). Em seguida grava de novo, no mesmo formato.
- A planta em execução grava em `Simulation:Instances:RecordingDirectory/{id}.csv`. A thread do laço só enfileira as linhas; quem escreve no disco é uma tarefa à parte.

### Laço de tempo real

- Cada thread do laço avança as plantas dela numa grade fixa: o ciclo k está previsto para `início + k × 10 ms` no relógio monotônico (`TimeProvider.GetTimestamp`, que é o `Stopwatch`). Por isso o erro de uma espera não se acumula.
- Depois de um atraso, até 10 ciclos são executados em seguida. O que passar disso é descartado e aparece na deriva da planta.
- No Windows, a espera usa um temporizador criado com `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`, porque o `Thread.Sleep` do Windows tem resolução de cerca de 15,6 ms. Nos outros sistemas (o CI em Linux), usa `Thread.Sleep`.
- As plantas vão para a thread menos ocupada. Uma planta que lança exceção sai do laço e fica em falha, sem afetar as outras.
- Expiração: uma planta sem cliente expira `IdleTimeout` depois da última atividade (criação, consulta pela API ou saída de um cliente). Até a hibernação da Etapa 10, expirar é encerrar.

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | 30 plantas da caldeira por 10 minutos, no Windows: deriva < 0,1%; atraso do passo p99 < 5 ms; CPU total < 50% de um núcleo | `simrun carga --plantas 30 --minutos 10` em Release, com um mestre de teste escrevendo válvulas e heartbeat de cada planta a cada 100 ms. Saída em `etapa-05/carga-30-plantas-10-min.txt` e relatório em `etapa-05/carga-30-plantas-10-min.json` | ✅ deriva máxima 0,0008%; atraso p99 0,76 ms (máx. 3,36 ms); CPU do processo 1,1% de um núcleo; 0 ciclos atrasados ou descartados |
| 2 | Reproduzir uma gravação gera CSV idêntico ao original (mesmo hash) | `RecordingTests.ARealTimeRecording_…`: gravação em tempo real, com comandos escritos de outra thread em momentos aleatórios e troca de formato, reproduzida com o mesmo SHA-256. `RecordingTests.Replay_OfAScriptedTest_…`: 3000 passos com comandos, `float32` em palavras invertidas, watchdog disparando e voltando e um distúrbio fora da imagem de I/O. Gravação feita pelo servidor real e reproduzida por `simrun reproduzir` | ✅ mesmo SHA-256 nos três casos; servidor: `AD31179B…572B20D` nos dois arquivos (`etapa-05/gravacao-servidor*.csv`) |
| 3 | Uma planta sem cliente expira no tempo configurado e libera recursos (teste com tempo reduzido) | `InstanceManagerTests.IdlePlants_Expire…` (relógio falso: não expira com 9 min, expira com 10; cliente e consulta adiam). `…WithAShortTimeout_…` (200 ms de tempo real: sai do gerenciador e do laço, e uma `WeakReference` para a imagem de I/O é coletada). `SimulationApiTests.AnIdlePlant_Expires…` (300 ms, pela API) | ✅ expira no tempo configurado; a memória é liberada |
| 4 | Testes de API com `WebApplicationFactory` cobrem criar, consultar e encerrar | `SimulationApiTests` (8 testes): cenários; criar, listar, consultar e encerrar; erros (cenário inexistente, pedido inválido, planta inexistente); limite (503); gravação em CSV; métricas; expiração; parada do host | ✅ 8 de 8; a classe passou 5 vezes seguidas |
| 5 | Encerrar o processo com plantas ativas (Ctrl+C ou parada do host, como na parada do serviço) não deixa threads penduradas | No Windows, servidor em Release com 5 plantas ativas e Ctrl+C (`etapa-05/ctrl-c-windows.log`). Testes: `SimulationApiTests.StoppingTheHost_…`, `InstanceManagerTests.ShuttingDown_…`, `RealTimeLoopTests.RunsPlantsInRealTime_…` e `…ConcurrentDispose_…`. No CI (Linux), o job de integração manda SIGINT com 5 plantas e exige saída com código 0 em até 5 s | ✅ Windows: encerrou 23 ms depois do Ctrl+C, com "threads ainda vivas: 0" e nenhum processo restante. CI: ⏳ roda quando a branch subir |

### Ensaio de carga (critério 1)

```text
$ dotnet server/src/IndustrialSim.SimRun/bin/Release/net10.0/simrun.dll carga --cenario scenarios/caldeira/scenario.json --plantas 30 --minutos 10 --relatorio docs/validacao/etapa-05/carga-30-plantas-10-min.json
Máquina: SAULO, 24 processadores lógicos, Microsoft Windows 10.0.26300, .NET 10.0.15
Plantas: 30 de 'caldeira' em 2 threads; espera: windows-high-resolution-timer
Duração: 600.0 s de relógio; 1800150 passos (60005 a 60005 por planta); 5497 varreduras do mestre
Atraso do passo: média 0.326 ms, p50 0.31 ms, p99 0.76 ms, máx. 3.36 ms
Custo do passo: média 3.7 µs, p99 20 µs, máx. 3.07 ms
Ciclos atrasados: 0; descartados: 0; estouros: 0; memória: 50 MB

[OK] deriva máxima 0.0008 % (meta < 0.1 %)
[OK] atraso do passo p99 0.76 ms (meta < 5 ms)
[OK] CPU do processo 1.1 % de um núcleo (meta < 50 %)
```

- A deriva tem resolução de um passo: ela é medida num instante qualquer entre dois passos. Por isso aparece maior nos primeiros segundos (0,14% aos 5 s numa rodada curta, ou seja, menos de 1 passo em 5 s). Em 10 minutos, essa resolução é de 0,0017%.
- A CPU é a do processo inteiro (laço, mestre de teste e runtime), dividida pelo tempo de relógio. Essa máquina de desenvolvimento é mais rápida que a meta do RNF07 (4 núcleos, 2015 em diante), e a folga é grande (1,1% contra 50%). Mesmo assim, o ensaio deve ser repetido no PC do laboratório na Etapa 9, que já tem o teste de carga de 30 minutos.
- O mestre fez 5497 varreduras em vez de 6000 porque o `Thread.Sleep(100)` dele, no Windows, dura cerca de 109 ms. Isso não afeta a planta.

## Comandos

```text
$ dotnet restore server/IndustrialSim.sln --locked-mode
$ dotnet build server/IndustrialSim.sln --no-restore --configuration Release -warnaserror
    0 Aviso(s)
    0 Erro(s)

$ cd server
$ dotnet test --no-build --configuration Release
  total: 328
  falhou: 0
  ignorados: 0

$ cd web
$ npm run lint      (sem erros)
$ npm test          Test Files 3 passed (3); Tests 10 passed (10)

$ simrun reproduzir server/src/IndustrialSim.Server/recordings/838c23d72f9e4c5fb22b8da267dba1a1.csv --cenario scenarios/caldeira/scenario.json --saida copia.csv
Reproduzidos 2280 passos (22.8 s); 229 linhas em copia.csv.
SHA-256 da gravação: AD31179B4F61CF2D3183C0362F57F432C23E0FFE6643D53A9BD87F855572B20D
SHA-256 da cópia:    AD31179B4F61CF2D3183C0362F57F432C23E0FFE6643D53A9BD87F855572B20D
Os arquivos são idênticos.
```

Testes novos: `IndustrialSim.Sessions.Tests` (35), `SimulationApiTests` (8), `ReplayCommandTests` (3 métodos) e `LoadCommandTests` (2 métodos).

## Problema encontrado e corrigido

Ao parar o host, o ASP.NET Core pode chamar o `StopAsync` dos serviços duas vezes ao mesmo tempo: uma pelo `IHost.StopAsync` e outra pelo `app.Run()`, que termina. A segunda chamada a `RealTimeLoop.Dispose` voltava na hora, ainda com as threads vivas. Agora uma chamada simultânea espera a primeira terminar, no laço e no gerenciador. Os testes `ConcurrentDispose_…` (20 repetições) e `StoppingTheHost_…` cobrem o caso.

## Decisões propostas para o descritivo

- **D33** (Etapa 5): o laço de tempo real roda em threads dedicadas (padrão: 2) compartilhadas pelas plantas, numa grade fixa no `Stopwatch`. Depois de um atraso, recupera até 10 passos; o resto é descartado e aparece na deriva. No Windows, a espera usa o temporizador de alta resolução (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`). Motivo: precisão de 10 ms no Windows (D10) sem girar a CPU, e uma planta lenta não trava as outras para sempre. Descartado: uma thread por planta (30 threads e trocas de contexto); `Thread.Sleep` ou `PeriodicTimer` no Windows (resolução de 15,6 ms); `timeBeginPeriod` (muda o relógio do sistema inteiro).
- **D34** (Etapa 5): a gravação (`industrialsim-recording/1`) guarda os coils e holding registers brutos copiados no início do passo, as configurações de I/O, as entradas e as saídas, numa linha por mudança e outra a cada 100 ms. A reprodução aplica tudo isso passo a passo e gera o mesmo arquivo, byte a byte. Motivo: os comandos que o mestre mandou ficam registrados como chegaram, e a reprodução serve de teste de determinismo (RNF02). Descartado: gravar só as entradas da planta em SI (não reproduz formato nem watchdog); uma linha por passo (arquivo 10 vezes maior).
- **D35** (Etapa 5): uma planta sem cliente expira `IdleTimeout` depois da última atividade (padrão: 30 min); até a Etapa 10, expirar é encerrar. Consultas pela API contam como atividade. Os erros da API trazem só um código, que a interface traduz. Motivo: libera memória sem banco de dados, que só chega na Etapa 10.

## Pendências e observações

- **CI no Windows:** na VM compartilhada do GitHub (`windows-latest`, com os projetos de teste rodando em paralelo), uma espera de 2 ms levou 10,8 ms (mediana), embora o temporizador de alta resolução tenha sido criado. O Windows Server escalona com fatias longas. Por isso, no CI, `Windows_UsesTheHighResolutionTimer_…` confere só que o temporizador de alta resolução é usado; a precisão é medida nesta máquina (erro mediano abaixo de 1 ms) e no ensaio de carga. O teste de gravação em tempo real passou a contar escritas, e não tempo, para ter o mesmo tamanho numa máquina lenta.
- **Gravações:** os arquivos ficam na pasta configurada depois que a planta acaba, e ainda não há política para limpá-los (cerca de 130 KB por minuto de ensaio, ou 8 MB por hora: a gravação de 22,8 s do servidor tem 50 KB). Isso deve entrar no histórico de ensaios da Etapa 10.
- **RF07:** a reprodução existe na biblioteca e no `simrun`. A IHM para exportar e reproduzir chega nas Etapas 7 e 8.
