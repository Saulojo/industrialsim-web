namespace IndustrialSim.SimRun.Tests;

public class InputScriptTests
{
    [Fact]
    public void Parse_ReadsColumnsRowsAndEmptyCells()
    {
        var script = Parse("time,a,b\n0,1,2\n\n# comentário\n1.5,,3e-1\n");

        Assert.Equal(["a", "b"], script.InputNames);
        Assert.Equal(2, script.Rows.Count);
        Assert.Equal(0.0, script.Rows[0].TimeSeconds);
        Assert.Equal([1.0, 2.0], script.Rows[0].Values);
        Assert.Equal(1.5, script.Rows[1].TimeSeconds);
        Assert.Equal([null, 0.3], script.Rows[1].Values);
        Assert.Equal(1.5, script.LastTimeSeconds);
    }

    /// <summary>CSV como o Excel em português exporta: ponto e vírgula e vírgula decimal.</summary>
    [Fact]
    public void Parse_AcceptsSemicolonAndDecimalComma()
    {
        var script = Parse("time;a\n0;0,25\n2,5;1\n");

        Assert.Equal([0.25], script.Rows[0].Values);
        Assert.Equal(2.5, script.Rows[1].TimeSeconds);
    }

    [Theory]
    [InlineData("", "vazio")]
    [InlineData("tempo,a\n0,1\n", "linha 1: a primeira coluna deve se chamar 'time'")]
    [InlineData("time,a,a\n0,1,1\n", "linha 1: a coluna 'a' aparece duas vezes")]
    [InlineData("time,a\n0,1,2\n", "linha 2: 3 colunas; o cabeçalho tem 2")]
    [InlineData("time,a\n0,abc\n", "linha 2, coluna 'a': 'abc' não é um número")]
    [InlineData("time,a\n,1\n", "linha 2: a coluna 'time' não pode ficar vazia")]
    [InlineData("time,a\n5,1\n2,0\n", "linha 3: o tempo 2 é menor que o da linha anterior")]
    [InlineData("time,a\n-1,1\n", "linha 2: tempo negativo")]
    public void Parse_InvalidScript_ReportsLine(string text, string expected)
    {
        var exception = Assert.Throws<FormatException>(() => Parse(text));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static InputScript Parse(string text)
    {
        using var reader = new StringReader(text);
        return InputScript.Parse(reader);
    }
}
