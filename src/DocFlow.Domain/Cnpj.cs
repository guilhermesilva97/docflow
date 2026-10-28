namespace DocFlow.Domain;

public static class Cnpj
{
    private static readonly int[] FirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static string Digits(string? value) =>
        new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static bool IsValid(string? value)
    {
        var digits = Digits(value);
        if (digits.Length != 14 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        return digits[12] - '0' == CheckDigit(digits[..12], FirstWeights)
            && digits[13] - '0' == CheckDigit(digits[..13], SecondWeights);
    }

    public static string WithCheckDigits(string twelveDigits)
    {
        var first = CheckDigit(twelveDigits, FirstWeights);
        var second = CheckDigit(twelveDigits + first, SecondWeights);
        return $"{twelveDigits}{first}{second}";
    }

    public static string Format(string digits) =>
        digits.Length == 14
            ? $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}"
            : digits;

    private static int CheckDigit(string digits, int[] weights)
    {
        var sum = digits.Select((c, i) => (c - '0') * weights[i]).Sum();
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
