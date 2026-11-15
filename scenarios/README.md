# Cenários

Cada subpasta é um pacote de cenário (D04): `scenario.json` e, a partir da Etapa 14, `model.glb`.

- `scenario.schema.json`: esquema JSON do `scenario.json` (versão 1). É gerado a partir do registro de tipos de componente; não edite à mão. Para regerar: `dotnet run --project server/src/IndustrialSim.SimRun -- --esquema scenarios/scenario.schema.json`.
- `caldeira/`: caldeira v1 (Fase 1). Parâmetros e fontes em `caldeira/README.md`.

## Formato do `scenario.json` (versão 1)

| Campo | Conteúdo |
| --- | --- |
| `schemaVersion` | Versão do formato: `1`. |
| `id`, `description` | Identificador do cenário e uma nota para quem edita o arquivo (não aparece na interface). |
| `stepSeconds` | Passo fixo de integração; padrão 0,01 s (D06). |
| `noiseSeed` | Semente do ruído dos instrumentos (D22). |
| `components` | Lista de componentes: `id` (a tag), `type`, `params` e `initial` (condições iniciais). |
| `connections` | Ligações `{ "from": "P-101.flow", "to": "FV-101.inletFlow" }`: a saída de um componente alimenta a entrada de outro. Cada entrada só tem uma fonte. |
| `inputs` | Entradas externas (comandos e cargas): `name`, `to` (entradas de componentes que alimenta) e `initial`. |
| `outputs` | Saídas externas (medições e estados): `name` e `from`. |

| `io` | Mapa Modbus (opcional): ver abaixo. |

As seções de instrumentos HART e `visual` entram nas Etapas 16 e 14.

## Seção `io` (mapa Modbus)

Liga endereços Modbus, em base 0, às entradas e saídas externas do cenário. A imagem de I/O é dimensionada por este mapa.

| Campo | Conteúdo |
| --- | --- |
| `coils` | O mestre escreve: `address` e `signal` (uma entrada externa, que recebe 0 ou 1). |
| `discreteInputs` | O mestre lê: `address` e `signal` (uma saída externa; ≥ 0,5 vale 1). |
| `holdingRegisters` | O mestre escreve um comando 4–20 mA: `address`, `signal` (entrada externa), `lrv` e `urv` (valor da entrada em 4 e 20 mA; padrão 0 e 1), `unit` e `unitFactor`. O comando é limitado à faixa. |
| `inputRegisters` | O mestre lê uma medição 4–20 mA: `address`, `signal` (saída externa), `lrv` e `urv` (valor do sinal, na unidade da planta, em 4 e 20 mA), `unit` e `unitFactor`. |
| `diagnostics` | Bloco em unidades de engenharia nos input registers: `address` do primeiro registrador e `values` (`signal`, `unit`, `unitFactor`), um `float32` em 2 registradores por valor. |
| `watchdog` | `heartbeatRegister` (holding register), `statusDiscreteInput` (vai a 1 quando dispara), `timeoutSeconds` (padrão 2) e `safeState`: lista de `{ "input", "value" }` com o valor seguro de cada entrada externa. As entradas fora da lista ficam como estavam. |

Regras:

- `unit` e `unitFactor` dão a unidade de engenharia: valor de engenharia = valor do sinal × `unitFactor` (ex.: `1e-5` de Pa para bar, `100` de fração para %). É a unidade do formato `float32`, do bloco de diagnóstico e dos valores forçados.
- A medição vira corrente pela faixa e satura em 3,8 e 20,5 mA; 3,6 e 21 mA só aparecem com o transmissor em falha (NAMUR NE 43).
- Nos formatos inteiros (0–27648, 0–10000, 0–32767), o sinal de endereço `a` fica no registrador `a`. No formato `float32`, ele ocupa `2a` e `2a+1`. Por isso o heartbeat e o bloco de diagnóstico precisam ficar acima de `2 × (maior endereço) + 1`, e o endereço de um sinal analógico vai até 32767.
- Uma entrada externa só pode ser comandada por um ponto (coil ou holding register).

## Tipos de componente

Unidades SI. A física e as fontes de cada tipo estão na classe correspondente, em `server/src/IndustrialSim.Core/Components`.

| Tipo | Entradas | Saídas | O que modela |
| --- | --- | --- | --- |
| `pump` | `run` | `flow`, `running` | Bomba liga/desliga, com partida de 1ª ordem. |
| `valve` | `command`, `inletFlow` | `position`, `opening`, `flow` | Válvula com atuador de 1ª ordem; característica linear ou igual-percentual. |
| `tank` | `inflow`, `outletOpening` | `level`, `outflow`, `overflow`, `meanOutflow` | Tanque atmosférico com saída por orifício (Torricelli). |
| `drum` | `feedwaterFlow`, `heat` | `level`, `steamGeneration`, `overflow`, `waterMass` | Tubulão v1: balanço de massa da água. |
| `burner` | `fuelFlow` | `heat` | Queimador e fornalha, com atraso de 1ª ordem. |
| `steamPressure` | `steamGeneration`, `outletOpening`, `reliefFlow` | `pressure`, `steamFlow` | Modelo de pressão v1, de 1ª ordem. |
| `transmitter` | `value` | `measurement` | Amortecimento, ruído com semente e quantização. |
| `switch` | `value` | `active` | Chave de nível ou pressão, com banda morta. |
| `reliefValve` | `pressure` | `open`, `flow` | Válvula de alívio de ação rápida. |

## Regras do motor

- Em cada passo, as entradas externas são copiadas para os componentes; depois cada componente avança, na ordem dada pelas ligações (quem fornece um sinal avança antes de quem o consome).
- Num laço de realimentação, a ligação que fecha o laço entrega o valor do passo anterior, e entra num componente com estado. Um laço só de componentes sem estado (laço algébrico) é recusado.
- A planta não tem controlador: só reage às entradas.

## Rodar um cenário sem servidor

```text
cd server
dotnet run --project src/IndustrialSim.SimRun -- ../scenarios/caldeira/scenario.json --entradas roteiro.csv --saida resultado.csv
```

O roteiro é um CSV com a coluna `time` (segundos) e uma coluna por entrada externa; cada linha vale a partir do seu instante, e célula vazia mantém o valor anterior. Sem `--saida`, o comando só confere o cenário e mostra entradas, saídas e ordem de execução.
