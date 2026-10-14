using System.Globalization;
using System.Numerics;

namespace GameBanking;

/// <summary>Money helpers. Amounts are integers in minor units; a currency's exponent is its decimal places. No floating point.</summary>
public static class Money
{
    /// <summary>Converts a decimal string to minor units: <c>ToMinor("100.50", 2) == 10050</c>.</summary>
    /// <exception cref="FormatException">Not a decimal amount, or more decimal places than the exponent allows.</exception>
    /// <exception cref="OverflowException">Too large for a long.</exception>
    public static long ToMinor(string amount, int exponent)
    {
        CheckExponent(exponent);
        var s = amount.Trim().Replace(",", "");
        var neg = s.StartsWith('-');
        if (neg) s = s[1..];
        var dot = s.IndexOf('.');
        var whole = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..];
        if (whole.Length == 0 || (dot >= 0 && frac.Length == 0) || !whole.All(char.IsAsciiDigit) || !frac.All(char.IsAsciiDigit))
            throw new FormatException($"not a decimal amount: \"{amount}\"");
        if (frac.Length > exponent) throw new FormatException($"{amount} has more than {exponent} decimal places");
        var n = BigInteger.Parse(whole + frac.PadRight(exponent, '0'), CultureInfo.InvariantCulture);
        if (neg) n = -n;
        if (n > long.MaxValue || n < long.MinValue) throw new OverflowException($"{amount} is too large");
        return (long)n;
    }

    /// <summary>Formats minor units as a plain decimal string: <c>FormatMinor(10050, 2) == "100.50"</c>.</summary>
    public static string FormatMinor(long minor, int exponent)
    {
        CheckExponent(exponent);
        var neg = minor < 0;
        var digits = BigInteger.Abs(minor).ToString(CultureInfo.InvariantCulture);
        if (exponent > 0)
        {
            digits = digits.PadLeft(exponent + 1, '0');
            digits = digits[..^exponent] + "." + digits[^exponent..];
        }
        return neg ? "-" + digits : digits;
    }

    private static void CheckExponent(int exponent)
    {
        if (exponent is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(exponent), "exponent must be 0 to 6");
    }
}
