"""Malha de nível LIC-101 da caldeira com pymodbus: um mestre Modbus TCP de exemplo.

Lê LT-101 (input register 0), calcula um PI e escreve o comando de FV-101 (holding register 0) e o
heartbeat (holding register 10), como faria um CLP. Escala 0–27648 (padrão da planta), faixa de LT-101
de 0 a 1,2 m. Veja docs/guias/python.md.

Uso:
    python level_loop.py --port 15000
    python level_loop.py --port 15000 --setpoint 0.6 --step-at 60 --step-to 0.7 --duration 300 --csv ensaio.csv

Requer pymodbus 3.10 ou mais novo (testado com 3.16.2): pip install pymodbus
"""

from __future__ import annotations

import argparse
import csv
import sys
import time

from pymodbus.client import ModbusTcpClient
from pymodbus.exceptions import ModbusException

FULL_SCALE = 27648  # 20 mA no formato 0–27648 (cartão Siemens)
LEVEL_URV_M = 1.2  # faixa de LT-101: 0 a 1,2 m

# Mapa Modbus da caldeira (endereços em base 0; docs/descritivo.md)
COIL_PUMP_RUN = 0
IR_LEVEL = 0  # LT-101
IR_WATER_VALVE_POSITION = 4  # FV-101: posição real
HR_WATER_VALVE = 0  # FV-101: comando
HR_HEARTBEAT = 10


def to_signed(value: int) -> int:
    """Registrador lido como INT (complemento de dois): abaixo de 4 mA o valor é negativo."""
    return value - 0x10000 if value >= 0x8000 else value


def counts_to_percent(counts: int) -> float:
    return 100.0 * counts / FULL_SCALE


def percent_to_counts(percent: float) -> int:
    return round(FULL_SCALE * min(max(percent, 0.0), 100.0) / 100.0)


class PI:
    """PI de posição com anti-windup por limitação da integral, saída de 0 a 100 %."""

    def __init__(self, kp: float, ti_s: float, period_s: float, initial_output: float) -> None:
        self.kp = kp
        self.ti_s = ti_s
        self.period_s = period_s
        # Partida sem solavanco: a integral começa no valor que a válvula já tem.
        self.integral = initial_output

    def update(self, setpoint: float, measurement: float) -> float:
        error = setpoint - measurement  # ação reversa: nível abaixo do setpoint abre a válvula de água
        candidate = self.integral + self.kp * error * self.period_s / self.ti_s
        output = self.kp * error + candidate
        if 0.0 <= output <= 100.0:
            self.integral = candidate
        return min(max(output, 0.0), 100.0)


def check(response):
    if response.isError():
        raise ModbusException(str(response))
    return response


def main() -> int:
    parser = argparse.ArgumentParser(description="Malha de nível LIC-101 da caldeira (mestre Modbus TCP).")
    parser.add_argument("--host", default="127.0.0.1", help="IP do servidor do simulador")
    parser.add_argument("--port", type=int, required=True, help="porta Modbus da planta (15000–15999)")
    parser.add_argument("--unit", type=int, default=1, help="Unit ID da planta (padrão 1)")
    parser.add_argument("--setpoint", type=float, default=0.6, help="setpoint de nível, em m")
    parser.add_argument("--kp", type=float, default=20.0, help="ganho proporcional, em %% de válvula por %% de nível")
    parser.add_argument("--ti", type=float, default=250.0, help="tempo integral, em s")
    parser.add_argument("--period", type=float, default=0.1, help="período da malha, em s")
    parser.add_argument("--step-at", type=float, default=None, help="instante do degrau de setpoint, em s")
    parser.add_argument("--step-to", type=float, default=None, help="novo setpoint depois do degrau, em m")
    parser.add_argument("--duration", type=float, default=0.0, help="duração do ensaio, em s (0 = até Ctrl+C)")
    parser.add_argument("--csv", default=None, help="arquivo CSV para registrar o ensaio")
    args = parser.parse_args()

    client = ModbusTcpClient(args.host, port=args.port, timeout=1, retries=0)
    print(f"Conectando a {args.host}:{args.port} (Unit ID {args.unit})...")

    # Até o pareamento ser aceito, o servidor segura a conexão sem responder.
    while True:
        try:
            if client.connect():
                check(client.write_coil(COIL_PUMP_RUN, True, device_id=args.unit))
                break
        except ModbusException:
            pass
        print("Aguardando o pareamento (aceite o pedido na API ou na IHM)...")
        client.close()
        time.sleep(2.0)

    initial = to_signed(check(client.read_holding_registers(HR_WATER_VALVE, count=1, device_id=args.unit)).registers[0])
    controller = PI(args.kp, args.ti, args.period, counts_to_percent(initial))
    print(f"Pareado. FV-101 começa em {counts_to_percent(initial):.1f} %; PI com Kp={args.kp}, Ti={args.ti} s.")

    writer = None
    log_file = None
    if args.csv:
        log_file = open(args.csv, "w", newline="", encoding="utf-8")
        writer = csv.writer(log_file)
        writer.writerow(["t_s", "setpoint_m", "level_m", "fv101_command_pct", "fv101_position_pct"])

    start = time.monotonic()
    next_cycle = start
    heartbeat = 0
    last_print = -1.0
    try:
        while True:
            now = time.monotonic() - start
            if args.duration > 0 and now >= args.duration:
                break

            setpoint = args.setpoint
            if args.step_at is not None and args.step_to is not None and now >= args.step_at:
                setpoint = args.step_to

            registers = check(client.read_input_registers(IR_LEVEL, count=5, device_id=args.unit)).registers
            level_pct = counts_to_percent(to_signed(registers[IR_LEVEL]))
            position_pct = counts_to_percent(to_signed(registers[IR_WATER_VALVE_POSITION]))
            output = controller.update(100.0 * setpoint / LEVEL_URV_M, level_pct)

            check(client.write_register(HR_WATER_VALVE, percent_to_counts(output), device_id=args.unit))
            heartbeat = (heartbeat + 1) & 0xFFFF
            check(client.write_register(HR_HEARTBEAT, heartbeat, device_id=args.unit))

            level_m = LEVEL_URV_M * level_pct / 100.0
            if writer:
                writer.writerow([f"{now:.2f}", f"{setpoint:.3f}", f"{level_m:.4f}", f"{output:.2f}", f"{position_pct:.2f}"])
            if now - last_print >= 1.0:
                print(f"t={now:6.1f} s  SP={setpoint:.3f} m  LT-101={level_m:.4f} m  FV-101: comando {output:5.1f} %, posição {position_pct:5.1f} %")
                last_print = now

            next_cycle += args.period
            time.sleep(max(0.0, next_cycle - time.monotonic()))
    except KeyboardInterrupt:
        pass
    except ModbusException as error:
        print(f"Erro Modbus: {error}", file=sys.stderr)
        return 1
    finally:
        client.close()
        if log_file:
            log_file.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
