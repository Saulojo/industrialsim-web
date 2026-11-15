# Cenário `caldeira` (v1)

Caldeira com tubulão da Fase 1: balanço de massa no tubulão e pressão com dinâmica de 1ª ordem em função do combustível e da saída de vapor. Serve para testar Modbus, IHM e implantação; o modelo realista (Åström & Bell, IAPWS-IF97, swell/shrink) é a Etapa 15 e mantém as mesmas entradas e saídas.

A planta não tem controle interno. O cenário começa em regime: bomba ligada, 2 kg/s de vapor a 10 bar absolutos e nível a meia altura do tubulão.

Os valores abaixo são ilustrativos, de uma caldeira pequena (7,2 t/h de vapor), a ajustar com o orientador.

## Componentes

| Tag | Tipo | Papel |
| --- | --- | --- |
| P-101 | `pump` | Bomba de alimentação: 4 kg/s nominais, partida em τ = 2 s. |
| FV-101 | `valve` | Válvula de água, igual-percentual (R = 50), τ = 2 s. |
| FV-102 | `valve` | Válvula de combustível, linear, 0,2217 kg/s toda aberta, τ = 2 s. |
| FV-103 | `valve` | Válvula de saída de vapor (carga), linear, τ = 2 s. |
| Q-101 | `burner` | Queimador e fornalha, τ = 10 s. |
| TB-101 | `drum` | Tubulão, lado da água: nível e vapor gerado. |
| TB-101-V | `steamPressure` | Tubulão, lado do vapor: pressão e vazão de vapor. |
| PSV-101 | `reliefValve` | Abre a 13 bar abs, fecha a 12,5 bar abs, 5 kg/s. |
| LT-101, PT-101, FT-101, FT-102 | `transmitter` | Nível (m), pressão (Pa abs), vazão de água e de vapor (kg/s). |
| LSLL-101, LSHH-101 | `switch` | Nível muito baixo (0,30 m) e muito alto (0,90 m), banda morta de 0,02 m. |
| PSH-101 | `switch` | Pressão alta (12 bar abs), banda morta de 0,2 bar. |

## Parâmetros e de onde vêm

| Parâmetro | Valor | Origem |
| --- | --- | --- |
| Poder calorífico inferior do combustível | 50,05 MJ/kg | Metano; Çengel & Boles, "Thermodynamics: An Engineering Approach", 8ª ed., tabela A-27. |
| Eficiência da caldeira | 0,85 | Valor adotado. |
| Entalpia de evaporação | 2,358 MJ/kg | h do vapor saturado a 1 MPa (2777,1 kJ/kg) menos h da água de alimentação a 100 °C (419,17 kJ/kg); Çengel & Boles, tabelas A-5 e A-4 (valores da IAPWS-IF97). |
| Densidade da água no tubulão | 887 kg/m³ | Líquido saturado a 1 MPa (v = 0,001127 m³/kg); mesma tabela A-5. |
| Seção e altura do tubulão | 4,8 m² e 1,2 m | Tubulão horizontal de 1,2 m de diâmetro por 4 m; seção tomada na altura do eixo. Valor adotado. |
| Condutância da saída de vapor | 4,45e-6 kg/(s·Pa) | Escolhida para dar 2 kg/s a 10 bar abs com FV-103 a 50%. |
| Capacitância de vapor | 1,335e-4 kg/Pa | Escolhida para a pressão ter τ = 60 s no ponto nominal (τ = C/(k·x)). Valor adotado. |
| Ruído e amortecimento dos transmissores | ver `scenario.json` | Valores adotados. |

Com esses números, cada kg de combustível gera 0,85 × 50,05 / 2,358 = 18,04 kg de vapor; 2 kg/s de vapor pedem 0,1109 kg/s de combustível, que é FV-102 a 50%.

## Ponto de operação inicial

| Grandeza | Valor |
| --- | --- |
| P-101 | Ligada |
| FV-101 | 82,28% (passa 50% da vazão da bomba: 2 kg/s) |
| FV-102 | 50% |
| FV-103 | 50% |
| Nível | 0,60 m |
| Pressão | 10 bar abs |

## Simplificações do v1

- O nível responde só ao balanço de massa: não há swell/shrink, e a pressão não afeta o nível.
- A vazão de água não depende da pressão do tubulão (sem curva de bomba).
- A vazão de vapor é linear na pressão manométrica e na abertura de FV-103.
- Capacitância e condutância são constantes.
