# Validação — Etapa 4: Imagem de I/O, escalas e watchdog

- Branch: `etapa-04-imagem-io`
- Pull request: a abrir depois da confirmação do autor
- Data: 15/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 |
| .NET SDK | 10.0.404 (fixado em `global.json`: 10.0.100 com `rollForward: latestFeature`) |
| CI | GitHub Actions, `ubuntu-latest`; execuções na aba "Checks" do pull request |

Nenhum pacote novo. `IndustrialSim.IO` passou a referenciar `IndustrialSim.Core`, e por isso os `packages.lock.json` de `IO`, `IO.Tests`, `Server` e `Server.Tests` mudaram.

Todos os ensaios usam dt = 0,01 s (D06) e o cenário `scenarios/caldeira/scenario.json` do produto.

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `server/src/IndustrialSim.IO` | `AnalogFormat` e `WordOrder`; `CurrentLoop` (valor ↔ mA pela faixa LRV/URV, limites e correntes de falha da NAMUR NE 43); `AnalogCodec` (mA ↔ contagens nos três formatos inteiros, `float32` ↔ 2 registradores nas duas ordens de palavra). |
| B | `server/src/IndustrialSim.Core/Scenarios`, `scenarios/` | Seção `io` do cenário: registros `IoDefinition`, leitura e validação em `ScenarioLoader.Io.cs`, esquema em `ScenarioSchemaWriter.Io.cs`; mapa da caldeira no `scenario.json`; `scenario.schema.json` regerado; `scenarios/README.md` com o formato da seção. |
| C | `server/src/IndustrialSim.IO` | `IoMap` (mapa resolvido, tamanho das áreas, endereços em base 0 e na notação clássica), `ModbusAddress`, `IoImage` (quatro áreas dimensionadas pelo mapa, com trava e snapshot), `IoSettings` e `PlantIo` (passo sincronizado, troca de configuração em execução, valores forçados, faixa, falha de transmissor e watchdog). |

Ordem de um passo do `PlantIo`:

1. As configurações pendentes entram em vigor; coils e holding registers são copiados da imagem de uma vez e convertidos em entradas da planta; o watchdog é avaliado e, se disparado, impõe o estado seguro.
2. A planta avança um passo.
3. As saídas da planta viram corrente, depois contagens ou `float32`, e são publicadas na imagem de uma vez.

O watchdog conta tempo simulado (passos), e não tempo de relógio, então o resultado é reproduzível (RNF02).

Mudanças em código da Etapa 3, ambas só acrescentam: `ScenarioDefinition` ganhou o campo opcional `Io`, e `ScenarioLoader` aceita o campo `io` na raiz. A seção é opcional e o `schemaVersion` continua 1; os cenários sem `io` seguem válidos (os testes da Etapa 3 passam sem alteração).

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | Conversões de ida e volta erram no máximo 1 contagem em toda a faixa, nos três formatos inteiros (teste por varredura) | `AnalogCodecTests.CountsRoundTrip_…`: toda contagem representável de cada formato → mA → valor → mA → contagem, com duas faixas de instrumento. `AnalogCodecTests.ValueRoundTrip_…`: 100 001 valores ao longo da faixa → contagens → valor | ✅ erro 0 contagem na ida e volta das contagens; no máximo 0,5 contagem na ida e volta do valor |
| 2 | No formato 0–27648, 3,6 mA vira −691 e 21 mA vira 29376; lido como `INT`, o valor se mantém | `AnalogCodecTests.Ne43FaultCurrents_…`: converte, grava no registrador de 16 bits, serializa em big-endian e relê como `INT`. `PlantIoTests.TransmitterFault_…`: o mesmo pela planta, com LT-101 e PT-101 em falha | ✅ −691 (0xFD4D) e 29376 (0x72C0) |
| 3 | `float32` correto nas duas ordens de palavra, com valores de referência fixos | `AnalogCodecTests.Float32_MatchesReferenceBitPatterns_…`: seis valores com o padrão de bits IEEE 754 conhecido | ✅ seis valores nas duas ordens |
| 4 | Trocar o formato com a planta rodando muda os valores no passo seguinte, sem reiniciar | `PlantIoTests.ChangingTheFormatWhileRunning_…`: três trocas (0–10000, `float32`, palavras invertidas) contra uma planta gêmea que fica em 0–27648 | ✅ valor novo no passo seguinte; mesmo hash de estado da gêmea após 400 passos |
| 5 | Um valor forçado ignora a planta até ser liberado | `PlantIoTests.ForcedValue_IgnoresThePlantUntilReleased`: LT-101 forçado em 0,9 m e LSLL-101 em 1 por 2000 passos, com o nível real caindo | ✅ registrador fixo em 20736 e marcado como forçado; ao liberar, volta a seguir a planta |
| 6 | Um comando escrito no meio de um passo só muda a planta no passo seguinte | `PlantIoTests.CommandWrittenInTheMiddleOfAStep_…`: passo feito fase a fase, com a escrita depois da cópia dos comandos, contra uma planta gêmea sem a escrita | ✅ hash igual ao da gêmea nesse passo; diferente no seguinte |
| 7 | Com o watchdog ligado e sem mudança do heartbeat por 2 s: FV-101 vai a 100%, FV-102 a 0% e a entrada discreta 5 sobe; com o heartbeat de volta, os comandos voltam a valer | `PlantIoTests.Watchdog_GoesToTheSafeState_AndRecoversWithTheHeartbeat`, com a bomba ligada e desligada | ✅ não dispara com 199 passos parados; dispara com 200; recupera no passo em que o heartbeat muda |
| 8 | A imagem de I/O da caldeira ocupa menos de 4 KB | `IoImageTests.CaldeiraImage_TakesLessThan4Kb`: soma dos bytes de dados e memória alocada pelo .NET na construção | ✅ 365 bytes de dados; 664 bytes alocados |

## Comandos

```text
$ dotnet restore server/IndustrialSim.sln --locked-mode
$ dotnet build server/IndustrialSim.sln --no-restore --configuration Release -warnaserror
    0 Aviso(s)
    0 Erro(s)

$ cd server
$ dotnet test --no-build --configuration Release
Resumo da execução de teste: Aprovado!
  total: 274
  falhou: 0
  concluído com êxito: 274
  ignorados: 0
```

Eram 195 testes na Etapa 3. Dos 79 novos, 57 estão no `IndustrialSim.IO.Tests` e 22 no `IndustrialSim.Core.Tests`.

As saídas abaixo vêm de:

```text
$ dotnet test --project tests/IndustrialSim.IO.Tests --no-build --configuration Release --output Detailed
```

## 1. Ida e volta das conversões

```text
Counts27648: 74752 contagens varridas, maior erro = 0 contagem(ns)
Counts10000: 131072 contagens varridas, maior erro = 0 contagem(ns)
Counts32767: 131072 contagens varridas, maior erro = 0 contagem(ns)

Counts27648: maior erro = 0,4998 contagem
Counts10000: maior erro = 0,5000 contagem
Counts32767: maior erro = 0,5000 contagem
```

No primeiro bloco, a varredura cobre a faixa inteira de cada formato (−4864 a 32511 no 0–27648; −32768 a 32767 nos outros), com as faixas 0–1,2 e −2,5e5–1,6e6. No segundo, o erro de meia contagem é o do arredondamento à contagem mais próxima.

## 2. Correntes de falha NAMUR NE 43

```text
3,6 mA -> -691 contagens -> registrador 0xFD4D -> INT -691
21 mA -> 29376 contagens -> registrador 0x72C0 -> INT 29376
```

Conta: (3,6 − 4) / 16 × 27648 = −691,2 → −691; (21 − 4) / 16 × 27648 = 29376.

Nos outros formatos (D28): 3,6 mA dá −250 em 0–10000 e −819 em 0–32767; 21 mA dá 10625 em 0–10000 e satura em 32767 em 0–32767.

## 3. `float32` nas duas ordens de palavra

```text
1: normal = 3F80 0000; invertida = 0000 3F80
-2,5: normal = C020 0000; invertida = 0000 C020
123,456: normal = 42F6 E979; invertida = E979 42F6
0,6: normal = 3F19 999A; invertida = 999A 3F19
10: normal = 4120 0000; invertida = 0000 4120
0: normal = 0000 0000; invertida = 0000 0000
```

## 4. Troca de formato com a planta rodando

```text
passo 100: LT-101 = 13821 (0–27648)
passo 101: LT-101 = 5005 (0–10000)
passo 201: LT-101 = 0,59828454 m, PT-101 = 9,991777 bar (float32)
hash com trocas = CA4831CC012BAEEF; sem trocas = CA4831CC012BAEEF
```

A troca é pedida depois do passo 100 e o snapshot continua em 0–27648 até o passo 101. Ao trocar, os comandos que estão nos holding registers são regravados no novo formato (13824 → 5000 → 50,0), para a planta não levar um tranco enquanto o mestre não reescreve os seus valores.

O hash só é igual ao da planta gêmea porque os comandos do teste (50% e 25%) são exatos em todos os formatos usados. Com um comando qualquer, a regravação arredonda para a contagem mais próxima do novo formato, e a planta passa a receber esse valor arredondado.

## 5. Valor forçado

```text
nível real: 0,6000 m -> 0,5925 m; registrador forçado em 20736 por 2000 passos
liberado: registrador = 13617
```

O valor forçado é dado em unidade de engenharia (D30) e vale a partir do passo seguinte. `ForcedValue_SurvivesAFormatChange` confere que 8 bar forçados em PT-101 viram 13824 em 0–27648 e 8,0 em `float32`, com os dois registradores marcados.

## 6. Comando no meio do passo

```text
passo 51 (escrita no meio): comando na planta = 0,8228, hash = 2FFB56ACF7CE0414, gêmea = 2FFB56ACF7CE0414
passo 52: comando na planta = 0,0000, hash = 76A96BA7CF9CD450, gêmea = AF57557F8330D0B1
```

## 7. Watchdog

```text
passo 201 (t = 2,01 s): watchdog disparado = True
30 s depois: FV-101 = 100,00% (27648), FV-102 = 0,00% (0), P-101 = 1, entrada discreta 5 = True
heartbeat de volta: watchdog = False, FV-101 = 0,25, FV-102 = 0,5

passo 201 (t = 2,01 s): watchdog disparado = True
30 s depois: FV-101 = 100,00% (27648), FV-102 = 0,00% (0), P-101 = 0, entrada discreta 5 = True
heartbeat de volta: watchdog = False, FV-101 = 0,25, FV-102 = 0,5
```

O heartbeat muda antes do passo 1. Os passos 2 a 200 veem 199 passos sem mudança e não disparam; o passo 201 completa 200 passos (2,00 s) e dispara. Os holding registers guardam o que o mestre escreveu (6912 e 13824) durante todo o tempo; só deixam de valer enquanto o watchdog está disparado.

Outros testes do watchdog: desligado por padrão e sem disparar em 1000 passos; tempo limite de 0,5 s dispara em 50 passos; desligar com ele disparado tira o estado seguro no passo seguinte.

## 8. Tamanho da imagem

```text
áreas: 1 coils, 6 entradas discretas, 11 holding registers, 110 input registers
bytes de dados: 365
bytes alocados pelo .NET (objeto, trava e vetores): 664
```

O protótipo reservava 65.536 endereços por área, cerca de 400 KB.

## Mapa da caldeira

`IoMapTests.CaldeiraMap_MatchesTheProjectDescription_InBothNotations` compara o mapa com a tabela do descritivo, linha a linha (área, base 0, notação clássica, registradores, sinal, unidade):

```text
Coil                0      1 x1 P-101_run
DiscreteInput       0  10001 x1 P-101_running
DiscreteInput       1  10002 x1 LSLL-101
DiscreteInput       2  10003 x1 LSHH-101
DiscreteInput       3  10004 x1 PSH-101
DiscreteInput       4  10005 x1 PSV-101_open
DiscreteInput       5  10006 x1 watchdog
HoldingRegister     0  40001 x1 FV-101_command %
HoldingRegister     1  40002 x1 FV-102_command %
HoldingRegister    10  40011 x1 heartbeat
InputRegister       0  30001 x1 LT-101 m
InputRegister       1  30002 x1 PT-101 bar
InputRegister       2  30003 x1 FT-101 kg/s
InputRegister       3  30004 x1 FT-102 kg/s
InputRegister       4  30005 x1 FV-101_position %
InputRegister       5  30006 x1 FV-102_position %
InputRegister     100  30101 x2 LT-101 m
InputRegister     102  30103 x2 PT-101 bar
InputRegister     104  30105 x2 FT-101 kg/s
InputRegister     106  30107 x2 FT-102 kg/s
InputRegister     108  30109 x2 FV-103_position %
```

Faixas adotadas (a ajustar com o orientador): LT-101 0–1,2 m; PT-101 0–16 bar absolutos; FT-101 0–4 kg/s; FT-102 0–5 kg/s; válvulas 0–100%.

## Além dos critérios

- **Concorrência.** `ConcurrentWrites_NeverAppearHalfDoneInASnapshot`: quatro threads escrevendo pares de registradores, 1 094 577 snapshots, nenhum com escrita pela metade. `SnapshotsWhileThePlantRuns_AreAlwaysFromASingleStep`: planta avançando numa thread e um mestre escrevendo em outra, 155 350 passos e 2 617 278 snapshots, nenhum misturando dois passos. Os números variam a cada execução.
- **Seção `io` inválida.** `IoSectionTests`: 19 casos (endereço ausente, fora da faixa ou repetido; sinal inexistente; entrada comandada por dois pontos; faixa nula; diagnóstico ou heartbeat sobre os sinais no arranjo `float32`; estado seguro repetido…), cada um com o caminho do campo e a mensagem, e o erro vindo sozinho.
- **Faixa e saturação.** `Range_ChangesTheCounts_…`: 0,6 m lidos em 0–2,4 m dão 6912; acima da faixa a corrente para em 20,5 mA (28512) e abaixo em 3,8 mA (−346).
- **Comandos.** `Commands_AreClampedToTheirRange_…`: o comando é limitado à faixa (32000 contagens valem 100%; −691 valem 0%); em `float32` o comando é em % e um NaN é ignorado.
- **Bloco de diagnóstico.** `DiagnosticsBlock_CanBeSwitchedOff`: desligado, os registradores 100 a 109 ficam em zero.

## Pendências e pontos para decisão

- As faixas dos transmissores são valores adotados, a confirmar com o orientador.
- A falha de transmissor e a troca de faixa existem só como métodos do `PlantIo`. A tela (Etapa 8) e o HART (Etapa 16) é que vão chamá-los.
- O bloco de diagnóstico publica as medições dos transmissores, com ruído, e não o valor interno da planta.
- Com o watchdog ligado desde o início e nenhum mestre conectado, a planta vai ao estado seguro depois do tempo limite.
- O acesso à imagem por Modbus de verdade (exceção de endereço ilegal, Unit ID) é da Etapa 6; aqui a imagem só devolve falso para um endereço fora do mapa.

## Decisões propostas para o descritivo

| ID | Decisão | Motivo |
| --- | --- | --- |
| D27 | No formato `float32`, o sinal analógico de endereço `a` ocupa os registradores `2a` e `2a+1`. Heartbeat e bloco de diagnóstico não mudam de endereço, e o cenário é recusado se eles caírem sobre os sinais nesse arranjo. | Um `float32` precisa de 2 registradores e os endereços 0 a 5 do mapa são consecutivos; a regra é simples de explicar e de configurar no mestre. |
| D28 | A medição satura em 3,8 e 20,5 mA; 3,6 e 21 mA só aparecem com o transmissor em falha (NAMUR NE 43). No formato 0–27648 as contagens vão de −4864 a 32511; nos formatos 0–10000 e 0–32767, o valor é limitado ao que cabe em `INT`. | Comportamento de transmissor real; o mestre distingue medição fora da faixa de instrumento em falha. |
| D29 | A faixa (LRV/URV) fica na entrada `io` de cada registrador, na unidade do sinal da planta; `unit` e `unitFactor` dão a unidade de engenharia (valor de engenharia = valor do sinal × fator), usada no `float32`, no bloco de diagnóstico e nos valores forçados. | A planta continua em SI (D06) e o usuário vê bar e %. |
| D30 | O valor forçado é guardado em unidade de engenharia (ou booleano), passa pela faixa e pelo formato atuais e tem prioridade sobre a planta e sobre a falha do transmissor. | Continua válido quando o formato muda. |
| D31 | Ao trocar o formato analógico ou a ordem das palavras, os comandos que estão nos holding registers são regravados no novo formato. | Sem isso, as contagens antigas seriam lidas no formato novo e as válvulas dariam um salto até o mestre reescrever. |
| D32 | O watchdog conta tempo simulado (passos da planta), e não tempo de relógio. | Determinismo (RNF02): uma gravação reproduzida dispara o watchdog no mesmo passo. |
