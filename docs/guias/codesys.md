# Mestre Modbus no CODESYS (Modbus TCP Client)

Guia testado na Etapa 6 com o **CODESYS V3.5 SP22 Patch 4** (interface em português) e o soft-PLC **CODESYS Control Win V3 x64**, no Windows 11, fechando a malha de nível LIC-101 da caldeira. Evidências em [`docs/validacao/etapa-06.md`](../validacao/etapa-06.md).

Programa pronto: [`tools/codesys/PLC_PRG.st`](../../tools/codesys/PLC_PRG.st).

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

> A partir da SP22, a CODESYS segue a nomenclatura nova da Modbus Organization: o mestre se chama **Modbus TCP Client** e o escravo (a planta), **Modbus TCP Server**. Em versões anteriores, os mesmos dispositivos se chamam *Modbus TCP Master* e *Modbus TCP Slave*.

## 1. Instalar e ligar o soft-PLC

1. Crie uma conta na CODESYS Store e instale o **CODESYS Development System V3.5** (o suporte a Modbus vem na instalação padrão). O **CODESYS Control Win** vem junto: sem licença, ele roda por 2 h e depois é preciso reiniciá-lo.
2. Ligue o soft-PLC: na bandeja do Windows, botão direito no ícone **CODESYS Control Win V3 - x64 SysTray** → **Start PLC**.

## 2. Projeto

1. **Arquivo → Novo projeto → Projeto padrão**. Dispositivo: **CODESYS Control Win V3 x64**; PLC_PRG em **Texto estruturado (ST)**.
2. Duplo clique em **Device** → **Configurações de comunicação** → **Varrer a rede** → selecione o PC. Na primeira vez, o soft-PLC pede para criar um usuário.

## 3. Dispositivos Modbus

1. Botão direito em **Device** → **Adicionar dispositivo…** → **Barramentos de campo → Adaptador Ethernet → Ethernet**. Duplo clique em **Ethernet** → **Geral** → escolha a placa de rede do PC.
2. Botão direito em **Ethernet** → **Adicionar dispositivo…** → **Modbus → Cliente TCP Modbus → Modbus TCP Client**.
3. No **Modbus_TCP_Client**, aba **Geral**: marque **Reconexão automática**. Enquanto o pareamento está pendente, as requisições dão time-out; sem essa opção o cliente desiste da conexão.
4. Botão direito em **Modbus_TCP_Client** → **Adicionar dispositivo…** → **Modbus → Servidor TCP Modbus → Modbus TCP Server**.
5. No **Modbus_TCP_Server**, aba **Geral**:
   - **Endereço IP do servidor**: o IP do servidor do simulador (`127.0.0.1` no mesmo PC);
   - **Porta**: a porta da planta (ex.: 15000);
   - o **Unit-ID** (1) fica na aba **ModbusTCPServer Parâmetros**.

## 4. Canais

Aba **Canal do servidor Modbus** → **Adicionar canal…**, quatro vezes:

| Nome | Tipo de acesso | Gatilho | Deslocamento | Comprimento | Para quê |
| --- | --- | --- | --- | --- | --- |
| `Medicoes` | Read Input Registers (Código da Função 04) | Cíclico, t#100ms | 0 | 6 | LT-101, PT-101, FT-101, FT-102, posições de FV-101 e FV-102 |
| `FV101` | Write Single Register (Código da Função 06) | Cíclico, t#100ms | 0 | 1 | Comando de FV-101 |
| `Heartbeat` | Write Single Register (Código da Função 06) | Cíclico, t#100ms | 10 (aparece como `16#000A`) | 1 | Heartbeat |
| `Bomba` | Write Single Coil (Código da Função 05) | Cíclico, t#100ms | 0 | 1 | P-101 |

O deslocamento é o endereço em base 0 (não use 30001/40001 aqui).

## 5. Variáveis

Aba **ModbusTCPServer Mapeamento de E/S**: duplo clique na coluna **Variável**, digite o nome e Enter.

| Linha (coluna Canal) | Endereço | Tipo | Variável |
| --- | --- | --- | --- |
| `Medicoes` (a linha do canal) | `%IW0` | `ARRAY [0..5] OF WORD` | `Medicoes` |
| `FV101[0]` (abra o **+** do canal) | `%QW0` | `WORD` | `FV101_CMD` |
| `Heartbeat[0]` | `%QW1` | `WORD` | `HEARTBEAT` |
| `Bit0` (abra o **+** de `Bomba` e depois o de `Bomba[0]`) | `%QX4.0` | `BOOL` | `P101_RUN` |

- O tipo não se edita: ele vem do canal. Nas saídas de um registrador só, use a sub-linha `[0]`, senão a variável vira um array de 1 elemento.
- A coil fica no bit 0 de um byte. Se o nome ficar na linha `Bomba[0]` (`BYTE`), escreva `P101_RUN := 1;` no programa em vez de `TRUE`.
- Os registradores chegam como `WORD`; converta para `INT` antes de escalar, porque abaixo de 4 mA o valor é negativo.
- Em **Sempre atualizar variáveis**, escolha **Habilitado 2 (sempre na tarefa de ciclo de barramento)**.

## 6. Programa e tarefa

O editor do **PLC_PRG** tem duas áreas, separadas por uma barra: a de cima é só para declarações (`PROGRAM … VAR … END_VAR`) e a de baixo para o código. Do arquivo [`PLC_PRG.st`](../../tools/codesys/PLC_PRG.st), cole a **PARTE 1** em cima e a **PARTE 2** embaixo. Código colado na área de cima dá o erro *C0006: Era esperado ',', AT ou ':' em vez de ':='*.

O programa espera a primeira medição chegar (até o pareamento as leituras ficam em 0), parte sem solavanco a partir da posição real de FV-101 e roda um PI em % da faixa (Kp 20, Ti 250 s), com anti-windup. Ele também incrementa o heartbeat e mantém a bomba ligada.

Em **Configuração de tarefas → MainTask**, ponha o intervalo em **100 ms** (o `period_s` do programa).

## 7. Rodar

1. **Compilação → Compilar** (F11): 0 erros.
2. **Online → Login** (Alt+F8) → aceite criar e baixar a aplicação → **Depuração → Start** (F5).
3. A primeira conexão fica pendente na planta. Aceite o pedido de pareamento (guia [modbus.md](modbus.md), seção 2). Com a reconexão automática, o CODESYS reconecta sozinho e passa a ser atendido.
4. No PLC_PRG online, `started` vira `TRUE`, `Medicoes[0]` mostra o nível (13824 = 0,6 m) e `out_pct` e `FV101_CMD` passam a seguir o PI.
5. Para mudar o setpoint: duplo clique em **Valor preparado** de `sp_m`, digite o valor (ex.: `0.55`) e **Depuração → Escrever valores** (Ctrl+F7).
