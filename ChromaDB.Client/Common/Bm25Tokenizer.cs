using System.Text;

namespace ChromaDB.Client.Common;

// The tokenizer of the BM25 function of Chroma, as bm25_tokenizer.py of its Python client 1.5.9 does it:
// the runs of characters that are neither word characters nor spaces become a space, the text is lowercased and split on spaces,
// the stopwords and the tokens longer than the limit are dropped, and the others are stemmed with Snowball English.
// Word characters, spaces and lowercase follow the rules of Python, from the tables of Bm25Unicode, not those of the .NET runtime,
// which differ in some characters and know fewer of them on .NET Framework.
internal sealed class Bm25Tokenizer(SnowballEnglishStemmer stemmer, HashSet<string> stopwords, int tokenMaxLength)
{
	public List<string> Tokenize(string text)
	{
		var tokens = new List<string>();
		foreach (var token in Split(PythonLower(RemoveNonAlphanumeric(text))))
		{
			if (stopwords.Contains(token) || CodePoints(token) > tokenMaxLength)
			{
				continue;
			}
			var stemmed = PythonStrip(stemmer.Stem(token));
			if (stemmed.Length > 0)
			{
				tokens.Add(stemmed);
			}
		}
		return tokens;
	}

	// re.sub(r"[^\w\s]+", " ", text, flags=re.UNICODE)
	static string RemoveNonAlphanumeric(string text)
	{
		var result = new StringBuilder(text.Length);
		var inRun = false;
		for (var i = 0; i < text.Length; i += CodePointLength(text, i))
		{
			var codePoint = CodePointAt(text, i);
			if (IsPythonWord(codePoint) || IsPythonSpace(codePoint))
			{
				result.Append(text, i, CodePointLength(text, i));
				inRun = false;
			}
			else if (!inRun)
			{
				result.Append(' ');
				inRun = true;
			}
		}
		return result.ToString();
	}

	// str.split(): the runs of spaces separate the tokens.
	static IEnumerable<string> Split(string text)
	{
		var start = -1;
		for (var i = 0; i < text.Length; i += CodePointLength(text, i))
		{
			if (IsPythonSpace(CodePointAt(text, i)))
			{
				if (start >= 0)
				{
					yield return text.Substring(start, i - start);
					start = -1;
				}
			}
			else if (start < 0)
			{
				start = i;
			}
		}
		if (start >= 0)
		{
			yield return text.Substring(start);
		}
	}

	// str.lower(): the lowercase of each character, and a capital sigma at the end of a word becomes a final sigma.
	internal static string PythonLower(string text)
	{
		var codePoints = new List<int>(text.Length);
		for (var i = 0; i < text.Length; i += CodePointLength(text, i))
		{
			codePoints.Add(CodePointAt(text, i));
		}
		var result = new StringBuilder(text.Length);
		for (var i = 0; i < codePoints.Count; i++)
		{
			var codePoint = codePoints[i];
			if (Bm25Unicode.LowercaseLonger.TryGetValue(codePoint, out var longer))
			{
				result.Append(longer);
			}
			else if (codePoint == 0x03A3)
			{
				result.Append(IsFinalSigma(codePoints, i) ? 'ς' : 'σ');
			}
			else
			{
				AppendCodePoint(result, Lowercase(codePoint));
			}
		}
		return result.ToString();
	}

	// The Final_Sigma context of Unicode, as Python checks it: a cased character before, skipping the case-ignorable ones,
	// and no cased character after.
	static bool IsFinalSigma(List<int> codePoints, int index)
	{
		var before = index - 1;
		while (before >= 0 && IsCaseIgnorable(codePoints[before]))
		{
			before--;
		}
		if (before < 0 || !IsCased(codePoints[before]))
		{
			return false;
		}
		var after = index + 1;
		while (after < codePoints.Count && IsCaseIgnorable(codePoints[after]))
		{
			after++;
		}
		return after == codePoints.Count || !IsCased(codePoints[after]);
	}

	static bool IsPythonWord(int codePoint) => InRanges(Bm25Unicode.WordRanges, codePoint);

	internal static bool IsPythonSpace(int codePoint) => InRanges(Bm25Unicode.SpaceRanges, codePoint);

	static bool IsCased(int codePoint) => InRanges(Bm25Unicode.CasedRanges, codePoint);

	static bool IsCaseIgnorable(int codePoint) => InRanges(Bm25Unicode.CaseIgnorableRanges, codePoint);

	// Binary search on pairs of first and last code point.
	static bool InRanges(int[] ranges, int codePoint)
	{
		int low = 0, high = ranges.Length / 2 - 1;
		while (low <= high)
		{
			var middle = (low + high) / 2;
			if (codePoint < ranges[middle * 2])
			{
				high = middle - 1;
			}
			else if (codePoint > ranges[middle * 2 + 1])
			{
				low = middle + 1;
			}
			else
			{
				return true;
			}
		}
		return false;
	}

	// Binary search on pairs of code point and lowercase code point.
	static int Lowercase(int codePoint)
	{
		var pairs = Bm25Unicode.Lowercase;
		int low = 0, high = pairs.Length / 2 - 1;
		while (low <= high)
		{
			var middle = (low + high) / 2;
			var key = pairs[middle * 2];
			if (codePoint < key)
			{
				high = middle - 1;
			}
			else if (codePoint > key)
			{
				low = middle + 1;
			}
			else
			{
				return pairs[middle * 2 + 1];
			}
		}
		return codePoint;
	}

	// str.strip()
	static string PythonStrip(string text)
	{
		var start = 0;
		while (start < text.Length && IsPythonSpace(text[start]))
		{
			start++;
		}
		var end = text.Length;
		while (end > start && IsPythonSpace(text[end - 1]))
		{
			end--;
		}
		return text.Substring(start, end - start);
	}

	// len() of Python counts code points.
	internal static int CodePoints(string text)
	{
		var count = 0;
		for (var i = 0; i < text.Length; i += CodePointLength(text, i))
		{
			count++;
		}
		return count;
	}

	// An unpaired surrogate counts as one code point, as in Python.
	static int CodePointAt(string text, int index)
		=> CodePointLength(text, index) == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];

	static int CodePointLength(string text, int index)
		=> char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

	static void AppendCodePoint(StringBuilder builder, int codePoint)
	{
		if (codePoint < 0x10000)
		{
			builder.Append((char)codePoint);
		}
		else
		{
			builder.Append(char.ConvertFromUtf32(codePoint));
		}
	}
}
