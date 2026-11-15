using System.Globalization;

namespace ChromaDB.Client.Backup.Common;

// The text of a number that reads back as the same bits, as short as the runtime gives it. "R" is the shortest form on .NET Core 3.0
// and later; on .NET Framework it can lose the last bit of a double, so the text is read back and, when it differs, the form with all
// the digits goes: G9 for a float, G17 for a double. NaN and the infinities have no JSON text: the callers write null for them.
internal static class JsonNumbers
{
	public static string Float(float value)
	{
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value) && SameSign(text, value) ? text : value.ToString("G9", CultureInfo.InvariantCulture);
	}

	// A double keeps a decimal point or an exponent, so that Chroma reads it as a float: 2.0, not 2.
	public static string Double(double value)
	{
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		if (!double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).Equals(value) || !SameSign(text, value))
		{
			text = value.ToString("G17", CultureInfo.InvariantCulture);
		}
		return text.IndexOfAny(['.', 'E', 'e']) < 0 ? text + ".0" : text;
	}

	public static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

	public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

	public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

	// -0 equals 0, but its text must keep the sign: 1 / -0 is -infinity.
	private static bool SameSign(string text, float value) => value != 0 || text.StartsWith("-", StringComparison.Ordinal) == (1 / value < 0);

	private static bool SameSign(string text, double value) => value != 0 || text.StartsWith("-", StringComparison.Ordinal) == (1 / value < 0);
}
