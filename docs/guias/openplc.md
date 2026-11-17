# Mestre Modbus no OpenPLC (Editor v4 com o simulador)

O OpenPLC Editor v4 tem um simulador embutido que roda o programa no próprio PC e pode ser mestre Modbus TCP ("Remote Device"). Não é preciso instalar o OpenPLC Runtime. Guia testado com o OpenPLC Editor 4.3.5 no Windows 11.

Projeto pronto: [`tools/openplc/caldeira-lic101`](../../tools/openplc/caldeira-lic101), com a malha de nível LIC-101 em ST (LT-101 → PI → FV-101) e o heartbeat.

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

## 1. Abrir o projeto

1. Abra o **OpenPLC Editor**.
2. Na tela inicial, clique em **Open** (abrir projeto) e escolha a pasta `tools/openplc/caldeira-lic101`.
3. Confira o alvo: em **Device** (árvore do projeto), o campo **Device** deve estar em **OpenPLC Simulator**.

## 2. Remote Device (a planta)

1. Clique no **+** no alto da árvore do projeto → **Remote Device**.
2. **Protocol**: **Modbus**. Nome: `caldeira`. Clique em **Create**.
3. No editor que abre, transporte **TCP/IP**:
   - **IP Address**: o IP do servidor (`127.0.0.1` no mesmo PC);
   - **Port**: a porta da planta (ex.: 15000);
   - **Slave ID**: 1;
   - **Timeout (ms)**: 1000.
4. Em **IO Tag Mapping**, clique no **+** e crie os grupos:

   | Name | Function Code | Cycle Time (ms) | Offset | Length | Error Handling |
   | --- | --- | --- | --- | --- | --- |
   | `medicoes` | 4 – Read Input Registers | 100 | 0 | 6 | Keep last value |
   | `fv101` | 6 – Write Single Register | 100 | 0 | 1 | Keep last value |
   | `heartbeat` | 6 – Write Single Register | 100 | 10 | 1 | Keep last value |
   | `bomba` | 5 – Write Single Coil | 100 | 0 | 1 | Keep last value |

5. Abra cada grupo (seta no início da linha) e preencha o **Alias** de cada ponto:

   | Ponto | Alias |
   | --- | --- |
   | `medicoes_0` | `LT101` |
   | `medicoes_4` | `FV101_POS` |
   | `fv101_0` | `FV101_CMD` |
   | `heartbeat_0` | `HEARTBEAT` |
   | `bomba_0` | `P101_RUN` |

6. Salve (**Ctrl+S**).

O OpenPLC usa o endereço em base 0 (Offset), como o `pymodbus`; não use 30001/40001.

## 3. Programa

O programa `main` (ST) já está no projeto: lê `LT101`, calcula o PI em % da faixa e escreve `FV101_CMD`, `HEARTBEAT` e `P101_RUN`. A tarefa roda a cada 100 ms.

## 4. Rodar

1. Inicie o simulador (botão de executar/depurar do Editor).
2. A primeira conexão do simulador fica pendente na planta. Aceite o pedido de pareamento (guia [modbus.md](modbus.md), seção 2).
3. No modo de depuração do Editor, acompanhe `LT101`, `FV101_CMD` e o setpoint.
