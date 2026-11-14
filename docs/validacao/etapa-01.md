# Validação — Etapa 1: Núcleo de simulação

- Branch: `etapa-01-nucleo`
- Pull request: [Saulojo/industrialsim-web#2](https://github.com/Saulojo/industrialsim-web/pull/2)
- Data: 14/11/2026

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 |
| .NET SDK | 10.0.404 (fixado em `global.json`: 10.0.100 com `rollForward: latestFeature`) |
| CI | GitHub Actions, `ubuntu-latest`; execuções na aba "Checks" do pull request |

Nenhum pacote novo: o núcleo usa só a biblioteca padrão do .NET; os testes continuam com xunit.v3 4.0.3.

Todos os ensaios usam dt = 0,01 s (D06).

## Critérios

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | RK4: dy/dt = −y em t = 1 s, com dt = 0,01 s, coincide com e⁻¹ com erro < 1e-8 | `Rk4Tests.Scalar_ExponentialDecay_MatchesExpMinusOne` e a versão vetorial | ✅ erro = 3,091e-11 |
| 2 | O atuador de 1ª ordem chega a 63,2% do degrau em t = τ (erro < 0,5%) | `FirstOrderActuatorTests.StepResponse_Reaches63PercentAtTimeConstant`, com τ = 0,1, 1, 5 e 30 s | ✅ erro = 0,019% nos quatro casos |
| 3 | Tanque: a variação de volume iguala ∫(Qin − Qout)dt, com erro relativo < 1e-6 em 600 s | `TankTests.VolumeChange_EqualsIntegralOfNetFlow`: 60 000 passos com entradas mudando a cada 30 s; integral calculada no teste, fora do tanque | ✅ erro relativo = 9,6e-13 |
| 4 | Bomba desligada e saída aberta: o nível cai | `TankTests.PumpOffAndOutletOpen_LevelFalls`: a bomba nunca é ligada e o nível tem de cair em cada um dos 3000 passos | ✅ 1,0000 m → 0,3538 m em 30 s |
| 5 | O nível nunca fica negativo nem passa da altura do tanque | `TankTests.Draining_NeverGoesBelowZero`, `Filling_NeverExceedsHeight` e `RandomInputs_LevelStaysWithinTank`, com a checagem em todos os passos | ✅ 500 000 passos aleatórios, tocando os dois limites |
| 6 | Duas execuções com as mesmas entradas e semente geram o mesmo hash de estado; outra semente muda só o ruído | `WaterTankPlantTests.SameInputsAndSeed_ProduceSameHashes` e `DifferentSeed_ChangesOnlyNoise`, 60 000 passos cada | ✅ hashes abaixo |
| 7 | Funções de transferência de 1ª e 2ª ordem convertidas para espaço de estados: a resposta ao degrau coincide com a solução analítica (erro < 1e-6) | `TransferFunctionTests`: 1ª ordem (3 casos) e 2ª ordem sub, criticamente e superamortecida (5 casos); erro máximo em todos os passos | ✅ pior caso = 5,787e-7 |

## Comandos

```text
$ cd server
$ dotnet build IndustrialSim.sln --configuration Release -warnaserror
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)

$ dotnet test --no-build --configuration Release
Resumo da execução de teste: Aprovado!
  total: 94
  falhou: 0
  concluído com êxito: 94
  ignorados: 0
```

Dos 94 testes, 89 são do `IndustrialSim.Core.Tests` (eram 1 na Etapa 0).

Para ver os números medidos de uma classe de teste:

```text
$ dotnet test --project tests/IndustrialSim.Core.Tests --filter-class IndustrialSim.Core.Tests.Components.TankTests --output Detailed
```

## 1. RK4

```text
y(1) = 0,3678794412023553; exp(-1) = 0,36787944117144233; erro = 3,091E-011
```

Além do critério: a ordem de convergência medida fica entre 3,8 e 4,2 (`Scalar_ConvergenceOrder_IsFour`); o oscilador harmônico confere com cos t e sen t em t = 10 s (erro < 1e-8); dy/dt = t³ é integrado exatamente; e as versões escalar e vetorial dão resultado idêntico bit a bit.

## 2. Atuador de 1ª ordem

```text
tau = 0,1 s: posição em t = tau = 0,6321202255875017; erro vs. 63,2% = 0,0190%; erro vs. 1 - 1/e = 3,332E-007
tau = 1 s:   posição em t = tau = 0,6321205587976448; erro vs. 63,2% = 0,0191%; erro vs. 1 - 1/e = 3,091E-011
tau = 5 s:   posição em t = tau = 0,6321205588285086; erro vs. 63,2% = 0,0191%; erro vs. 1 - 1/e = 4,907E-014
tau = 30 s:  posição em t = tau = 0,6321205588285572; erro vs. 63,2% = 0,0191%; erro vs. 1 - 1/e = 4,441E-016
```

O erro de 0,019% é a diferença entre 63,2% (valor arredondado do critério) e 1 − e⁻¹ = 63,212%. Contra a solução exata, o erro do RK4 cresce com (dt/τ)⁴ e fica em 3,3e-7 no pior caso testado (dt/τ = 0,1).

## 3. Conservação no tanque

Tanque de 2 m² × 3 m, saída de 0,005 m² (Cd = 0,61), nível inicial de 1,5 m. A cada 30 s mudam a vazão de entrada (0,004 a 0,020 m³/s) e a abertura da saída (0,2 a 1,0).

```text
ΔV = 0,5394639710409699 m³; ∫(Qin − Qout)dt = 0,5394639710414877 m³; erro relativo = 9,599E-013
∫Qin dt = 7,220466392082704 m³; ∫Qout dt = 6,681002421041216 m³; nível entre 1,4346 m e 2,0294 m
```

A integral é independente do tanque: Qin é constante em cada passo (soma exata) e Qout é integrada pela regra do trapézio, com a vazão lida no início e no fim de cada passo. O teste também exige que o tanque não sature no ensaio, para o resultado medir o integrador e não os limites.

Com o tanque transbordando, o balanço fecha quando o transbordo entra na conta (`VolumeChange_EqualsIntegralOfNetFlow_WhenOverflowing`):

```text
ΔV = 0,5 m³; transbordo = 2,1839966502089623 m³; erro relativo = 2,404E-009
```

Além do critério: o esvaziamento confere com a solução analítica de Torricelli, √h(t) = √h₀ − Cd·a·√(2g)·t/(2A), com erro < 1e-10 em 60 s; e o nível de regime confere com h = (Qin/(Cd·a·x))²/(2g), com erro < 1e-9.

## 4. Bomba desligada

```text
nível: 1,0000 m -> 0,3538 m em 30 s, com a bomba desligada
```

O problema do protótipo (bomba forçada ligada a cada passo) tem testes de regressão próprios: `PumpTests.NeverCommanded_StaysOff`, `PumpTests.StopCommand_BringsFlowBackToZeroAndKeepsItThere` e `TankTests.PumpOnThenOff_LevelRisesThenFalls`.

## 5. Limites de nível

```text
500000 passos: 222 com o tanque vazio, 288092 com o tanque cheio
```

Entradas aleatórias com semente fixa, inclusive fora da faixa (vazão negativa, abertura abaixo de 0 e acima de 1). Em todos os passos: 0 ≤ nível ≤ altura, e vazões de saída e de transbordo não negativas.

## 6. Determinismo

Planta de referência (`tests/IndustrialSim.Core.Tests/ReferencePlants/WaterTankPlant.cs`): bomba, válvula de entrada, tanque e medição de nível com ruído gaussiano de 2 mm. Roteiro de 600 s em malha aberta, com a bomba ligando e desligando e a válvula e a carga variando. O hash acumula o estado de todos os 60 000 passos.

```text
semente 2026, execução 1: estado = 744989c7a55bff97; medição = 8e8e1e2384b760f6
semente 2026, execução 2: estado = 744989c7a55bff97; medição = 8e8e1e2384b760f6
semente 7:                estado = 744989c7a55bff97; medição = 4dc87c2927f1798d
ruído (semente 7): média = 1,462E-005 m; desvio = 0,001987 m (pedido 0,002 m)
```

- Mesma semente: hash do estado e hash da medição iguais.
- Outra semente: hash do estado igual; hash da medição diferente.
- Uma diferença de 1e-12 no comando da válvula muda o hash do estado (`DifferentInputs_ChangeStateHash`), então o hash não é insensível.

O hash de estado cobre o número do passo e os estados dos componentes. O ruído entra só na medição e nunca volta para a física.

Limite do que foi verificado: os hashes acima são de execuções na mesma máquina. Igualdade bit a bit entre Windows e Linux não foi testada nesta etapa.

## 7. Função de transferência → espaço de estados

Conversão na forma canônica controlável; degrau unitário em t = 0; erro máximo sobre todos os passos.

```text
1ª ordem, K = 1, tau = 1 s: erro máximo = 3,091E-011
1ª ordem, K = 2,5, tau = 0,5 s: erro máximo = 1,247E-009
1ª ordem, K = -3, tau = 12 s: erro máximo = 1,199E-014
2ª ordem subamortecida, K = 1, wn = 2 rad/s, zeta = 0,5: erro máximo = 1,050E-009
2ª ordem subamortecida, K = 3, wn = 5 rad/s, zeta = 0,1: erro máximo = 5,787E-007
2ª ordem subamortecida, K = 1, wn = 0,5 rad/s, zeta = 0,9: erro máximo = 4,779E-012
2ª ordem criticamente amortecida: erro máximo = 9,877E-010
2ª ordem superamortecida: erro máximo = 7,829E-009
```

O pior caso (ωn = 5 rad/s, ζ = 0,1, ganho 3) fica a menos de um fator 2 do limite: sistemas mais rápidos ou menos amortecidos que esse passam de 1e-6 com dt = 10 ms.

Além do critério: um avanço-atraso (2s + 1)/(5s + 1), que tem ganho direto, também confere com a solução analítica.

## Outros itens do escopo

| Item | Onde | Teste |
| --- | --- | --- |
| Nenhum controlador dentro da planta | O PID fica em `tests/.../ReferencePlants/PidController.cs` | `ArchitectureTests.Core_HasNoControllerTypes`; `WaterTankPlantTests.WithoutCommands_PlantOnlyDrains` |
| PID só como referência | Malha fechada de nível com o PID de teste | `ReferencePid_HoldsLevelAtSetpoint`: 1,00003 m em 600 s e 0,99998 m após distúrbio de carga (setpoint 1 m) |
| Relógio de passo fixo | `Simulation/SimulationClock.cs` | Um milhão de passos de 0,01 s dão exatamente 10 000 s |
| Gerador com semente | `Numerics/SeededRandom.cs` (xoshiro256\*\* + SplitMix64) | Vetores de referência publicados; média e desvio das saídas uniforme e gaussiana |
| Hash de estado | `Simulation/StateHasher.cs` (FNV-1a de 64 bits) | Vetores de referência publicados |
| Abstração de componente | `Components/Component.cs`, `Components/SignalSet.cs` | `ComponentTests` |

## Fontes das equações e parâmetros

| Item | Fonte |
| --- | --- |
| RK4 | Butcher, *Numerical Methods for Ordinary Differential Equations*, 3ª ed., 2016; Press et al., *Numerical Recipes*, 3ª ed., seção 17.1 |
| Atuador e resposta de 1ª ordem | Seborg, Edgar, Mellichamp, Doyle, *Process Dynamics and Control*, 4ª ed., 2016 |
| Balanço de massa do tanque | Seborg et al., idem (modelos de nível de líquido) |
| Torricelli e coeficiente de descarga (Cd = 0,61, orifício de borda viva) | White, *Fluid Mechanics*, 8ª ed., 2016 |
| Vazão proporcional à rotação da bomba | White, idem (leis de semelhança de bombas) |
| Gravidade padrão, 9,80665 m/s² | 3ª CGPM (1901); BIPM, brochura do SI, 9ª ed., 2019 |
| Forma canônica controlável e respostas ao degrau de 2ª ordem | Ogata, *Modern Control Engineering*, 5ª ed., 2010 |
| xoshiro256\*\* e SplitMix64 | Blackman e Vigna, *Scrambled linear pseudorandom number generators*, ACM TOMS, 2021 (código em domínio público) |
| Box–Muller | Box e Muller, Ann. Math. Statist., v. 29, n. 2, 1958 |
| FNV-1a | Fowler, Noll, Vo e Eastlake, *The FNV Non-Cryptographic Hash Algorithm* (IETF) |
| PID de teste e anti-windup | Åström e Hägglund, *Advanced PID Control*, ISA, 2006 |

As seções citadas nos comentários do código foram escritas de memória e devem ser conferidas nos livros antes de irem para um texto acadêmico.

## Decisões e simplificações desta etapa

- **Gerador pseudoaleatório próprio** (registrado como D22 no descritivo): a sequência do `System.Random` com semente não é garantida entre versões do .NET; a do xoshiro256\*\* é fixa e tem teste com vetores de referência.
- **Planta de referência no projeto de testes**: pela D04 o produto não tem classe por planta, então o "modelo codificado da Etapa 1", que a Etapa 3 vai comparar com o JSON, fica em `tests/`.
- **Vazões em m³/s**: o tanque trabalha com volume, como pede o critério 3. A conversão para kg/s do diagnóstico da caldeira fica para o cenário (Etapa 3).
- **Bomba sem curva**: a vazão depende só da rotação, não da contrapressão.
- **Passo máximo do atuador e da bomba**: um passo acima de 2,5·τ é recusado com erro claro, porque o RK4 ficaria instável (limite teórico: 2,785·τ).

## Pendências para concluir a etapa

- [x] Push da branch e pull request aberto.
- [x] Critérios marcados em `docs/plano.md`, "Estado atual" atualizado e D22 registrada em `docs/descritivo.md` (confirmado pelo Saulo em 14/11/2026).
- [ ] CI verde no pull request e integração à `main`.
