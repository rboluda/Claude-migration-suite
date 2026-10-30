using Migrador.Core;

namespace Migrador.Tests;

public class FidelityTests
{
    [Fact]
    public void Summary_reports_worst_level_and_counts_never_an_average()
    {
        var s = FidelitySummary.Of([Fidelity.Original, Fidelity.Original, Fidelity.Original, Fidelity.Lost, Fidelity.ExtractedText]);

        Assert.Equal(Fidelity.Lost, s.Worst);
        Assert.Equal(3, s[Fidelity.Original]);
        Assert.Equal(1, s[Fidelity.ExtractedText]);
        Assert.Equal(1, s[Fidelity.Lost]);
        Assert.Equal(0, s[Fidelity.Equivalent]);
        Assert.Equal(5, s.Total);
    }

    [Fact]
    public void Empty_summary_is_original()
    {
        Assert.Equal(Fidelity.Original, FidelitySummary.Of([]).Worst);
        Assert.Equal(0, FidelitySummary.Of([]).Total);
    }

    [Theory]
    [InlineData("notas.txt", Fidelity.Equivalent)]
    [InlineData("README.MD", Fidelity.Equivalent)]
    [InlineData("informe.pdf", Fidelity.ExtractedText)]
    [InlineData("hoja.xlsx", Fidelity.ExtractedText)]
    [InlineData(null, Fidelity.ExtractedText)]
    public void Extracted_text_is_equivalent_only_for_plain_text_files(string? name, Fidelity expected) =>
        Assert.Equal(expected, FidelityRules.ForExtractedText(name));
}
