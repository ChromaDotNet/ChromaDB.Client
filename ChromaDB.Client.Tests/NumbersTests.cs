using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
			Assert.That(ReadBack(ChromaNumbers.Format(value)), Is.EqualTo(value), "2^" + e);
		}
		for (var e = -149; e <= 127; e++)
		{
			var value = (float)Math.Pow(2, e);
			Assert.That(float.Parse(ChromaNumbers.Format(value), CultureInfo.InvariantCulture), Is.EqualTo(value), "2^" + e);
		}
	}

	// "R" writes the shortest digits on .NET Core 3.0 and later, not on .NET Framework.
	static readonly bool RuntimeShortest = Environment.Version.Major is 3 or >= 5;

	static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

	// A text read back as the client reads the answers: double.Parse of .NET Framework misreads some of the shortest.
	static double ReadBack(string text) => RuntimeShortest ? double.Parse(text, CultureInfo.InvariantCulture) : ChromaNumbers.ParseDouble(Encoding.ASCII.GetBytes(text));

	// The shortest texts that .NET Framework, and System.Text.Json on it, read as the double next to them; the bits
	// are the ones of a parser that rounds right. The client reads them right on every runtime.
	[TestCase("3.16E-322", 0x0000000000000040L)]
	[TestCase("5.021723523791529E-285", 0x04E7E548B948ED8DL)]
	[TestCase("3.915010753235773E-227", 0x10EDADA8022037F4L)]
	[TestCase("9.002841324384607E-274", 0x073F2B7FA8C10637L)]
	[TestCase("2.4703282292062328E-324", 0x0000000000000001L)]
	[TestCase("1.7976931348623157E308", 0x7FEFFFFFFFFFFFFFL)]
	[TestCase("2.2250738585072011E-308", 0x000FFFFFFFFFFFFFL)]
	[TestCase("-0.0", unchecked((long)0x8000000000000000UL))]
	public async Task NumbersInAnswers(string text, long bits)
	{
		Assert.That(BitConverter.DoubleToInt64Bits(ChromaNumbers.ParseDouble(Encoding.ASCII.GetBytes(text))), Is.EqualTo(bits));
		var answer = "{\"ids\":[\"a\"],\"embeddings\":null,\"metadatas\":[{\"v\":" + text + ",\"l\":[" + text + "]}],\"documents\":null,\"uris\":null,\"include\":[\"metadatas\"]}";
		using var http = new HttpClient(new Answer(answer));
		var collection = new ChromaCollectionClient(Guid.NewGuid(), "c", new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false), http);
		var metadata = (await collection.GetAsync(include: ChromaGetInclude.Metadatas)).Single().Metadata!;
		Assert.That(BitConverter.DoubleToInt64Bits((double)metadata["v"]), Is.EqualTo(bits));
		Assert.That(BitConverter.DoubleToInt64Bits((double)((List<object>)metadata["l"])[0]), Is.EqualTo(bits));
	}

	// The text of 80,000 doubles and floats from a generator that gives the same numbers on every runtime: the same on
	// .NET 8, where "R" gives the digits, and on .NET Framework, where they come from the exact value and from doubles.
	[Test]
	public void SameTextOnEveryRuntime()
	{
		ulong state = 20261005;
		ulong Next()
		{
			var z = state += 0x9E3779B97F4A7C15UL;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}
		var text = new StringBuilder();
		for (var i = 0; i < 20000; i++)
		{
			var d = BitConverter.Int64BitsToDouble((long)Next());
			if (!double.IsNaN(d) && !double.IsInfinity(d)) text.Append(ChromaNumbers.Format(d)).Append('\n');
			var f = BitConverter.ToSingle(BitConverter.GetBytes((uint)Next()), 0);
			if (!float.IsNaN(f) && !float.IsInfinity(f)) text.Append(ChromaNumbers.Format(f)).Append('\n');
			text.Append(ChromaNumbers.Format((Next() % 2000001) / 1000.0 - 1000)).Append('\n');
			text.Append(ChromaNumbers.Format((float)((Next() % 2000001) / 997.0))).Append('\n');
		}
		using var sha = SHA256.Create();
		var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
		Assert.That(hash, Is.EqualTo("926F425A9739B9FD95D2E357A4CD0301CDF31678407E31DAC9784A185F95D723"));
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
			if (RuntimeShortest && double.Parse(r, CultureInfo.InvariantCulture) == d)
			{
				ChromaNumbers.FromText(r, out var rDigits, out var rExponent);
				Assert.That((digits, exponent), Is.EqualTo((rDigits, rExponent)), r);
			}
			Assert.That(ReadBack(ChromaNumbers.Format(d)), Is.EqualTo(d));

			var f = Math.Abs(BitConverter.ToSingle(bytes, 0));
			if (float.IsNaN(f) || float.IsInfinity(f) || f == 0) continue;
			ChromaNumbers.ExactDigits(BitConverter.ToInt32(BitConverter.GetBytes(f), 0), 23, 150, out digits, out exponent);
			var fr = f.ToString("R", CultureInfo.InvariantCulture);
			if (RuntimeShortest && float.Parse(fr, CultureInfo.InvariantCulture) == f)
			{
				ChromaNumbers.FromText(fr, out var fDigits, out var fExponent);
				Assert.That((digits, exponent), Is.EqualTo((fDigits, fExponent)), fr);
			}
			Assert.That(float.Parse(ChromaNumbers.Format(f), CultureInfo.InvariantCulture), Is.EqualTo(f));
		}
	}

	// The digits of a float with doubles, the path of .NET Framework, are the exact ones whenever they decide: floats
	// across the whole range, the subnormals and the powers of two.
	[Test]
	public void FloatDigitsAreTheExactOnes()
	{
		var random = new Random(20261005);
		var decided = 0;
		var bits = Enumerable.Range(1, 5000)
			.Concat(Enumerable.Range(0, 254).Select(e => (e + 1) << 23))
			.Concat(Enumerable.Range(0, 200000).Select(_ => random.Next(1, 0x7F800000)))
			.Concat([0x7F7FFFFF, 0x00800000, 0x007FFFFF]);
		foreach (var b in bits)
		{
			if (!ChromaNumbers.FloatDigits(b, out var digits, out var exponent)) continue;
			decided++;
			ChromaNumbers.ExactDigits(b, 23, 150, out var exactDigits, out var exactExponent);
			Assert.That((digits, exponent), Is.EqualTo((exactDigits, exactExponent)), b.ToString("X8", CultureInfo.InvariantCulture));
		}
		Assert.That(decided, Is.GreaterThan(190000));
	}

	// -0.0 in an answer keeps its sign, which .NET Framework drops when it parses it: in embeddings, metadata and
	// sparse vectors.
	[Test]
	public async Task NegativeZeroInAnswers()
	{
		const string answer = """{"ids":["a"],"embeddings":[[-0.0,1.0]],"metadatas":[{"z":-0.0,"s":{"#type":"sparse_vector","indices":[1],"values":[-0.0]}}],"documents":null,"uris":null,"include":["embeddings","metadatas"]}""";
		using var http = new HttpClient(new Answer(answer));
		var options = new ChromaConfigurationOptions("http://localhost:8000").WithBatchSplitting(false);
		var collection = new ChromaCollectionClient(Guid.NewGuid(), "c", options, http);
		var entry = (await collection.GetAsync(include: ChromaGetInclude.Embeddings | ChromaGetInclude.Metadatas)).Single();
		Assert.That(Bits(entry.Embedding!.Value.Span[0]), Is.LessThan(0));
		Assert.That(BitConverter.DoubleToInt64Bits((double)entry.Metadata!["z"]), Is.LessThan(0));
		Assert.That(Bits(((ChromaSparseVector)entry.Metadata["s"]).Values[0]), Is.LessThan(0));
	}

	sealed class Answer(string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
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
