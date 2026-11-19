import { Socket } from 'node:net';

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
