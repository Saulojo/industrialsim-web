# Mestre Modbus em Python (`pymodbus`)

Exemplo pronto: [`tools/modbus-master/level_loop.py`](../../tools/modbus-master/level_loop.py) fecha a malha de nível LIC-101 da caldeira (LT-101 → PI → FV-101) e mantém o heartbeat. Testado com Python 3.15 e `pymodbus` 3.16.2 (licença BSD-3) no Windows 11.

Antes, leia [Conectar um mestre Modbus à planta](modbus.md): criar a planta, porta e pareamento.

## 1. Instalar

```bash
pip install pymodbus
```

## 2. Rodar

Com a planta criada (porta em `modbusPort`):

```bash
python tools/modbus-master/level_loop.py --port 15000
```

Na primeira vez, o script mostra `Aguardando o pareamento...`: aceite o pedido (guia [modbus.md](modbus.md), seção 2). Depois disso ele imprime, a cada segundo, o setpoint, o nível e o comando e a posição de FV-101.

Opções úteis:

| Opção | Padrão | Significado |
| --- | --- | --- |
| `--host` | `127.0.0.1` | IP do servidor |
| `--port` | — | Porta da planta |
| `--unit` | 1 | Unit ID |
| `--setpoint` | 0,6 | Setpoint de nível (m) |
| `--kp`, `--ti` | 20, 250 s | Sintonia do PI (% de válvula por % de nível; tempo integral) |
| `--step-at`, `--step-to` | — | Degrau de setpoint: instante (s) e novo valor (m) |
| `--duration` | 0 | Duração do ensaio (0 = até Ctrl+C) |
| `--csv` | — | Arquivo para registrar o ensaio |

Exemplo de ensaio com degrau de 0,6 para 0,55 m aos 20 s, gravado em CSV:

```bash
python tools/modbus-master/level_loop.py --port 15000 --step-at 20 --step-to 0.55 --duration 300 --csv ensaio.csv
```

## 3. Como o script fala com a planta

```python
from pymodbus.client import ModbusTcpClient

client = ModbusTcpClient("127.0.0.1", port=15000, timeout=1, retries=0)
client.connect()
client.write_coil(0, True, device_id=1)                                # P-101: liga a bomba
nivel = client.read_input_registers(0, count=1, device_id=1).registers[0]  # LT-101 em contagens
client.write_register(0, 13824, device_id=1)                           # FV-101 em 50 %
client.write_register(10, batida, device_id=1)                         # heartbeat
```

- No `pymodbus` 3.10 ou mais novo o Unit ID se chama `device_id` (antes era `slave`).
- O valor lido vem sem sinal (0–65535): abaixo de 4 mA ele passa de 32767. Converta para `INT` como em `to_signed()` no script.
- O PI trabalha em % da faixa: nível em % de 0–1,2 m e saída em % de abertura, convertida para 0–27648.
