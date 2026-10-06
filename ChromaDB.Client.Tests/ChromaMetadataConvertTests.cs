using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The values that go in metadata, and back as the type of the caller: dates as round-trip text, a DateTimeOffset in UTC.
[TestFixture]
public class ChromaMetadataConvertTests
{
	static readonly DateTimeOffset Opened = new(2026, 10, 4, 12, 30, 0, TimeSpan.FromHours(2));

	[Test]
	public void ScalarsStayAsTheyAre()
	{
		foreach (var value in new object[] { "text", true, 1, 2L, 1.5f, 2.5 })
		{
			Assert.That(ChromaMetadataConvert.ToMetadataValue(value), Is.EqualTo(value));
		}
		Assert.That(ChromaMetadataConvert.ToMetadataValue(null), Is.Null);
	}

	// Equal instants are equal text, as == compares them; a DateTime keeps its Kind.
	[Test]
	public void Dates()
	{
		Assert.That(ChromaMetadataConvert.ToMetadataValue(Opened), Is.EqualTo("2026-10-04T10:30:00.0000000+00:00"));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(Opened.ToUniversalTime()), Is.EqualTo("2026-10-04T10:30:00.0000000+00:00"));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Utc)), Is.EqualTo("2026-10-04T10:30:00.0000000Z"));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Unspecified)), Is.EqualTo("2026-10-04T10:30:00.0000000"));
		foreach (var kind in new[] { DateTimeKind.Utc, DateTimeKind.Unspecified, DateTimeKind.Local })
		{
			var date = new DateTime(2026, 10, 4, 10, 30, 0, kind);
			var read = (DateTime)ChromaMetadataConvert.FromMetadataValue(ChromaMetadataConvert.ToMetadataValue(date), typeof(DateTime))!;
			Assert.That((read, read.Kind), Is.EqualTo((date, kind)));
		}
		var opened = (DateTimeOffset)ChromaMetadataConvert.FromMetadataValue(ChromaMetadataConvert.ToMetadataValue(Opened), typeof(DateTimeOffset))!;
		Assert.That((opened, opened.Offset), Is.EqualTo((Opened, TimeSpan.Zero)));
	}

#if !CHROMA_CLIENT_NETSTANDARD2_0
	[Test]
	public void DateOnlyAsADate()
	{
		var day = new DateOnly(2026, 10, 4);
		Assert.That(ChromaMetadataConvert.ToMetadataValue(day), Is.EqualTo("2026-10-04"));
		Assert.That(ChromaMetadataConvert.FromMetadataValue("2026-10-04", typeof(DateOnly)), Is.EqualTo(day));
		Assert.That(ChromaMetadataConvert.FromMetadataValue(new List<object> { "2026-10-04" }, typeof(DateOnly[])), Is.EqualTo(new[] { day }));
	}
#endif

	// Chroma stores no empty list: it is no value, as null is.
	[Test]
	public void Lists()
	{
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new[] { 1, 2 }), Is.EqualTo(new List<object> { 1, 2 }));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new List<string> { "a" }), Is.EqualTo(new List<object> { "a" }));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new[] { Opened }), Is.EqualTo(new List<object> { "2026-10-04T10:30:00.0000000+00:00" }));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(Array.Empty<int>()), Is.Null);
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new List<string>()), Is.Null);
		// An int and a long are both integers, a float and a double both floating-point numbers.
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new object[] { 1, 2L }), Is.EqualTo(new List<object> { 1, 2L }));
		Assert.That(ChromaMetadataConvert.ToMetadataValue(new object[] { 1.5f, 2.5 }), Is.EqualTo(new List<object> { 1.5f, 2.5 }));
	}

	[Test]
	public void ValuesChromaHasNot()
	{
		Assert.That(() => ChromaMetadataConvert.ToMetadataValue(1.5m), Throws.ArgumentException);
		Assert.That(() => ChromaMetadataConvert.ToMetadataValue(new[] { Guid.Empty }), Throws.ArgumentException);
		Assert.That(() => ChromaMetadataConvert.ToMetadataValue(new List<string?> { "a", null }), Throws.ArgumentException);
		Assert.That(() => ChromaMetadataConvert.ToMetadataValue(new object[] { 1, "x" }), Throws.ArgumentException.With.Message.Contains("one type"));
		Assert.That(() => ChromaMetadataConvert.ToMetadataValue(new object[] { 1.5, true }), Throws.ArgumentException.With.Message.Contains("one type"));
	}

	[TestCase("text", typeof(string), "text")]
	[TestCase(true, typeof(bool), true)]
	[TestCase(2L, typeof(long), 2L)]
	[TestCase(2L, typeof(int), 2)]
	[TestCase(2L, typeof(int?), 2)]
	[TestCase(2L, typeof(double), 2.0)]
	[TestCase(2L, typeof(float), 2f)]
	[TestCase(2.5, typeof(double), 2.5)]
	[TestCase(2.5, typeof(float), 2.5f)]
	public void ReadsScalars(object value, Type type, object expected)
	{
		var read = ChromaMetadataConvert.FromMetadataValue(value, type);
		Assert.That((read, read!.GetType()), Is.EqualTo((expected, expected.GetType())));
	}

	[Test]
	public void ReadsNull()
		=> Assert.That(ChromaMetadataConvert.FromMetadataValue(null, typeof(int?)), Is.Null);

	static IEnumerable<TestCaseData> Collections()
	{
		var date = new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Utc);
		yield return new TestCaseData(new List<object> { "a", "b" }, typeof(string[]), new[] { "a", "b" });
		yield return new TestCaseData(new List<object> { "a" }, typeof(List<string>), new List<string> { "a" });
		yield return new TestCaseData(new List<object> { true }, typeof(bool[]), new[] { true });
		yield return new TestCaseData(new List<object> { 1L }, typeof(List<bool>), null).SetName("ReadsCollections(an integer as a bool)");
		yield return new TestCaseData(new List<object> { 1L, 2L }, typeof(int[]), new[] { 1, 2 });
		yield return new TestCaseData(new List<object> { 1L }, typeof(List<int>), new List<int> { 1 });
		yield return new TestCaseData(new List<object> { 1L }, typeof(long[]), new[] { 1L });
		yield return new TestCaseData(new List<object> { 1.5 }, typeof(List<float>), new List<float> { 1.5f });
		yield return new TestCaseData(new List<object> { 1.5, 2L }, typeof(double[]), new[] { 1.5, 2.0 });
		yield return new TestCaseData(new List<object> { "2026-10-04T10:30:00.0000000Z" }, typeof(List<DateTime>), new List<DateTime> { date });
		yield return new TestCaseData(new List<object> { "2026-10-04T10:30:00.0000000+00:00" }, typeof(DateTimeOffset[]), new[] { new DateTimeOffset(date) });
	}

	[TestCaseSource(nameof(Collections))]
	public void ReadsCollections(List<object> value, Type type, object? expected)
	{
		if (expected is null)
		{
			Assert.That(() => ChromaMetadataConvert.FromMetadataValue(value, type), Throws.InstanceOf<InvalidCastException>());
			return;
		}
		var read = ChromaMetadataConvert.FromMetadataValue(value, type);
		Assert.That((read, read!.GetType()), Is.EqualTo((expected, type)));
	}

	// One exception for any value that does not convert, with the cause inside when there is one.
	[Test]
	public void ValuesThatDoNotConvert()
	{
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(1.5, typeof(int)), Throws.InstanceOf<InvalidCastException>().With.Message.EqualTo("Cannot read the metadata value of type Double as Int32."));
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue("x", typeof(bool)), Throws.InstanceOf<InvalidCastException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(true, typeof(int)), Throws.InstanceOf<InvalidCastException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(long.MaxValue, typeof(int)), Throws.InstanceOf<InvalidCastException>().With.InnerException.InstanceOf<OverflowException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue("not a date", typeof(DateTime)), Throws.InstanceOf<InvalidCastException>().With.InnerException.InstanceOf<FormatException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(new List<object> { 1L }, typeof(int)), Throws.InstanceOf<InvalidCastException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(new List<object> { 1L }, typeof(Guid[])), Throws.InstanceOf<InvalidCastException>());
		Assert.That(() => ChromaMetadataConvert.FromMetadataValue(new List<object?> { 1L, null }, typeof(long[])), Throws.InstanceOf<InvalidCastException>());
	}
}
