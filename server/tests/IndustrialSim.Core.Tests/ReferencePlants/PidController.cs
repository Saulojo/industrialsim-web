namespace IndustrialSim.Core.Tests.ReferencePlants;

/// <summary>
/// PID de referência, só para testes: na planta não existe controlador (o controle vem do mestre Modbus).
/// Forma posicional, u = Kp·e + Ki·∫e dt + Kd·de/dt, com saída limitada e anti-windup por integração condicional
/// (a integral não acumula enquanto a saída está saturada).
/// Fonte: Åström, K. J.; Hägglund, T. "Advanced PID Control", ISA, 2006, seções 3.2 e 3.5.
/// </summary>
internal sealed class PidController(double kp, double ki, double kd, double outputMin, double outputMax)
{
    private double _integral;
    private double _previousError;
    private bool _hasPreviousError;

    public double Update(double setpoint, double measurement, double stepSeconds)
    {
        double error = setpoint - measurement;
        double derivative = _hasPreviousError ? (error - _previousError) / stepSeconds : 0.0;
        double candidateIntegral = _integral + (ki * error * stepSeconds);

        double unsaturated = (kp * error) + candidateIntegral + (kd * derivative);
        double output = Math.Clamp(unsaturated, outputMin, outputMax);
        if (output == unsaturated)
        {
            _integral = candidateIntegral;
        }

        _previousError = error;
        _hasPreviousError = true;
        return output;
    }
}
