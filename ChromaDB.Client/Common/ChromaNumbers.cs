using System.Globalization;
using System.Numerics;
using System.Text;

namespace ChromaDB.Client.Common;

// The text of the doubles and floats the client sends: the fewest digits that read back as the same number, as the
// Python client and System.Text.Json on .NET 8 write them, with ".0" on a whole number, so that Chroma keeps it a
// float (100.0, not 100). The same text on every build and runtime: System.Text.Json writes 17 digits for a double and
// 9 for a float on .NET Framework and in the netstandard2.0 build, and "0" for -0.0 on .NET Framework; Chroma reads
// "100" as an integer, and the 0.x servers compute the distances on the query as they read it.
internal static class ChromaNumbers
{
	// .NET Core 3.0 and later write the shortest digits that read back as the same number, the nearest among them ("R");
	// .NET Framework and Mono do not, and there the digits come from the exact value.
#if NET
	private const bool RuntimeShortest = true;
#else
	private static readonly bool RuntimeShortest = Environment.Version.Major is 3 or >= 5;
#endif

	public static string Format(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			throw new ArgumentException($"{value.ToString(CultureInfo.InvariantCulture)} is not a number JSON can carry: Chroma takes finite numbers only.", nameof(value));
		}
		var bits = BitConverter.DoubleToInt64Bits(value);
		if (value == 0)
		{
			return bits < 0 ? "-0.0" : "0.0";
		}
		string digits;
		int exponent;
		// "R" of .NET 10 does not read back as the number for 2^-25 and 2^-958: checked, and the exact digits then.
		var text = RuntimeShortest ? Math.Abs(value).ToString("R", CultureInfo.InvariantCulture) : null;
		if (text is not null && double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == Math.Abs(value))
		{
			FromText(text, out digits, out exponent);
		}
		else
		{
			ExactDigits(bits & long.MaxValue, 52, 1075, out digits, out exponent);
		}
		return Write(bits < 0, digits, exponent);
	}

	public static string Format(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value))
		{
			throw new ArgumentException($"{value.ToString(CultureInfo.InvariantCulture)} is not a number JSON can carry: Chroma takes finite numbers only.", nameof(value));
		}
		var bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
		if (value == 0)
		{
			return bits < 0 ? "-0.0" : "0.0";
		}
		string digits;
		int exponent;
		var text = RuntimeShortest ? Math.Abs(value).ToString("R", CultureInfo.InvariantCulture) : null;
		if (text is not null && float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == Math.Abs(value))
		{
			FromText(text, out digits, out exponent);
		}
		else
		{
			ExactDigits(bits & int.MaxValue, 23, 150, out digits, out exponent);
		}
		return Write(bits < 0, digits, exponent);
	}

	// "1.2345E-07" or "123.45" into the digits "12345" and the exponent of the first digit, -7 or 2.
	internal static void FromText(string text, out string digits, out int exponent)
	{
		var e = text.IndexOfAny(['E', 'e']);
		var mantissa = e < 0 ? text : text.Substring(0, e);
		var power = e < 0 ? 0 : int.Parse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
		var point = mantissa.IndexOf('.');
		var whole = point < 0 ? mantissa : mantissa.Substring(0, point);
		var all = whole + (point < 0 ? "" : mantissa.Substring(point + 1));
		var lead = 0;
		while (lead < all.Length - 1 && all[lead] == '0') lead++;
		digits = all.Substring(lead).TrimEnd('0');
		exponent = power + whole.Length - 1 - lead;
	}

	// The shortest digits of a positive finite number that read back as it, the nearest to it among those (Steele and
	// White, with exact integers). The bits without the sign; the size of the fraction and the bias of the format.
	internal static void ExactDigits(long bits, int fractionBits, int bias, out string digits, out int exponent)
	{
		var biased = (int)(bits >> fractionBits);
		var fraction = bits & ((1L << fractionBits) - 1);
		var mantissa = biased == 0 ? fraction : fraction | (1L << fractionBits);
		var power = (biased == 0 ? 1 : biased) - bias;
		// The numbers that read back as this one: between the midpoints with the neighbours, the midpoints included
		// when the mantissa is even (round half to even). Below a power of two the neighbour is half as far.
		var scale = fraction == 0 && biased > 1 ? 4 : 2;
		var value = new BigInteger(mantissa) * scale;
		var high = value + scale / 2;
		var low = value - 1;
		var denominator = new BigInteger(scale);
		if (power >= 0)
		{
			value <<= power;
			high <<= power;
			low <<= power;
		}
		else
		{
			denominator <<= -power;
		}
		var inclusive = mantissa % 2 == 0;

		var k = (int)Math.Floor(BigInteger.Log10(value) - BigInteger.Log10(denominator));
		while (Compare(value, denominator, k) < 0) k--;
		while (Compare(value, denominator, k + 1) >= 0) k++;

		var maxDigits = fractionBits > 23 ? 17 : 9;
		for (var precision = 1; precision <= maxDigits; precision++)
		{
			var shift = k - precision + 1;
			var rounded = RoundHalfEven(value, denominator, shift);
			BigInteger? best = null;
			BigInteger bestDistance = default;
			// The nearest number of this many digits, and its neighbours for the uneven bounds below a power of two.
			// All in units of 10^shift, so 10^precision (a rounding up to the next power of ten) is one digit too.
			foreach (var n in new[] { rounded, rounded - 1, rounded + 1 })
			{
				if (n <= 0 || !Inside(n, shift, low, high, denominator, inclusive)) continue;
				var distance = BigInteger.Abs(Numerator(n, shift, denominator) - Scale(value, shift));
				if (best is null || distance < bestDistance || distance == bestDistance && n.IsEven)
				{
					best = n;
					bestDistance = distance;
				}
			}
			if (best is { } found)
			{
				var text = found.ToString(CultureInfo.InvariantCulture);
				digits = text.TrimEnd('0');
				exponent = shift + text.Length - 1;
				return;
			}
		}
		throw new InvalidOperationException("No digits read back as the number.");
	}

	// The powers of ten up to the ones of the smallest subnormal double, computed once.
	private static readonly BigInteger[] Powers = MakePowers(400);

	private static BigInteger[] MakePowers(int count)
	{
		var powers = new BigInteger[count];
		powers[0] = BigInteger.One;
		for (var i = 1; i < count; i++) powers[i] = powers[i - 1] * 10;
		return powers;
	}

	private static BigInteger Pow10(int n) => n < Powers.Length ? Powers[n] : BigInteger.Pow(10, n);

	// value / denominator against 10^k.
	private static int Compare(BigInteger value, BigInteger denominator, int k)
		=> k >= 0 ? value.CompareTo(denominator * Pow10(k)) : (value * Pow10(-k)).CompareTo(denominator);

	// value / denominator / 10^shift, rounded half to even.
	private static BigInteger RoundHalfEven(BigInteger value, BigInteger denominator, int shift)
	{
		var num = shift < 0 ? value * Pow10(-shift) : value;
		var den = shift > 0 ? denominator * Pow10(shift) : denominator;
		var q = BigInteger.DivRem(num, den, out var r);
		var twice = (r * 2).CompareTo(den);
		return twice > 0 || twice == 0 && !q.IsEven ? q + 1 : q;
	}

	// n * 10^e and a bound x / denominator over the same denominator, denominator * 10^-e when e < 0.
	private static BigInteger Numerator(BigInteger n, int e, BigInteger denominator) => e >= 0 ? n * Pow10(e) * denominator : n * denominator;
	private static BigInteger Scale(BigInteger x, int e) => e >= 0 ? x : x * Pow10(-e);

	private static bool Inside(BigInteger n, int e, BigInteger low, BigInteger high, BigInteger denominator, bool inclusive)
	{
		var number = Numerator(n, e, denominator);
		var cLow = number.CompareTo(Scale(low, e));
		var cHigh = number.CompareTo(Scale(high, e));
		return inclusive ? cLow >= 0 && cHigh <= 0 : cLow > 0 && cHigh < 0;
	}

	// Plain notation from 10^-4 to below 10^15, as .NET writes doubles, d.dddE+XX outside.
	private static string Write(bool negative, string digits, int exponent)
	{
		var sb = new StringBuilder(digits.Length + 8);
		if (negative) sb.Append('-');
		if (exponent >= -4 && exponent < 15)
		{
			if (exponent < 0)
			{
				sb.Append("0.").Append('0', -exponent - 1).Append(digits);
			}
			else if (digits.Length <= exponent + 1)
			{
				sb.Append(digits).Append('0', exponent + 1 - digits.Length).Append(".0");
			}
			else
			{
				sb.Append(digits, 0, exponent + 1).Append('.').Append(digits, exponent + 1, digits.Length - exponent - 1);
			}
		}
		else
		{
			sb.Append(digits[0]);
			if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
			sb.Append('E').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
		}
		return sb.ToString();
	}
}
