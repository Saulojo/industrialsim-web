# Mestre Modbus no TIA Portal (`MB_CLIENT`)

> **Situação:** guia escrito a partir da ajuda do TIA Portal; ainda não testado com CLP real nem com PLCSIM Advanced. Esse teste é critério da Etapa 6 e ficou pendente para a Etapa 9. Confira os nomes dos parâmetros na ajuda da sua versão da instrução `MB_CLIENT`.

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

## Quem pode ser mestre

- S7-1200 ou S7-1500 real, ligado à rede do servidor.
- **PLCSIM Advanced** (S7-1500, licença paga): faz Modbus TCP com a rede pelo adaptador virtual.
- O PLCSIM comum **não** faz Modbus TCP com a rede. Sem PLCSIM Advanced, use CODESYS, OpenPLC ou Python.

## 1. Conexão (`TCON_IP_v4`)

Crie um DB global com uma variável do tipo `TCON_IP_v4` para cada `MB_CLIENT` (cada uma é uma conexão TCP):

| Campo | Valor |
| --- | --- |
| `InterfaceId` | HW identifier da interface PROFINET (ex.: 64) |
| `ID` | Único por conexão: 1, 2, 3… |
| `ConnectionType` | `16#0B` (TCP) |
| `ActiveEstablished` | `TRUE` |
| `RemoteAddress.ADDR` | IP do servidor, byte a byte |
| `RemotePort` | Porta da planta (ex.: 15000) |
| `LocalPort` | 0 |

## 2. Unit ID

Por padrão, o `MB_CLIENT` usa o Unit ID 255 (variável estática `MB_UNIT_ID` do DB de instância, nas versões que a têm). A planta responde ao Unit ID 1. Escolha uma das opções:

- pôr `MB_UNIT_ID := 1` no DB de instância de cada `MB_CLIENT`; ou
- trocar o Unit ID da planta para 255: `PUT /api/plants/{id}/modbus/unit-id` com `{"unitId":255}`.

Com o Unit ID errado, a planta responde com a exceção 0B, e o `STATUS` do bloco mostra o erro.

## 3. Chamadas do `MB_CLIENT`

O `MB_CLIENT` usa a notação clássica: o endereço escolhe a área e a função.

| Instância | `MB_MODE` | `MB_DATA_ADDR` | `MB_DATA_LEN` | Função | `MB_DATA_PTR` |
| --- | --- | --- | --- | --- | --- |
| Leitura | 0 | 30001 | 6 | 04 (input registers) | `Array[0..5] of Int` |
| Comandos | 1 | 40001 | 2 | 16 (holding registers) | `Array[0..1] of Int` |
| Heartbeat | 1 | 40011 | 1 | 16 | `Int` |

Use uma instância (e um `ID` de conexão) por linha; dispare `REQ` a cada 100 ms, ou quando `DONE` ou `ERROR` da chamada anterior subir.

## 4. Escala

As medições chegam como `INT` de 0 a 27648, igual a um cartão analógico. Use `NORM_X` e `SCALE_X`:

```iecst
"Nivel_m" := SCALE_X(MIN := 0.0, VALUE := NORM_X(MIN := 0, VALUE := "Leitura"[0], MAX := 27648), MAX := 1.2);
"Comandos"[0] := REAL_TO_INT(NORM_X(MIN := 0.0, VALUE := "SaidaPI_pct", MAX := 100.0) * 27648.0);
```

## 5. Malha de nível

Use o `PID_Compact` com PV = `"Nivel_m"`, setpoint 0,6 m e saída em % (`Output`) para `"Comandos"[0]`. Ponto de partida de sintonia: ganho 20, tempo integral 250 s (os mesmos do exemplo em Python, que trabalham em % da faixa). Incremente o heartbeat a cada ciclo da chamada de escrita.

Na primeira conexão, aceite o pedido de pareamento do IP do CLP (guia [modbus.md](modbus.md), seção 2).
