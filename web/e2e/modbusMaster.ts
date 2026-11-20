import { Socket } from 'node:net';

/** Resposta de exceção Modbus. */
export class ModbusException extends Error {
  constructor(readonly code: number) {
    super(`Exceção Modbus ${code}`);
  }
}

/**
 * Mestre Modbus TCP mínimo para os testes de ponta a ponta (sem dependência nova): conecta, lê e escreve
 * holding registers e avisa quando o servidor fecha a conexão.
 */
export class ModbusMaster {
  private readonly socket = new Socket();
  private transaction = 0;
  private buffer = Buffer.alloc(0);
  private waiting: ((frame: Buffer) => void) | null = null;
  readonly closed: Promise<void>;

  private constructor() {
    this.closed = new Promise((resolve) => this.socket.once('close', () => resolve()));
    this.socket.on('data', (data: Buffer) => {
      this.buffer = Buffer.concat([this.buffer, data]);
      this.deliver();
    });
    this.socket.on('error', () => {
      // a queda aparece em `closed`
    });
  }

  static connect(port: number, host = '127.0.0.1'): Promise<ModbusMaster> {
    const master = new ModbusMaster();
    return new Promise((resolve, reject) => {
      master.socket.once('error', reject);
      master.socket.connect(port, host, () => resolve(master));
    });
  }

  /** Função 3: lê holding registers. */
  async readHoldingRegisters(address: number, count: number, unitId = 1): Promise<number[]> {
    const pdu = Buffer.from([0x03, address >> 8, address & 0xff, count >> 8, count & 0xff]);
    const reply = await this.request(pdu, unitId);
    if (reply[0] !== 0x03) throw new Error(`Exceção Modbus ${reply[1]}`);
    const values: number[] = [];
    for (let i = 0; i < count; i++) values.push(reply.readUInt16BE(2 + 2 * i));
    return values;
  }

  /** Função 4: lê input registers (palavras sem sinal). Lança `ModbusException` com o código da exceção. */
  async readInputRegisters(address: number, count: number, unitId = 1): Promise<number[]> {
    const pdu = Buffer.from([0x04, address >> 8, address & 0xff, count >> 8, count & 0xff]);
    const reply = await this.request(pdu, unitId);
    if (reply[0] !== 0x04) throw new ModbusException(reply[1] ?? 0);
    const values: number[] = [];
    for (let i = 0; i < count; i++) values.push(reply.readUInt16BE(2 + 2 * i));
    return values;
  }

  /** Lê um `float32` em dois input registers, com a palavra mais significativa primeiro. */
  async readFloat(address: number, unitId = 1): Promise<number> {
    const [high = 0, low = 0] = await this.readInputRegisters(address, 2, unitId);
    const buffer = Buffer.alloc(4);
    buffer.writeUInt16BE(high, 0);
    buffer.writeUInt16BE(low, 2);
    return buffer.readFloatBE(0);
  }

  /** Função 6: escreve um holding register. */
  async writeHoldingRegister(address: number, value: number, unitId = 1): Promise<void> {
    const pdu = Buffer.from([
      0x06,
      address >> 8,
      address & 0xff,
      (value >> 8) & 0xff,
      value & 0xff,
    ]);
    const reply = await this.request(pdu, unitId);
    if (reply[0] !== 0x06) throw new Error(`Exceção Modbus ${reply[1]}`);
  }

  close(): void {
    this.socket.destroy();
  }

  private request(pdu: Buffer, unitId: number, timeoutMs = 5000): Promise<Buffer> {
    const id = ++this.transaction & 0xffff;
    const header = Buffer.alloc(7);
    header.writeUInt16BE(id, 0);
    header.writeUInt16BE(0, 2);
    header.writeUInt16BE(pdu.length + 1, 4);
    header.writeUInt8(unitId, 6);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.waiting = null;
        reject(new Error('Sem resposta Modbus.'));
      }, timeoutMs);
      this.waiting = (frame) => {
        clearTimeout(timer);
        resolve(frame);
      };
      this.socket.write(Buffer.concat([header, pdu]));
    });
  }

  private deliver(): void {
    while (this.buffer.length >= 7) {
      const length = this.buffer.readUInt16BE(4);
      if (this.buffer.length < 6 + length) return;
      const pdu = this.buffer.subarray(7, 6 + length);
      this.buffer = this.buffer.subarray(6 + length);
      const waiting = this.waiting;
      this.waiting = null;
      waiting?.(Buffer.from(pdu));
    }
  }
}
