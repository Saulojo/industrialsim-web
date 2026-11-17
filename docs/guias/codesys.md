# Mestre Modbus no CODESYS (Modbus TCP Master)

> **Situação:** guia escrito a partir da documentação do CODESYS V3.5; ainda não testado contra o simulador (a Etapa 6 foi validada com `pymodbus` e OpenPLC). Os nomes de menus podem variar um pouco entre versões.

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

## 1. Dispositivos

Com um projeto Standard e o **CODESYS Control Win** (soft-PLC gratuito para teste, 2 h por execução sem licença):

1. Na árvore, clique com o botão direito em **Device** → **Add Device…** → **Fieldbuses** → **Ethernet Adapter** → **Ethernet**. Em **General**, escolha a placa de rede do PC.
2. Com o botão direito em **Ethernet** → **Add Device…** → **Modbus** → **Modbus TCP Master**.
3. Com o botão direito em **Modbus_TCP_Master** → **Add Device…** → **Modbus TCP Slave**.
4. No **Modbus TCP Slave**, aba **General**:
   - **Server IP address**: o IP do servidor do simulador (`127.0.0.1` se for no mesmo PC);
   - **Port**: a porta da planta (ex.: 15000);
   - **Unit-ID**: 1.

## 2. Canais (Modbus Slave Channel)

Na aba **Modbus Slave Channel** do escravo, **Add Channel…**:

| Nome | Access Type | Cycle Time | Offset | Length | Para quê |
| --- | --- | --- | --- | --- | --- |
| Medicoes | Read Input Registers (FC 4) | 100 ms | 0 | 6 | LT-101, PT-101, FT-101, FT-102, posições de FV-101 e FV-102 |
| Comandos | Write Multiple Registers (FC 16) | 100 ms | 0 | 2 | FV-101 e FV-102 |
| Heartbeat | Write Single Register (FC 6) | 100 ms | 10 | 1 | Heartbeat |
| Bomba | Write Single Coil (FC 5) | 100 ms | 0 | 1 | P-101 |

O offset é o endereço em base 0 (não use 30001/40001 aqui).

## 3. Variáveis

Na aba **ModbusTCPSlave I/O Mapping**, dê nome aos canais (ex.: `LT101`, `FV101_CMD`, `HEARTBEAT`, `P101_RUN`). Os registradores aparecem como `WORD`; converta para `INT` antes de escalar, porque abaixo de 4 mA o valor é negativo.

## 4. Malha de nível (ST)

```iecst
PROGRAM PLC_PRG
VAR
    sp_m       : REAL := 0.6;      (* setpoint de nível, m *)
    kp         : REAL := 20.0;     (* % de válvula por % de nível *)
    ti_s       : REAL := 250.0;    (* tempo integral, s *)
    period_s   : REAL := 0.1;      (* período da tarefa, s *)
    level_pct, err, candidate, out_pct : REAL;
    integral   : REAL;
    started    : BOOL;
    beat       : WORD;
END_VAR

level_pct := INT_TO_REAL(WORD_TO_INT(LT101)) / 276.48;
IF NOT started THEN
    integral := INT_TO_REAL(WORD_TO_INT(FV101_POS)) / 276.48;  (* partida sem solavanco *)
    started := TRUE;
END_IF

err := sp_m / 1.2 * 100.0 - level_pct;            (* ação reversa *)
candidate := integral + kp * err * period_s / ti_s;
out_pct := kp * err + candidate;
IF out_pct >= 0.0 AND out_pct <= 100.0 THEN
    integral := candidate;                        (* anti-windup *)
END_IF
out_pct := LIMIT(0.0, out_pct, 100.0);

FV101_CMD := INT_TO_WORD(REAL_TO_INT(out_pct * 276.48));
beat := beat + 1;
HEARTBEAT := beat;
P101_RUN := TRUE;
```

Rode a tarefa a 100 ms (a mesma do `period_s`). Ao entrar em **Login** e **Start**, a primeira conexão fica pendente: aceite o pedido de pareamento (guia [modbus.md](modbus.md), seção 2).
