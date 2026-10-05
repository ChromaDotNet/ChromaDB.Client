using System.Globalization;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The text of the doubles and floats in the requests: the fewest digits that read back as the number, ".0" on a whole
// number, the same on every build and runtime.
[TestFixture]
public class NumbersTests
{
	[TestCase(100.0, "100.0")]
	[TestCase(0.1, "0.1")]
	[TestCase(-0.0, "-0.0")]
	[TestCase(0.0, "0.0")]
	[TestCase(2.5, "2.5")]
	[TestCase(-1.5, "-1.5")]
	[TestCase(1e15, "1E+15")]
	[TestCase(123456789012345.0, "123456789012345.0")]
	[TestCase(0.0001, "0.0001")]
	[TestCase(1e-5, "1E-05")]
	[TestCase(1.0 / 3, "0.3333333333333333")]
	[TestCase(0.30000000000000004, "0.30000000000000004")]
	[TestCase(9007199254740993.0, "9.007199254740992E+15")]
	[TestCase(5e-324, "5E-324")]
	[TestCase(double.MaxValue, "1.7976931348623157E+308")]
	// 2^-25 and 2^-958: "R" of .NET 10 gives 16 digits that read back as the double below.
	[TestCase(2.98023223876953125E-08, "2.9802322387695312E-08")]
	public void Doubles(double value, string text)
	{
		Assert.That(ChromaNumbers.Format(value), Is.EqualTo(text));
	}

	[TestCase(100f, "100.0")]
	[TestCase(0.1f, "0.1")]
	[TestCase(-0f, "-0.0")]
	[TestCase(16777216f, "16777216.0")]
	[TestCase(1e-7f, "1E-07")]
	[TestCase(0.31594023f, "0.31594023")]
	[TestCase(float.MaxValue, "3.4028235E+38")]
	[TestCase(float.Epsilon, "1E-45")]
	public void Floats(float value, string text)
	{
		Assert.That(ChromaNumbers.Format(value), Is.EqualTo(text));
	}

	[Test]
	public void PowersOfTwoReadBack()
	{
		for (var e = -1074; e <= 1023; e++)
		{
			var value = Math.Pow(2, e);
			Assert.That(double.Parse(ChromaNumbers.Format(value), CultureInfo.InvariantCulture), Is.EqualTo(value), "2^" + e);
		}
		for (var e = -149; e <= 127; e++)
		{
			var value = (float)Math.Pow(2, e);
			Assert.That(float.Parse(ChromaNumbers.Format(value), CultureInfo.InvariantCulture), Is.EqualTo(value), "2^" + e);
		}
	}

	// The digits computed from the exact value, as on .NET Framework, are the ones of "R" on .NET 8 and later where those
	// read back, and every text reads back as the number.
	[Test]
	public void ExactDigitsAreTheShortest()
	{
		var random = new Random(20261005);
		var bytes = new byte[8];
		for (var i = 0; i < 20000; i++)
		{
			random.NextBytes(bytes);
			var d = Math.Abs(BitConverter.ToDouble(bytes, 0));
			if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) continue;
			ChromaNumbers.ExactDigits(BitConverter.DoubleToInt64Bits(d), 52, 1075, out var digits, out var exponent);
			var r = d.ToString("R", CultureInfo.InvariantCulture);
			if (double.Parse(r, CultureInfo.InvariantCulture) == d)
			{
				ChromaNumbers.FromText(r, out var rDigits, out var rExponent);
				Assert.That((digits, exponent), Is.EqualTo((rDigits, rExponent)), r);
			}
			Assert.That(double.Parse(ChromaNumbers.Format(d), CultureInfo.InvariantCulture), Is.EqualTo(d));

			var f = Math.Abs(BitConverter.ToSingle(bytes, 0));
			if (float.IsNaN(f) || float.IsInfinity(f) || f == 0) continue;
			ChromaNumbers.ExactDigits(BitConverter.ToInt32(BitConverter.GetBytes(f), 0), 23, 150, out digits, out exponent);
			var fr = f.ToString("R", CultureInfo.InvariantCulture);
			if (float.Parse(fr, CultureInfo.InvariantCulture) == f)
			{
				ChromaNumbers.FromText(fr, out var fDigits, out var fExponent);
				Assert.That((digits, exponent), Is.EqualTo((fDigits, fExponent)), fr);
			}
			Assert.That(float.Parse(ChromaNumbers.Format(f), CultureInfo.InvariantCulture), Is.EqualTo(f));
		}
	}

	[TestCase(double.NaN)]
	[TestCase(double.PositiveInfinity)]
	[TestCase(double.NegativeInfinity)]
	public void NotANumber(double value)
	{
		Assert.That(() => ChromaNumbers.Format(value), Throws.ArgumentException);
		Assert.That(() => ChromaNumbers.Format((float)value), Throws.ArgumentException);
	}

	// In a filter: a whole double stays a float, so a list of doubles is not a list of ints and floats, which Chroma rejects.
	[Test]
	public void Filters()
	{
		Assert.That(ChromaWhereOperator.Equal("k", 2.0).ToString(), Is.EqualTo("{\"k\":{\"$eq\":2.0}}"));
		Assert.That(ChromaWhereOperator.In("k", 0.0, 2.25).ToString(), Is.EqualTo("{\"k\":{\"$in\":[0.0,2.25]}}"));
		Assert.That(ChromaWhereOperator.Equal("k", 2L).ToString(), Is.EqualTo("{\"k\":{\"$eq\":2}}"));
		Assert.That(ChromaWhereOperator.GreaterThan("k", 0.1f).ToString(), Is.EqualTo("{\"k\":{\"$gt\":0.1}}"));
		Assert.That(ChromaWhereOperator.Equal("k", 3m).ToString(), Is.EqualTo("{\"k\":{\"$eq\":3.0}}"));
		Assert.That(ChromaWhereOperator.Equal("k", 2.50m).ToString(), Is.EqualTo("{\"k\":{\"$eq\":2.50}}"));
		Assert.That(ChromaWhereOperator.Equal("k", -0.0).ToString(), Is.EqualTo("{\"k\":{\"$eq\":-0.0}}"));
	}

	[Test]
	public void SparseVectors()
	{
		Assert.That(new ChromaSparseVector([1, 2], [1f, 0.5f]).ToString(), Does.Contain("\"values\":[1.0,0.5]"));
	}
}
