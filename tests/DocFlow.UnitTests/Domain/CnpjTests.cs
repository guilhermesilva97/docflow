using DocFlow.Domain;

namespace DocFlow.UnitTests.Domain;

public class CnpjTests
{
    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Accepts_valid_cnpj_with_or_without_punctuation(string value) =>
        Assert.True(Cnpj.IsValid(value));

    [Theory]
    [InlineData("11.222.333/0001-82")]
    [InlineData("11111111111111")]
    [InlineData("1122233300018")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_cnpj(string? value) =>
        Assert.False(Cnpj.IsValid(value));

    [Fact]
    public void Generates_check_digits_that_validate()
    {
        var cnpj = Cnpj.WithCheckDigits("112223330001");

        Assert.Equal("11222333000181", cnpj);
        Assert.Equal("11.222.333/0001-81", Cnpj.Format(cnpj));
    }
}
