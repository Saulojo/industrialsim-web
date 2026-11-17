(* Malha de nível LIC-101 da caldeira do IndustrialSim Web no CODESYS (V3.5 SP22): LT-101 -> PI -> FV-101.
   O editor do PLC_PRG tem duas áreas: a de cima (declarações) e a de baixo (código). Cole a PARTE 1 na de cima,
   no lugar do VAR END_VAR vazio, e a PARTE 2 na de baixo. Veja docs/guias/codesys.md.
   As variáveis Medicoes, FV101_CMD, HEARTBEAT e P101_RUN são criadas na aba "ModbusTCPServer Mapeamento de E/S":
   Medicoes na linha do canal (ARRAY [0..5] OF WORD); FV101_CMD, HEARTBEAT e P101_RUN na sub-linha [0] do canal
   (WORD, WORD e BOOL).
   Escala 0-27648 = 4-20 mA; PI em % da faixa com anti-windup; sintonia SIMC (Skogestad, 2003): Kp 20, Ti 250 s. *)

(* ======== PARTE 1: área de cima (declarações) ======== *)
PROGRAM PLC_PRG
VAR
    sp_m       : REAL := 0.6;      (* setpoint de nível, m *)
    kp         : REAL := 20.0;     (* % de válvula por % de nível *)
    ti_s       : REAL := 250.0;    (* tempo integral, s *)
    period_s   : REAL := 0.1;      (* período da MainTask, s (T#100MS) *)
    level_m    : REAL;
    level_pct  : REAL;
    err        : REAL;
    candidate  : REAL;
    out_pct    : REAL;
    integral   : REAL;
    started    : BOOL;
    beat       : WORD;
END_VAR

(* ======== PARTE 2: área de baixo (código) ======== *)

(* Medicoes[0] = LT-101, Medicoes[4] = posição de FV-101 (input registers 0 a 5). *)
level_pct := INT_TO_REAL(WORD_TO_INT(Medicoes[0])) / 276.48;
level_m := level_pct * 1.2 / 100.0;

(* Até o pareamento, as leituras não chegam e ficam em 0: espera a primeira medição. *)
IF NOT started THEN
    FV101_CMD := 22749;                                                    (* 82,28 %: regime do cenário *)
    IF Medicoes[0] > 0 THEN
        integral := INT_TO_REAL(WORD_TO_INT(Medicoes[4])) / 276.48;        (* partida sem solavanco *)
        started := TRUE;
    END_IF
ELSE
    err := sp_m / 1.2 * 100.0 - level_pct;                                 (* ação reversa *)
    candidate := integral + kp * err * period_s / ti_s;
    out_pct := kp * err + candidate;
    IF out_pct >= 0.0 AND out_pct <= 100.0 THEN
        integral := candidate;                                             (* anti-windup *)
    END_IF
    out_pct := LIMIT(0.0, out_pct, 100.0);
    FV101_CMD := INT_TO_WORD(REAL_TO_INT(out_pct * 276.48));
END_IF

beat := beat + 1;
HEARTBEAT := beat;
P101_RUN := TRUE;
