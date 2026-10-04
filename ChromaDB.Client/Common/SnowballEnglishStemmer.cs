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

// Port of english_stemmer.py of the Python package snowballstemmer 3.1.1.

namespace ChromaDB.Client.Common;

// The Snowball English (Porter2) stemmer, with the stems of snowballstemmer 3.1.1 (generated from english.sbl by Snowball 3.1.1).
// It keeps no state between calls, so one instance can stem from several threads at once.
internal sealed class SnowballEnglishStemmer
{
	// stemWord.
	public string Stem(string word)
	{
		var program = new EnglishProgram(word);
		program.Stem();
		return program.GetCurrent();
	}

	// EnglishStemmer of english_stemmer.py: the routines in the same order, with the same names of the variables.
	sealed class EnglishProgram : SnowballProgram
	{
		// g_aeo, g_v, g_v_WXY and g_valid_LI.
		const string GAeo = "aeo";
		const string GV = "aeiouy";
		const string GVWXY = "Yaeiouwxy";
		const string GValidLI = "cdeghkmnrt";

		bool _yFound;
		int _p2;
		int _p1;

		public EnglishProgram(string word)
			: base(word)
		{ }

		bool Prelude()
		{
			_yFound = false;
			var v1 = cursor;
			bra = cursor;
			if (cursor != limit && current[cursor] == '\'')
			{
				cursor++;
				ket = cursor;
				SliceDel();
			}
			cursor = v1;
			var v2 = cursor;
			bra = cursor;
			if (cursor != limit && current[cursor] == 'y')
			{
				cursor++;
				ket = cursor;
				SliceFrom("Y");
				_yFound = true;
			}
			cursor = v2;
			var v3 = cursor;
			while (true)
			{
				var v4 = cursor;
				var found = false;
				while (true)
				{
					var v5 = cursor;
					if (InGrouping(GV))
					{
						bra = cursor;
						if (cursor != limit && current[cursor] == 'y')
						{
							cursor++;
							ket = cursor;
							cursor = v5;
							found = true;
							break;
						}
					}
					cursor = v5;
					if (cursor >= limit)
					{
						break;
					}
					cursor++;
				}
				if (!found)
				{
					cursor = v4;
					break;
				}
				SliceFrom("Y");
				_yFound = true;
			}
			cursor = v3;
			return true;
		}

		bool MarkRegions()
		{
			_p1 = limit;
			_p2 = limit;
			var v1 = cursor;
			do
			{
				var v2 = cursor;
				if (FindAmong(A0) == 0)
				{
					cursor = v2;
					if (!GoOutGrouping(GV))
					{
						break;
					}
					cursor++;
					if (!GoInGrouping(GV))
					{
						break;
					}
					cursor++;
				}
				_p1 = cursor;
				if (!GoOutGrouping(GV))
				{
					break;
				}
				cursor++;
				if (!GoInGrouping(GV))
				{
					break;
				}
				cursor++;
				_p2 = cursor;
			}
			while (false);
			cursor = v1;
			return true;
		}

		bool Shortv()
		{
			var v1 = limit - cursor;
			if (OutGroupingB(GVWXY) && InGroupingB(GV) && OutGroupingB(GV))
			{
				return true;
			}
			cursor = limit - v1;
			if (OutGroupingB(GV) && InGroupingB(GV) && cursor <= limitBackward)
			{
				return true;
			}
			cursor = limit - v1;
			if (!EqSB("past"))
			{
				return false;
			}
			return true;
		}

		bool R1() => _p1 <= cursor;

		bool R2() => _p2 <= cursor;

		bool Step1a()
		{
			var v1 = limit - cursor;
			ket = cursor;
			if (FindAmongB(A1) == 0)
			{
				cursor = limit - v1;
			}
			else
			{
				bra = cursor;
				SliceDel();
			}
			ket = cursor;
			var amongVar = FindAmongB(A2);
			if (amongVar == 0)
			{
				return false;
			}
			bra = cursor;
			if (amongVar == 1)
			{
				SliceFrom("ss");
			}
			else if (amongVar == 2)
			{
				var v2 = limit - cursor;
				if (cursor - 2 >= limitBackward)
				{
					cursor -= 2;
					SliceFrom("i");
				}
				else
				{
					cursor = limit - v2;
					SliceFrom("ie");
				}
			}
			else if (amongVar == 3)
			{
				if (cursor <= limitBackward)
				{
					return false;
				}
				cursor--;
				if (!GoOutGroupingB(GV))
				{
					return false;
				}
				cursor--;
				SliceDel();
			}
			return true;
		}

		bool Step1b()
		{
			ket = cursor;
			var amongVar = FindAmongB(A5);
			bra = cursor;
			var v1 = limit - cursor;
			// The first alternative: a break goes to the second one.
			do
			{
				if (amongVar == 1)
				{
					var v2 = limit - cursor;
					if (R1())
					{
						var v3 = limit - cursor;
						if (FindAmongB(A3) == 0 || cursor > limitBackward)
						{
							cursor = limit - v3;
							SliceFrom("ee");
						}
					}
					cursor = limit - v2;
				}
				else if (amongVar == 2)
				{
					break;
				}
				else if (amongVar == 3)
				{
					amongVar = FindAmongB(A4);
					if (amongVar == 0)
					{
						break;
					}
					if (amongVar == 1)
					{
						var v4 = limit - cursor;
						if (!OutGroupingB(GV))
						{
							break;
						}
						if (cursor > limitBackward)
						{
							break;
						}
						cursor = limit - v4;
						bra = cursor;
						SliceFrom("ie");
					}
					else
					{
						if (cursor > limitBackward)
						{
							break;
						}
					}
				}
				return true;
			}
			while (false);
			cursor = limit - v1;
			var v5 = limit - cursor;
			if (!GoOutGroupingB(GV))
			{
				return false;
			}
			cursor--;
			cursor = limit - v5;
			SliceDel();
			ket = cursor;
			bra = cursor;
			var v6 = limit - cursor;
			amongVar = FindAmongB(A6);
			if (amongVar == 1)
			{
				SliceFrom("e");
				return false;
			}
			else if (amongVar == 2)
			{
				var v7 = limit - cursor;
				if (InGroupingB(GAeo) && cursor <= limitBackward)
				{
					return false;
				}
				cursor = limit - v7;
			}
			else
			{
				if (cursor != _p1)
				{
					return false;
				}
				var v8 = limit - cursor;
				if (!Shortv())
				{
					return false;
				}
				cursor = limit - v8;
				SliceFrom("e");
				return false;
			}
			cursor = limit - v6;
			ket = cursor;
			if (cursor <= limitBackward)
			{
				return false;
			}
			cursor--;
			bra = cursor;
			SliceDel();
			return true;
		}

		bool Step1c()
		{
			ket = cursor;
			if (cursor > limitBackward && current[cursor - 1] == 'y')
			{
				cursor--;
			}
			else
			{
				if (cursor <= limitBackward || current[cursor - 1] != 'Y')
				{
					return false;
				}
				cursor--;
			}
			bra = cursor;
			if (!OutGroupingB(GV))
			{
				return false;
			}
			if (cursor <= limitBackward)
			{
				return false;
			}
			SliceFrom("i");
			return true;
		}

		bool Step2()
		{
			ket = cursor;
			var amongVar = FindAmongB(A7);
			if (amongVar == 0)
			{
				return false;
			}
			bra = cursor;
			if (!R1())
			{
				return false;
			}
			switch (amongVar)
			{
				case 1:
					SliceFrom("tion");
					break;
				case 2:
					SliceFrom("ence");
					break;
				case 3:
					SliceFrom("ance");
					break;
				case 4:
					SliceFrom("able");
					break;
				case 5:
					SliceFrom("ent");
					break;
				case 6:
					SliceFrom("ize");
					break;
				case 7:
					SliceFrom("ate");
					break;
				case 8:
					SliceFrom("al");
					break;
				case 9:
					SliceFrom("ful");
					break;
				case 10:
					SliceFrom("ous");
					break;
				case 11:
					SliceFrom("ive");
					break;
				case 12:
					SliceFrom("ble");
					break;
				case 13:
					SliceFrom("og");
					break;
				case 14:
					if (cursor <= limitBackward || current[cursor - 1] != 'l')
					{
						return false;
					}
					cursor--;
					SliceFrom("og");
					break;
				case 15:
					SliceFrom("less");
					break;
				default:
					if (!InGroupingB(GValidLI))
					{
						return false;
					}
					SliceDel();
					break;
			}
			return true;
		}

		bool Step3()
		{
			ket = cursor;
			var amongVar = FindAmongB(A8);
			if (amongVar == 0)
			{
				return false;
			}
			bra = cursor;
			if (!R1())
			{
				return false;
			}
			switch (amongVar)
			{
				case 1:
					SliceFrom("tion");
					break;
				case 2:
					SliceFrom("ate");
					break;
				case 3:
					SliceFrom("al");
					break;
				case 4:
					SliceFrom("ic");
					break;
				case 5:
					SliceDel();
					break;
				default:
					if (!R2())
					{
						return false;
					}
					SliceDel();
					break;
			}
			return true;
		}

		bool Step4()
		{
			ket = cursor;
			var amongVar = FindAmongB(A9);
			if (amongVar == 0)
			{
				return false;
			}
			bra = cursor;
			if (!R2())
			{
				return false;
			}
			if (amongVar == 1)
			{
				SliceDel();
			}
			else
			{
				if (cursor > limitBackward && current[cursor - 1] == 's')
				{
					cursor--;
				}
				else
				{
					if (cursor <= limitBackward || current[cursor - 1] != 't')
					{
						return false;
					}
					cursor--;
				}
				SliceDel();
			}
			return true;
		}

		bool Step5()
		{
			ket = cursor;
			var amongVar = FindAmongB(A10);
			if (amongVar == 0)
			{
				return false;
			}
			bra = cursor;
			if (amongVar == 1)
			{
				if (!R2())
				{
					if (!R1())
					{
						return false;
					}
					var v1 = limit - cursor;
					if (Shortv())
					{
						return false;
					}
					cursor = limit - v1;
				}
				SliceDel();
			}
			else
			{
				if (!R2())
				{
					return false;
				}
				if (cursor <= limitBackward || current[cursor - 1] != 'l')
				{
					return false;
				}
				cursor--;
				SliceDel();
			}
			return true;
		}

		bool Exception1()
		{
			bra = cursor;
			var amongVar = FindAmong(A11);
			if (amongVar == 0)
			{
				return false;
			}
			ket = cursor;
			if (cursor < limit)
			{
				return false;
			}
			if (amongVar > 0)
			{
				SliceFrom(As11[amongVar - 1]);
			}
			return true;
		}

		bool Postlude()
		{
			if (!_yFound)
			{
				return false;
			}
			while (true)
			{
				var v1 = cursor;
				var found = false;
				while (true)
				{
					var v2 = cursor;
					bra = cursor;
					if (cursor != limit && current[cursor] == 'Y')
					{
						cursor++;
						ket = cursor;
						cursor = v2;
						found = true;
						break;
					}
					cursor = v2;
					if (cursor >= limit)
					{
						break;
					}
					cursor++;
				}
				if (!found)
				{
					cursor = v1;
					break;
				}
				SliceFrom("y");
			}
			return true;
		}

		// _stem.
		public bool Stem()
		{
			var v1 = cursor;
			if (Exception1())
			{
				return true;
			}
			cursor = v1;
			// Words shorter than 3 characters stay as they are.
			if (cursor + 3 > limit)
			{
				return true;
			}
			Prelude();
			MarkRegions();
			limitBackward = cursor;
			cursor = limit;
			var v2 = limit - cursor;
			Step1a();
			cursor = limit - v2;
			var v3 = limit - cursor;
			Step1b();
			cursor = limit - v3;
			var v4 = limit - cursor;
			Step1c();
			cursor = limit - v4;
			var v5 = limit - cursor;
			Step2();
			cursor = limit - v5;
			var v6 = limit - cursor;
			Step3();
			cursor = limit - v6;
			var v7 = limit - cursor;
			Step4();
			cursor = limit - v7;
			var v8 = limit - cursor;
			Step5();
			cursor = limit - v8;
			cursor = limitBackward;
			var v9 = cursor;
			Postlude();
			cursor = v9;
			return true;
		}

		static readonly Among[] A0 =
		[
			new("arsen", -1, -1),
			new("commun", -1, -1),
			new("emerg", -1, -1),
			new("gener", -1, -1),
			new("inter", -1, -1),
			new("later", -1, -1),
			new("organ", -1, -1),
			new("past", -1, -1),
			new("univers", -1, -1),
		];

		static readonly Among[] A1 =
		[
			new("'", -1, 1),
			new("'s'", 0, 1),
			new("'s", -1, 1),
		];

		static readonly Among[] A2 =
		[
			new("ied", -1, 2),
			new("s", -1, 3),
			new("ies", 1, 2),
			new("sses", 1, 1),
			new("ss", 1, -1),
			new("us", 1, -1),
		];

		static readonly Among[] A3 =
		[
			new("succ", -1, 1),
			new("proc", -1, 1),
			new("exc", -1, 1),
		];

		static readonly Among[] A4 =
		[
			new("even", -1, 2),
			new("cann", -1, 2),
			new("inn", -1, 2),
			new("earr", -1, 2),
			new("herr", -1, 2),
			new("out", -1, 2),
			new("y", -1, 1),
		];

		static readonly Among[] A5 =
		[
			new("", -1, -1),
			new("ed", 0, 2),
			new("eed", 1, 1),
			new("ing", 0, 3),
			new("edly", 0, 2),
			new("eedly", 4, 1),
			new("ingly", 0, 2),
		];

		static readonly Among[] A6 =
		[
			new("", -1, 3),
			new("bb", 0, 2),
			new("dd", 0, 2),
			new("ff", 0, 2),
			new("gg", 0, 2),
			new("bl", 0, 1),
			new("mm", 0, 2),
			new("nn", 0, 2),
			new("pp", 0, 2),
			new("rr", 0, 2),
			new("at", 0, 1),
			new("tt", 0, 2),
			new("iz", 0, 1),
		];

		static readonly Among[] A7 =
		[
			new("anci", -1, 3),
			new("enci", -1, 2),
			new("ogi", -1, 14),
			new("li", -1, 16),
			new("bli", 3, 12),
			new("abli", 4, 4),
			new("alli", 3, 8),
			new("fulli", 3, 9),
			new("lessli", 3, 15),
			new("ousli", 3, 10),
			new("entli", 3, 5),
			new("aliti", -1, 8),
			new("biliti", -1, 12),
			new("iviti", -1, 11),
			new("tional", -1, 1),
			new("ational", 14, 7),
			new("alism", -1, 8),
			new("ation", -1, 7),
			new("ization", 17, 6),
			new("izer", -1, 6),
			new("ator", -1, 7),
			new("iveness", -1, 11),
			new("fulness", -1, 9),
			new("ousness", -1, 10),
			new("ogist", -1, 13),
		];

		static readonly Among[] A8 =
		[
			new("icate", -1, 4),
			new("ative", -1, 6),
			new("alize", -1, 3),
			new("iciti", -1, 4),
			new("ical", -1, 4),
			new("tional", -1, 1),
			new("ational", 5, 2),
			new("ful", -1, 5),
			new("ness", -1, 5),
		];

		static readonly Among[] A9 =
		[
			new("ic", -1, 1),
			new("ance", -1, 1),
			new("ence", -1, 1),
			new("able", -1, 1),
			new("ible", -1, 1),
			new("ate", -1, 1),
			new("ive", -1, 1),
			new("ize", -1, 1),
			new("iti", -1, 1),
			new("al", -1, 1),
			new("ism", -1, 1),
			new("ion", -1, 2),
			new("er", -1, 1),
			new("ous", -1, 1),
			new("ant", -1, 1),
			new("ent", -1, 1),
			new("ment", 15, 1),
			new("ement", 16, 1),
		];

		static readonly Among[] A10 =
		[
			new("e", -1, 1),
			new("l", -1, 2),
		];

		static readonly Among[] A11 =
		[
			new("andes", -1, -1),
			new("atlas", -1, -1),
			new("bias", -1, -1),
			new("cosmos", -1, -1),
			new("early", -1, 6),
			new("gently", -1, 4),
			new("howe", -1, -1),
			new("idly", -1, 3),
			new("news", -1, -1),
			new("only", -1, 7),
			new("singly", -1, 8),
			new("skies", -1, 2),
			new("skis", -1, 1),
			new("sky", -1, -1),
			new("ugly", -1, 5),
		];

		static readonly string[] As11 = ["ski", "sky", "idl", "gentl", "ugli", "earli", "onli", "singl"];
	}
}
