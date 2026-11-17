PROGRAM main
  (* Malha de nível LIC-101 da caldeira do IndustrialSim Web: LT-101 -> PI -> FV-101.
     Endereços do Remote Device "caldeira" (devices/remote/caldeira.json), escala 0-27648 = 4-20 mA.
     PI em % da faixa, com anti-windup; sintonia SIMC (Skogestad, 2003): Kp 20, Ti 250 s. *)
  VAR
    LT101 AT %IW0 : INT;                (* LT-101: nível do tubulão, 0-1,2 m *)
    FV101_POS AT %IW4 : INT;            (* FV-101: posição real, 0-100 % *)
    FV101_CMD AT %QW0 : INT := 22749;   (* FV-101: comando; começa em 82,28 %, o regime do cenário *)
    HEARTBEAT AT %QW1 : INT;            (* heartbeat para o watchdog da planta *)
    P101_RUN AT %QX0.0 : BOOL := TRUE;  (* P-101: liga a bomba *)
  END_VAR
  VAR
    SP_M : REAL := 0.6;          (* setpoint de nível, m *)
    KP : REAL := 20.0;           (* % de válvula por % de nível *)
    TI_S : REAL := 250.0;        (* tempo integral, s *)
    PERIOD_S : REAL := 0.1;      (* período da tarefa task0, s *)
    level_pct : REAL;
    err : REAL;
    candidate : REAL;
    out_pct : REAL;
    integral : REAL;
    started : BOOL := FALSE;
    beat : INT := 0;
  END_VAR

  (* Espera a primeira medição chegar (até o pareamento, as leituras falham e ficam em 0). *)
  IF NOT started THEN
    IF LT101 > 0 THEN
      integral := INT_TO_REAL(FV101_POS) / 276.48;  (* partida sem solavanco *)
      started := TRUE;
    END_IF;
  ELSE
    level_pct := INT_TO_REAL(LT101) / 276.48;
    err := SP_M / 1.2 * 100.0 - level_pct;          (* ação reversa *)
    candidate := integral + KP * err * PERIOD_S / TI_S;
    out_pct := KP * err + candidate;
    IF out_pct >= 0.0 AND out_pct <= 100.0 THEN
      integral := candidate;                        (* anti-windup *)
    END_IF;
    out_pct := LIMIT(0.0, out_pct, 100.0);
    FV101_CMD := REAL_TO_INT(out_pct * 276.48);
  END_IF;

  beat := beat + 1;
  IF beat >= 30000 THEN
    beat := 0;
  END_IF;
  HEARTBEAT := beat;
  P101_RUN := TRUE;
END_PROGRAM
