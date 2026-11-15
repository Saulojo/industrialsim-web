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

As seções `io` (mapa Modbus), de instrumentos HART e `visual` entram nas Etapas 4, 16 e 14.

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
