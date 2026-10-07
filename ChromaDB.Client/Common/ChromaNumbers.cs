using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

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

	// Works around KD-7 (docs/COMPATIBILITY.md)
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
		var bits = new SingleBits { Value = value }.Bits;
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
		else if (!FloatDigits(bits & int.MaxValue, out digits, out exponent))
		{
			ExactDigits(bits & int.MaxValue, 23, 150, out digits, out exponent);
		}
		return Write(bits < 0, digits, exponent);
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
	private struct SingleBits
	{
		[System.Runtime.InteropServices.FieldOffset(0)] public float Value;
		[System.Runtime.InteropServices.FieldOffset(0)] public int Bits;
	}

	// The digits of ExactDigits for a float, with doubles: a float, its neighbours and the midpoints between them are
	// exact doubles, and every step here errs by a few parts in 10^16, far below the gap between two floats. Where a
	// decision comes closer than that to a bound (a rounding half way, a candidate on a midpoint, two candidates as
	// near), it gives up and the exact digits decide: the same result as ExactDigits, on every runtime, about twenty
	// times faster on .NET Framework, where the embeddings of a query go through here.
	internal static bool FloatDigits(int bits, out string digits, out int exponent)
	{
		digits = "";
		exponent = 0;
		var biased = bits >> 23;
		var fraction = bits & ((1 << 23) - 1);
		var mantissa = biased == 0 ? fraction : fraction | (1 << 23);
		var power = (biased == 0 ? 1 : biased) - 150;
		var value = mantissa * Pow2(power);
		var high = value + Pow2(power - 1);
		var low = value - (fraction == 0 && biased > 1 ? Pow2(power - 2) : Pow2(power - 1));
		var tolerance = value * 1e-12;

		var k = (int)Math.Floor(Math.Log10(value));
		var ratio = value / Pow10Double(k);
		if (ratio >= 10) k++;
		else if (ratio < 1) k--;
		ratio = value / Pow10Double(k);
		if (ratio < 1 + 1e-12 && ratio != 1 || ratio > 10 - 1e-11) return false;

		for (var precision = 1; precision <= 9; precision++)
		{
			var shift = k - precision + 1;
			var scaled = value / Pow10Double(shift);
			var rounded = Math.Floor(scaled + 0.5);
			if (Math.Abs(scaled - Math.Floor(scaled) - 0.5) < 1e-6) return false;
			var best = 0.0;
			var bestDistance = double.MaxValue;
			for (var c = rounded - 1; c <= rounded + 1; c++)
			{
				if (c <= 0) continue;
				var candidate = c * Pow10Double(shift);
				var aboveLow = candidate - low;
				var belowHigh = high - candidate;
				if (Math.Abs(aboveLow) <= tolerance || Math.Abs(belowHigh) <= tolerance) return false;
				if (aboveLow < 0 || belowHigh < 0) continue;
				var distance = Math.Abs(candidate - value);
				if (Math.Abs(distance - bestDistance) <= tolerance) return false;
				if (distance < bestDistance)
				{
					best = c;
					bestDistance = distance;
				}
			}
			if (best > 0)
			{
				var text = ((long)best).ToString(CultureInfo.InvariantCulture);
				digits = text.TrimEnd('0');
				exponent = shift + text.Length - 1;
				return true;
			}
		}
		return false;
	}

	private static double Pow2(int n) => n >= -1022 ? BitConverter.Int64BitsToDouble((long)(n + 1023) << 52) : Math.Pow(2, n);

	// 10^n for the floats, -55 to 40: exact up to 10^22, the nearest double beyond, so that a step errs by one rounding.
	private static readonly double[] PowersDouble = MakePowersDouble();

	private static double[] MakePowersDouble()
	{
		var powers = new double[96];
		for (var n = -55; n <= 40; n++)
		{
			powers[n + 55] = double.Parse("1E" + n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}
		return powers;
	}

	private static double Pow10Double(int n) => PowersDouble[n + 55];

	// A number of an answer, with the sign of -0.0, which .NET Framework drops when it parses "-0.0". On .NET Framework
	// System.Text.Json parses with the runtime, which reads about one in 250 of the shortest texts of doubles, the ones
	// Chroma writes, as the double next to it: there the digits are read here.
	public static double ReadDouble(ref Utf8JsonReader reader)
	{
		if (!RuntimeShortest && reader.TokenType == JsonTokenType.Number)
		{
			ReadOnlySpan<byte> text = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
			return ParseDouble(text);
		}
		var value = reader.GetDouble();
		return value == 0 && StartsWithMinus(ref reader) ? -0.0 : value;
	}

	// The double nearest to a JSON number, half to even, as the parsers of .NET Core 3.0 and later read it: with doubles
	// when that is exact (up to 15 digits and 10^22, a single rounding), with exact integers otherwise.
	internal static double ParseDouble(ReadOnlySpan<byte> text)
	{
		var i = 0;
		var negative = text.Length > 0 && text[0] == (byte)'-';
		if (negative) i++;
		var digits = new StringBuilder(24);
		var exponent = 0;
		var fraction = false;
		for (; i < text.Length; i++)
		{
			var c = text[i];
			if (c >= (byte)'0' && c <= (byte)'9')
			{
				if (digits.Length > 0 || c != (byte)'0') digits.Append((char)c);
				if (fraction) exponent--;
			}
			else if (c == (byte)'.')
			{
				fraction = true;
			}
			else
			{
				break;
			}
		}
		if (i < text.Length)
		{
			i++;
			var negativeExponent = false;
			if (i < text.Length && (text[i] == (byte)'+' || text[i] == (byte)'-'))
			{
				negativeExponent = text[i] == (byte)'-';
				i++;
			}
			var e = 0;
			for (; i < text.Length; i++) e = Math.Min(e * 10 + (text[i] - (byte)'0'), 100000);
			exponent += negativeExponent ? -e : e;
		}
		var length = digits.Length;
		while (length > 0 && digits[length - 1] == '0')
		{
			length--;
			exponent++;
		}
		double value;
		if (length == 0 || length + exponent < -330)
		{
			value = 0;
		}
		else if (length + exponent > 310)
		{
			value = double.PositiveInfinity;
		}
		else if (length <= 15 && exponent >= -22 && exponent <= 22)
		{
			var mantissa = (double)long.Parse(digits.ToString(0, length), CultureInfo.InvariantCulture);
			value = exponent >= 0 ? mantissa * ExactPowers[exponent] : mantissa / ExactPowers[-exponent];
		}
		else
		{
			value = Nearest(BigInteger.Parse(digits.ToString(0, length), CultureInfo.InvariantCulture), exponent);
		}
		return negative ? -value : value;
	}

	private static readonly double[] ExactPowers = [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22];

	// The double nearest to digits * 10^exponent.
	private static double Nearest(BigInteger digits, int exponent)
	{
		var num = exponent >= 0 ? digits * Pow10(exponent) : digits;
		var den = exponent >= 0 ? BigInteger.One : Pow10(-exponent);
		// The power of two of the 53 bits of the mantissa: estimated, then corrected.
		var e2 = (int)Math.Floor(BigInteger.Log(num, 2) - BigInteger.Log(den, 2)) - 52;
		while (true)
		{
			if (e2 < -1074) e2 = -1074;
			var q = BigInteger.DivRem(e2 >= 0 ? num : num << -e2, e2 >= 0 ? den << e2 : den, out var r);
			if (q >= Two53)
			{
				e2++;
				continue;
			}
			if (q < Two52 && e2 > -1074)
			{
				e2--;
				continue;
			}
			var twice = (r * 2).CompareTo(e2 >= 0 ? den << e2 : den);
			if (twice > 0 || twice == 0 && !q.IsEven) q++;
			if (q == Two53)
			{
				q = Two52;
				e2++;
			}
			if (e2 > 971) return double.PositiveInfinity;
			var bits = q < Two52 ? (long)q : ((long)(e2 + 1075) << 52) | (long)(q - Two52);
			return BitConverter.Int64BitsToDouble(bits);
		}
	}

	private static readonly BigInteger Two52 = BigInteger.One << 52;
	private static readonly BigInteger Two53 = BigInteger.One << 53;

	// Works around KD-9 and KD-10 (docs/COMPATIBILITY.md)
	// Chroma 1.x sends null for a float it cannot write, NaN or infinite, like an embedding beyond the range of a float in a cosine
	// collection: it reads as NaN, so that one record does not cost the whole answer. A number beyond the range of a float, like a
	// distance that Chroma 0.6.3 computes as a double, reads as an infinity, as a cast from the double does.
	public static float ReadSingle(ref Utf8JsonReader reader)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return float.NaN;
		}
		if (!reader.TryGetSingle(out var value))
		{
			return (float)ReadDouble(ref reader);
		}
		return value == 0 && StartsWithMinus(ref reader) ? -0f : value;
	}

	public static float ReadSingle(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Null)
		{
			return float.NaN;
		}
		if (!element.TryGetSingle(out var value))
		{
			return (float)element.GetDouble();
		}
		return value == 0 && element.GetRawText().StartsWith("-", StringComparison.Ordinal) ? -0f : value;
	}

	private static bool StartsWithMinus(ref Utf8JsonReader reader)
		=> (reader.HasValueSequence ? reader.ValueSequence.First.Span : reader.ValueSpan) is { Length: > 0 } span && span[0] == (byte)'-';

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
