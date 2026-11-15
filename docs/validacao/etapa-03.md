# Validação — Etapa 3: Motor de cenários

- Branch: `etapa-03-cenarios`
- Pull request: [Saulojo/industrialsim-web#4](https://github.com/Saulojo/industrialsim-web/pull/4)
- Data: 15/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 |
| .NET SDK | 10.0.404 (fixado em `global.json`: 10.0.100 com `rollForward: latestFeature`) |
| CI | GitHub Actions, `ubuntu-latest`; execuções na aba "Checks" do pull request |

Nenhum pacote novo: o leitor de cenário usa o `System.Text.Json` do próprio .NET, e o validador é do projeto.

Todos os ensaios usam dt = 0,01 s (D06).

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `server/src/IndustrialSim.Core/Scenarios` | Leitor e validador do `scenario.json` (`ScenarioLoader`), registro de tipos de componente (`ComponentRegistry`, `StandardComponents`), montagem e execução da planta (`ScenarioPlant`), ordem de execução pelas ligações (`ExecutionOrder`) e gerador do esquema JSON (`ScenarioSchemaWriter`). |
| B | `server/src/IndustrialSim.Core/Components` | `Valve` (linear e igual-percentual), `Drum` (tubulão v1), `SteamPressure` (pressão v1), `Burner`, `Transmitter` (amortecimento, ruído, quantização), `ThresholdSwitch` (chaves de nível e pressão) e `ReliefValve`. |
| C | `scenarios/`, `server/src/IndustrialSim.SimRun` | Cenário `caldeira` v1 em JSON, esquema `scenario.schema.json` e a linha de comando `simrun`. |

Mudanças em código da Etapa 1, ambas só acrescentam:

- `Tank` ganhou a saída `meanOutflow`: a vazão média que saiu pelo orifício no último passo, tirada do balanço de volume do próprio tanque. É ela que se liga à entrada de outro tanque. O estado e os resultados da Etapa 1 não mudam (os 89 testes da Etapa 1 continuam passando sem alteração).
- `Component` ganhou `Initialize()`, chamado uma vez pelo motor depois de ligar os sinais (o filtro de um transmissor começa no valor medido; uma chave começa no estado certo).

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | O tanque descrito em JSON gera resultado idêntico, bit a bit, ao modelo codificado da Etapa 1 para o mesmo roteiro | `WaterTankScenarioTests.JsonTank_IsBitIdenticalToCodedPlant`: `water-tank.json` contra o `WaterTankPlant` da Etapa 1, no roteiro de 600 s do teste de determinismo da Etapa 1; compara a cada passo os bits do nível, da medição com ruído e do hash de estado | ✅ 60 000 passos idênticos; mesmos hashes |
| 2 | Um cenário de dois tanques em série é criado só com JSON, sem alterar código, e conserva massa (erro < 1e-6) | `TwoTanksScenarioTests.TwoTanksInSeries_ConserveMass`: `two-tanks.json`, 600 s com entradas mudando a cada 30 s; integral calculada no teste, fora da planta | ✅ erro relativo = 2,4e-10 |
| 3 | Dez casos de JSON inválido geram erro claro com o caminho do campo | `InvalidScenarioTests`: 23 casos, cada um partindo do cenário válido com um defeito; confere o caminho, trechos da mensagem e que o erro vem sozinho | ✅ 23 casos (lista abaixo) |
| 4 | Caldeira v1 com degraus coerentes: mais água sobe o nível; mais combustível sobe a pressão; mais vapor baixa a pressão | `CaldeiraScenarioTests`: três testes de sinal sobre `scenarios/caldeira/scenario.json`, cada um nos dois sentidos, comparando com a mesma planta sem degrau | ✅ sinais corretos nos seis degraus |
| 5 | A válvula igual-percentual segue a curva teórica com erro < 1% | `ValveTests.EqualPercentage_FollowsTheoreticalCurve`: válvula criada por JSON, comandos de 5% a 100%, vazão em regime contra R^(x − 1) | ✅ maior erro = 8,9e-6 (0,0009%) |

## Comandos

```text
$ cd server
$ dotnet build IndustrialSim.sln --configuration Release -warnaserror
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)

$ dotnet test --no-build --configuration Release
Resumo da execução de teste: Aprovado!
  total: 195
  falhou: 0
  concluído com êxito: 195
  ignorados: 0
```

Eram 94 testes na Etapa 1. Dos 101 novos, 78 estão no `IndustrialSim.Core.Tests` e 23 no novo `IndustrialSim.SimRun.Tests`.

Para ver os números medidos de uma classe de teste:

```text
$ dotnet test --project tests/IndustrialSim.Core.Tests --filter-class IndustrialSim.Core.Tests.Scenarios.CaldeiraScenarioTests --output Detailed
```

## 1. Tanque em JSON contra o modelo codificado

```text
código (Etapa 1): estado = 744989c7a55bff97; medição = 8e8e1e2384b760f6; nível final = 1,6942443956940914 m
JSON   (Etapa 3): estado = 744989c7a55bff97; medição = 8e8e1e2384b760f6; nível final = 1,6942443956940914 m
ordem de execução do JSON: P-101 → FV-101 → TQ-101 → LT-101
```

Os hashes são acumulados nos 60 000 passos: o de estado cobre o número do passo e os estados de bomba, válvula e tanque; o de medição cobre o nível medido, com ruído (semente 2026). Além dos hashes, o teste compara os bits do nível e da medição em cada passo.

O JSON usa os tipos `pump`, `valve` (linear), `tank` e `transmitter`. Para os bits coincidirem:

- a válvula integra a posição com a mesma expressão do `FirstOrderActuator` (conferido bit a bit em `ValveTests.Actuator_MatchesFirstOrderActuator`);
- a vazão que entra no tanque é vazão da bomba × posição da válvula, com os valores do fim do passo, como no modelo codificado;
- o primeiro transmissor do cenário recebe a própria semente do cenário, e sorteia uma vez por passo.

Além do critério: inverter a ordem dos componentes no arquivo não muda o resultado (`ComponentOrderInFile_DoesNotChangeResult`), e outra semente muda só a medição (`DifferentSeed_ChangesOnlyNoise`).

## 2. Dois tanques em série

```text
ΔV total = 0,9741979461327874 m³; ∫(Qin − Qout)dt = 0,9741979463631152 m³; erro relativo = 2,364E-010
∫Qin dt = 7,220466392082704 m³; ∫Qout dt = 6,246268445719589 m³; passou do 1º para o 2º tanque: 6,681002421042599 m³
níveis entre 0,7893 m e 2,0294 m; ordem de execução: TQ-1 → TQ-2
```

O cenário é `server/tests/IndustrialSim.Core.Tests/Scenarios/Files/two-tanks.json`: dois componentes `tank` e uma ligação `TQ-1.meanOutflow → TQ-2.inflow`. Nenhuma classe foi criada para ele.

Qin é a entrada do primeiro tanque e Qout a saída do segundo; como na Etapa 1, Qout é integrada pela regra do trapézio, no teste. O ensaio só vale se nenhum tanque saturar, e isso é conferido.

Segundo teste, da ligação em si (`WaterLeavingFirstTank_EntersSecond`), com o segundo tanque fechado:

```text
saiu do 1º: 2,396386168027568 m³; chegou ao 2º: 2,396386168028142 m³; diferença = 5,742E-013 m³
```

## 3. JSON inválido

Cada caso gera um único erro, no caminho indicado:

```text
components[0].type: tipo de componente desconhecido: 'turbine'. Disponíveis: pump, valve, tank, drum, burner, steamPressure, transmitter, switch, reliefValve.
connections[1].to: o componente 'TQ-101' não tem entrada 'vazao'. Disponíveis: inflow, outletOpening.
connections[0].from: componente inexistente: 'P-999'. Disponíveis: P-101, FV-101, TQ-101, LT-101.
components[2].params.areaM2: valor -1 fora da faixa: deve ser > 0 (área da seção transversal, em m²).
components[2].params.heightM: campo obrigatório ausente (altura do tanque, em m).
components[0].params.ratedFlow: esperado um número (vazão nominal, na unidade de vazão do cenário (m³/s ou kg/s)); recebido o texto "muito".
components[2].params.areaM3: campo desconhecido para o tipo 'tank'. Aceitos: areaM2, heightM, outletAreaM2, dischargeCoefficient.
components[1].params.characteristic: valor inválido: recebido o texto "parabolic". Opções: linear, equalPercentage.
components[1].initial.position: valor 1.5 fora da faixa: deve ser ≥ 0 e ≤ 1 (posição inicial).
components[1].id: o identificador 'P-101' já foi usado em components[0].
schemaVersion: versão 99 não suportada; este código lê a versão 1.
schemaVersion: campo obrigatório ausente; este código lê a versão 1.
componentes: campo desconhecido. Aceitos aqui: $schema, schemaVersion, id, description, stepSeconds, noiseSeed, components, connections, inputs, outputs.
connections[3].to: a entrada 'TQ-101.inflow' já recebe a saída 'FV-101.flow'; uma entrada só pode ter uma fonte.
inputs[0].to[1]: a entrada 'FV-101.inletFlow' já recebe a saída 'P-101.flow'; uma entrada só pode ter uma fonte.
outputs[0].from: o componente 'TQ-101' não tem saída 'nivel'. Disponíveis: level, outflow, overflow, meanOutflow.
connections[0].from: 'P-101' não está no formato 'componente.sinal'.
stepSeconds: valor 0 fora da faixa: deve ser > 0 (segundos).
outputs[1].name: o nome 'pumpRun' já foi usado em inputs[0].name; entradas e saídas externas não podem repetir nomes.
components: o cenário precisa de pelo menos um componente.
connections: esperada uma lista; recebido o texto "P-101.flow -> FV-101.inletFlow".
connections: laço algébrico entre componentes sem estado (A, B): a saída de cada um depende, no mesmo instante, da saída do outro. Ponha no laço um componente com dinâmica (ex.: uma válvula ou um transmissor com amortecimento).
components[4].params: As pressões devem obedecer contrapressão (101325 Pa) < fechamento (1200000 Pa) < ajuste (1000000 Pa).
```

Além dos 23 casos: texto que não é JSON aponta o documento (`$`) com linha e coluna (`MalformedJson_ReportsLineAndColumn`), e vários defeitos independentes saem todos de uma vez (`SeveralProblems_AreAllReported`):

```text
Cenário inválido (4 erros):
  components[0].params.ratedFlow: valor -0.03 fora da faixa: deve ser ≥ 0 (vazão nominal, na unidade de vazão do cenário (m³/s ou kg/s)).
  components[2].params.areaM2: valor 0 fora da faixa: deve ser > 0 (área da seção transversal, em m²).
  connections[2].to: o componente 'LT-101' não tem entrada 'entrada'. Disponíveis: value.
  outputs[0].from: componente inexistente: 'TQ-999'. Disponíveis: P-101, FV-101, TQ-101, LT-101.
```

O esquema `scenarios/scenario.schema.json` é gerado do mesmo registro de tipos que o validador usa, e `ScenarioEngineTests.SchemaFile_MatchesGeneratedSchema` falha se o arquivo divergir do gerador.

## 4. Degraus na caldeira v1

Os testes leem os valores reais da planta (sem o ruído dos transmissores) e comparam duas plantas lado a lado, uma com o degrau e outra sem. Além do sinal do desvio no fim, conferem que ele não troca de sinal em nenhum passo depois do primeiro segundo.

```text
FV-101 +0,05: nível em 120 s = 0,61196 m (sem degrau: 0,60000 m); Δ = +0,01196 m
FV-101 -0,05: nível em 120 s = 0,59014 m (sem degrau: 0,60000 m); Δ = -0,00986 m
FV-102 +0,05: pressão em 300 s = 10,8930 bar (sem degrau: 10,0017 bar); Δ = +0,8913 bar
FV-102 -0,05: pressão em 300 s = 9,1104 bar (sem degrau: 10,0017 bar); Δ = -0,8913 bar
FV-103 +0,10: pressão em 300 s = 8,5075 bar (sem degrau: 10,0017 bar); Δ = -1,4942 bar
FV-103 -0,10: pressão em 300 s = 12,2065 bar (sem degrau: 10,0017 bar); Δ = +2,2048 bar
```

Os números batem com a conta à mão do modelo: +5% em FV-102 dá +10% de vapor gerado, e ΔP = 0,2 kg/s ÷ (0,5 × 4,45e-6 kg/(s·Pa)) = 0,90 bar; em 300 s (5τ) a resposta chegou a 99% disso.

Outros ensaios do cenário:

```text
ordem de execução: P-101 → FV-101 → FV-102 → FV-103 → Q-101 → TB-101 → LT-101 → FT-101 → LSLL-101 → LSHH-101 → TB-101-V → PSV-101 → PT-101 → FT-102 → PSH-101
nível: 0,60000 m → 0,59999 m; pressão: 10,0000 bar → 10,0017 bar            (600 s sem mexer em nada)
água = 1,99987 kg/s; vapor gerado = 1,99993 kg/s; vapor na saída = 1,99993 kg/s
PSH-101 atuou em 11,76 s; PSV-101 abriu em 15,73 s e abriu 32 vezes em 300 s; pressão máxima = 13,0030 bar (ajuste 13 bar)
LSLL-101 atuou em t = 640,66 s, com nível = 0,30000 m                         (bomba desligada)
execução 1: estado = 04ddf497aad72ca1; medições = 36e2f9b4f22fe18e
execução 2: estado = 04ddf497aad72ca1; medições = 36e2f9b4f22fe18e
semente 999: medições = ecef3791ee0cc0e1
```

O laço entre a pressão (`TB-101-V`) e a válvula de alívio (`PSV-101`) passa por estado: a pressão avança primeiro e a vazão de alívio entra no passo seguinte.

## 5. Válvula igual-percentual

R = 50, vazão nominal de 4 kg/s, τ = 2 s; cada ponto é lido 20 s (10τ) depois do comando.

```text
comando   vazão medida   vazão teórica   erro relativo
    5%       0,097282        0,097283   8,880E-006
   10%       0,118300        0,118301   8,881E-006
   20%       0,174936        0,174938   8,881E-006
   30%       0,258689        0,258691   8,881E-006
   40%       0,382538        0,382541   8,881E-006
   50%       0,565680        0,565685   8,881E-006
   60%       0,836504        0,836512   8,881E-006
   70%       1,236987        1,236998   8,881E-006
   80%       1,829204        1,829220   8,881E-006
   90%       2,704949        2,704973   8,881E-006
  100%       3,999964        4,000000   8,881E-006
maior erro relativo = 8,881E-006 (limite 1%)
```

(O teste percorre de 5% em 5%; a tabela mostra parte das linhas.) O erro é o resto do transitório do atuador: em 10τ faltam e⁻¹⁰ do degrau de 5% na posição, e isso vezes ln 50 dá 8,9e-6. A curva teórica é calculada no teste por `exp` e `log`, não pela função da válvula.

A igual-percentual teórica não fecha (f(0) = 1/R = 2%). Abaixo de 2% do curso a curva do modelo desce em linha reta até zero, para que comando 0 seja válvula fechada (`EqualPercentage_ClosesAtZero`). É escolha de modelagem; o critério vale de 2% a 100%.

## Linha de comando `simrun`

Roteiro `docs/validacao/etapa-03/roteiro-caldeira.csv` (degraus em FV-101, FV-102 e FV-103), resultado em `docs/validacao/etapa-03/resultado-caldeira.csv`:

```text
$ dotnet run --configuration Release --project src/IndustrialSim.SimRun -- ../scenarios/caldeira/scenario.json --entradas ../docs/validacao/etapa-03/roteiro-caldeira.csv --saida ../docs/validacao/etapa-03/resultado-caldeira.csv --intervalo 10
Cenário 'caldeira': 15 componentes, passo de 0.01 s.
Entradas: P-101_run, FV-101_command, FV-102_command, FV-103_command
Saídas: LT-101, PT-101, FT-101, FT-102, FV-101_position, FV-102_position, FV-103_position, P-101_running, LSLL-101, LSHH-101, PSH-101, PSV-101_open
Ordem de execução: P-101 -> FV-101 -> FV-102 -> FV-103 -> Q-101 -> TB-101 -> LT-101 -> FT-101 -> LSLL-101 -> LSHH-101 -> TB-101-V -> PSV-101 -> PT-101 -> FT-102 -> PSH-101
Simulados 144000 passos (1440 s); 145 linhas em ../docs/validacao/etapa-03/resultado-caldeira.csv.
Hash do estado (acumulado em todos os passos): 5d4ca055a89162fc
```

Uma segunda execução deu o mesmo hash e um arquivo idêntico byte a byte (`cmp`). Trecho do resultado (medições dos transmissores, com ruído):

| t (s) | FV-101 | FV-102 | FV-103 | LT-101 (m) | PT-101 (Pa) | FT-101 (kg/s) | FT-102 (kg/s) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 0,8228 | 0,50 | 0,5 | 0,6000 | 1 000 000 | 2,000 | 2,000 |
| 180 | 0,8728 | 0,50 | 0,5 | 0,6115 | 1 000 437 | 2,427 | 2,003 |
| 540 | 0,8228 | 0,55 | 0,5 | 0,5989 | 1 089 316 | 2,006 | 2,200 |
| 840 | 0,8228 | 0,50 | 0,5 | 0,5990 | 1 000 233 | 1,995 | 2,008 |
| 1140 | 0,8228 | 0,50 | 0,6 | 0,5985 | 850 462 | 1,999 | 2,000 |
| 1440 | 0,8228 | 0,50 | 0,5 | 0,5972 | 999 261 | 2,000 | 2,001 |

Cada linha mostra o estado no fim do passo e as entradas que valeram nele. Mais água sobe o nível (180 s), mais combustível sobe a pressão (540 s) e mais vapor baixa a pressão (1140 s).

Os testes do `simrun` (`SimRunTests`, `InputScriptTests`) conferem que o CSV traz os mesmos valores de rodar a planta direto, que cenário inválido sai com os caminhos dos campos e código de saída 1, e que argumentos errados mostram o uso.

## Pendências e pontos para decisão

| Item | Situação |
| --- | --- |
| Valores de tabela do cenário | Poder calorífico do metano, entalpias e densidade a 1 MPa foram citados de Çengel & Boles (tabelas A-27, A-4, A-5) de memória. Conferir no livro antes de usar em texto de pesquisa. |
| Parâmetros da caldeira | Ilustrativos (caldeira de 7,2 t/h, τ de pressão de 60 s), a ajustar com o orientador. |
| Mensagens em português no núcleo | As mensagens de validação de cenário e do `simrun` estão em português no código, como as exceções da Etapa 1. São para quem escreve cenários, não texto da interface web. Se forem aparecer no site, passam para os arquivos de idioma na etapa que as exibir. |

## Decisões propostas para o descritivo

| ID | Decisão | Motivo |
| --- | --- | --- |
| D23 | Validação do `scenario.json` por código próprio (`ScenarioLoader`, `ScenarioPlant`), com o esquema JSON gerado do registro de tipos e conferido por teste. | Mensagens em português com o caminho do campo e sem dependência nova; o esquema serve ao editor e não diverge do validador. |
| D24 | Ordem de execução calculada pelas ligações; num laço, a ligação atrasada de um passo entra num componente com estado; laço sem estado é erro. | A ordem do arquivo deixa de importar, e o resultado é determinístico. |
| D25 | Cada transmissor tem gerador de ruído próprio, com semente `noiseSeed + k` (k = ordem do transmissor no arquivo). | Acrescentar um instrumento não muda o ruído dos anteriores. |
| D26 | Unidades de vazão: `tank` em m³/s; `drum`, `burner` e `steamPressure` em kg/s; `pump` e `valve` na unidade do cenário. | O bloco de diagnóstico do descritivo é em kg/s; o tanque da Etapa 1 fica como estava. |

Linha de "Estado atual" proposta: "Etapas 1 a 3 concluídas (Etapa 3 em 15/11/2026); próxima: Etapa 4 (imagem de I/O, escalas e watchdog)."
