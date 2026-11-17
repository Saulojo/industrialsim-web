# Mestre Modbus no OpenPLC (Editor v4 e Runtime v4)

> **Situação:** projeto montado no OpenPLC Editor 4.3.5 e compilado; **não testado contra a planta**, porque só o OpenPLC Runtime v4 (Linux) faz o papel de mestre Modbus TCP. Veja "Limitação do Simulator" abaixo.

Projeto pronto: [`tools/openplc/caldeira-lic101`](../../tools/openplc/caldeira-lic101), com o Remote Device `caldeira` e a malha de nível LIC-101 em ST (LT-101 → PI → FV-101), o heartbeat e a bomba.

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

## Limitação do Simulator

O **OpenPLC Simulator** embutido no Editor roda o programa, mas **não abre conexões TCP**: ele emula um ATmega2560 (Arduino Mega) só com timers e serial (`avr8js`), e a configuração de mestre do Remote Device é gerada para o Runtime v4. Na Etapa 6, com o Simulator rodando, nenhuma conexão chegou à porta da planta. Para usar o OpenPLC como mestre, rode o programa no **OpenPLC Runtime v4** (Linux; no Windows, por WSL ou Docker) e escolha esse alvo em **Device**.

## 1. Remote Device (a planta)

1. Abra o projeto no **OpenPLC Editor** (**Open** → pasta `tools/openplc/caldeira-lic101`).
2. Clique no **+** no alto da árvore do projeto → **Remote Device** → **Protocol: Modbus** → nome `caldeira` → **Create**. (No projeto pronto, ele já existe.)
3. Transporte **TCP/IP**: **IP Address** do servidor, **Port** da planta (ex.: 15000), **Slave ID** 1, **Timeout** 1000 ms.
4. Em **IO Tag Mapping**, crie os grupos (todos com **Cycle Time** 100 ms e **Error Handling: Keep last value**):

   | Name | Function Code | Offset | Length | Endereço IEC gerado |
   | --- | --- | --- | --- | --- |
   | `medicoes` | 4 – Read Input Registers | 0 | 6 | `%IW0`–`%IW5` |
   | `fv101` | 6 – Write Single Register | 0 | 1 | `%QW0` |
   | `heartbeat` | 6 – Write Single Register | 10 | 1 | `%QW1` |
   | `bomba` | 5 – Write Single Coil | 0 | 1 | `%QX0.0` |

O OpenPLC usa o endereço em base 0 (Offset), como o `pymodbus`.

## 2. Programa

O Editor não cria variáveis a partir dos aliases do Remote Device: o programa `main` (ST) declara as variáveis nos endereços que o Editor atribuiu (`LT101 AT %IW0 : INT`, `FV101_POS AT %IW4`, `FV101_CMD AT %QW0`, `HEARTBEAT AT %QW1`, `P101_RUN AT %QX0.0 : BOOL`). Se você criar os grupos em outra ordem, confira os endereços na coluna **Address** e ajuste as declarações.

O PI é o mesmo do exemplo em Python e do CODESYS (Kp 20, Ti 250 s), com a tarefa a 100 ms.

## 3. Rodar (Runtime v4)

1. Em **Device**, escolha **OpenPLC Runtime v4** e o endereço do runtime.
2. Envie o programa e inicie o runtime.
3. A primeira conexão do runtime fica pendente na planta. Aceite o pedido de pareamento (guia [modbus.md](modbus.md), seção 2).
