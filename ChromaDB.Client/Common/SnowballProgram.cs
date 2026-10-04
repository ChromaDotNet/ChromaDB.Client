// Copyright (c) 2001, Dr Martin Porter
// Copyright (c) 2004,2005, Richard Boulton
// Copyright (c) 2013, Yoshiki Shibukawa
// Copyright (c) 2006-2025, Olly Betts
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
//   1. Redistributions of source code must retain the above copyright notice,
//      this list of conditions and the following disclaimer.
//   2. Redistributions in binary form must reproduce the above copyright notice,
//      this list of conditions and the following disclaimer in the documentation
//      and/or other materials provided with the distribution.
//   3. Neither the name of the Snowball project nor the names of its contributors
//      may be used to endorse or promote products derived from this software
//      without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
// ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

// Port of basestemmer.py and among.py of the Python package snowballstemmer 3.1.1, the base of the port of english_stemmer.py.

using System.Diagnostics;

namespace ChromaDB.Client.Common;

// The state of one stemming and the operations of the generated code on it, as BaseStemmer of snowballstemmer, with only the
// operations the English stemmer uses. An instance stems one word: SnowballEnglishStemmer creates one for each call.
internal abstract class SnowballProgram
{
	// The word as code points, not UTF-16 code units: Python indexes a string by code point, so a character outside the BMP is one
	// position, as in snowballstemmer. A surrogate without its pair stays a code point of its own, as in a Python string.
	protected int[] current;
	int _length;
	protected int cursor;
	protected int limit;
	protected int limitBackward;
	protected int bra;
	protected int ket;

	// set_current.
	protected SnowballProgram(string value)
	{
		// The English replacements never make the word longer than it was (the "e" of step 1b comes after the removal of a longer
		// suffix), and ReplaceS grows the array if one did.
		current = new int[value.Length];
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
			{
				current[_length++] = char.ConvertToUtf32(c, value[i + 1]);
				i++;
			}
			else
			{
				current[_length++] = c;
			}
		}
		cursor = 0;
		limit = _length;
		limitBackward = 0;
		bra = cursor;
		ket = limit;
	}

	// get_current.
	public string GetCurrent()
	{
		var chars = new char[_length * 2];
		var n = 0;
		for (var i = 0; i < _length; i++)
		{
			var c = current[i];
			if (c > char.MaxValue)
			{
				c -= 0x10000;
				chars[n++] = (char)(0xD800 + (c >> 10));
				chars[n++] = (char)(0xDC00 + (c & 0x3FF));
			}
			else
			{
				chars[n++] = (char)c;
			}
		}
		return new string(chars, 0, n);
	}

	// The groupings are strings of characters, as "aeo" in the Python code.
	static bool InSet(string s, int c) => c <= char.MaxValue && s.IndexOf((char)c) >= 0;

	protected bool InGrouping(string s)
	{
		if (cursor >= limit)
		{
			return false;
		}
		if (!InSet(s, current[cursor]))
		{
			return false;
		}
		cursor++;
		return true;
	}

	protected bool GoInGrouping(string s)
	{
		while (cursor < limit)
		{
			if (!InSet(s, current[cursor]))
			{
				return true;
			}
			cursor++;
		}
		return false;
	}

	protected bool InGroupingB(string s)
	{
		if (cursor <= limitBackward)
		{
			return false;
		}
		if (!InSet(s, current[cursor - 1]))
		{
			return false;
		}
		cursor--;
		return true;
	}

	protected bool GoOutGrouping(string s)
	{
		while (cursor < limit)
		{
			if (InSet(s, current[cursor]))
			{
				return true;
			}
			cursor++;
		}
		return false;
	}

	protected bool OutGroupingB(string s)
	{
		if (cursor <= limitBackward)
		{
			return false;
		}
		if (!InSet(s, current[cursor - 1]))
		{
			cursor--;
			return true;
		}
		return false;
	}

	protected bool GoOutGroupingB(string s)
	{
		while (cursor > limitBackward)
		{
			if (InSet(s, current[cursor - 1]))
			{
				return true;
			}
			cursor--;
		}
		return false;
	}

	// eq_s_b: current.endswith(s, limit_backward, cursor).
	protected bool EqSB(string s)
	{
		if (cursor - limitBackward < s.Length)
		{
			return false;
		}
		var start = cursor - s.Length;
		for (var i = 0; i < s.Length; i++)
		{
			if (current[start + i] != s[i])
			{
				return false;
			}
		}
		cursor -= s.Length;
		return true;
	}

	protected int FindAmong(Among[] v)
	{
		var i = 0;
		var j = v.Length;

		var c = cursor;
		var l = limit;

		var commonI = 0;
		var commonJ = 0;

		var firstKeyInspected = false;

		while (true)
		{
			var k = i + ((j - i) >> 1);
			var diff = 0;
			var common = Math.Min(commonI, commonJ); // smaller
			var w = v[k];
			for (var i2 = common; i2 < w.S.Length; i2++)
			{
				if (c + common == l)
				{
					diff = -1;
					break;
				}
				diff = current[c + common] - w.S[i2];
				if (diff != 0)
				{
					break;
				}
				common++;
			}
			if (diff < 0)
			{
				j = k;
				commonJ = common;
			}
			else
			{
				i = k;
				commonI = common;
			}
			if (j - i <= 1)
			{
				if (i > 0)
				{
					break; // v->s has been inspected
				}
				if (j == i)
				{
					break; // only one item in v
				}
				// - but now we need to go round once more to get
				// v->s inspected. This looks messy, but is actually
				// the optimal approach.
				if (firstKeyInspected)
				{
					break;
				}
				firstKeyInspected = true;
			}
		}
		while (true)
		{
			var w = v[i];
			if (commonI >= w.S.Length)
			{
				// The English tables have no among functions: w.method is always None.
				cursor = c + w.S.Length;
				return w.Result;
			}
			i = w.SubstringI;
			if (i < 0)
			{
				return 0;
			}
		}
	}

	// find_among_b is for backwards processing. Same comments apply.
	protected int FindAmongB(Among[] v)
	{
		var i = 0;
		var j = v.Length;

		var c = cursor;
		var lb = limitBackward;

		var commonI = 0;
		var commonJ = 0;

		var firstKeyInspected = false;

		while (true)
		{
			var k = i + ((j - i) >> 1);
			var diff = 0;
			var common = Math.Min(commonI, commonJ);
			var w = v[k];
			for (var i2 = w.S.Length - 1 - common; i2 >= 0; i2--)
			{
				if (c - common == lb)
				{
					diff = -1;
					break;
				}
				diff = current[c - 1 - common] - w.S[i2];
				if (diff != 0)
				{
					break;
				}
				common++;
			}
			if (diff < 0)
			{
				j = k;
				commonJ = common;
			}
			else
			{
				i = k;
				commonI = common;
			}
			if (j - i <= 1)
			{
				if (i > 0)
				{
					break;
				}
				if (j == i)
				{
					break;
				}
				if (firstKeyInspected)
				{
					break;
				}
				firstKeyInspected = true;
			}
		}
		while (true)
		{
			var w = v[i];
			if (commonI >= w.S.Length)
			{
				cursor = c - w.S.Length;
				return w.Result;
			}
			i = w.SubstringI;
			if (i < 0)
			{
				return 0;
			}
		}
	}

	// Replaces the characters between cBra and cKet by the characters of s.
	int ReplaceS(int cBra, int cKet, string s)
	{
		var adjustment = s.Length - (cKet - cBra);
		var length = _length + adjustment;
		if (length > current.Length)
		{
			Array.Resize(ref current, length);
		}
		// Array.Copy copies overlapping ranges as if through a temporary array.
		Array.Copy(current, cKet, current, cKet + adjustment, _length - cKet);
		for (var i = 0; i < s.Length; i++)
		{
			current[cBra + i] = s[i];
		}
		_length = length;
		limit += adjustment;
		if (cursor >= cKet)
		{
			cursor += adjustment;
		}
		else if (cursor > cBra)
		{
			cursor = cBra;
		}
		return adjustment;
	}

	protected void SliceFrom(string s)
	{
		Debug.Assert(bra >= 0);
		Debug.Assert(bra <= ket);
		Debug.Assert(ket <= limit);
		Debug.Assert(limit <= _length);
		ReplaceS(bra, ket, s);
		ket = bra + s.Length;
	}

	protected void SliceDel() => SliceFrom("");

	// An entry of a table of the generated code: the string, the index of the longest entry that is a substring of it (-1 when
	// there is none) and the result of the lookup.
	internal sealed class Among
	{
		public Among(string s, int substringI, int result)
		{
			S = s;
			SubstringI = substringI;
			Result = result;
		}

		public string S { get; }
		public int SubstringI { get; }
		public int Result { get; }
	}
}
