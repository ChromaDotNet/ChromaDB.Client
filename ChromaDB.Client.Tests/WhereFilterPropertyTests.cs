using System.Globalization;
using ChromaDB.Client.Tests.Common;
using FsCheck;
using FsCheck.Fluent;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// Filters made at random from &, |, Not, All, None, the comparisons, In, NotIn and Contains: the client sends the JSON that an oracle
// written here gives, Not(Not(f)) is f, and De Morgan holds.
[TestFixture]
public class WhereFilterPropertyTests
{
	[Test]
	public void TheJsonOfTheOracle()
		=> PropertyChecks.ForAll(Filters, filter => filter.Build().ToString() == Json(Normal(filter, negated: false)), 500);

	[Test]
	public void NotOfNotIsTheFilter()
		=> PropertyChecks.ForAll(Filters, filter =>
		{
			var built = filter.Build();
			return ChromaWhereOperator.Not(ChromaWhereOperator.Not(built)).ToString() == built.ToString();
		}, 500);

	[Test]
	public void DeMorgan()
		=> PropertyChecks.ForAll(Gen.Zip(Filters, Filters), pair =>
		{
			var (a, b) = (pair.Item1.Build(), pair.Item2.Build());
			return ChromaWhereOperator.Not(a & b).ToString() == (ChromaWhereOperator.Not(a) | ChromaWhereOperator.Not(b)).ToString()
				&& ChromaWhereOperator.Not(a | b).ToString() == (ChromaWhereOperator.Not(a) & ChromaWhereOperator.Not(b)).ToString();
		}, 500);

	// The filters, as the tests make them.
	internal abstract record Filter
	{
		public abstract ChromaWhereOperator Build();
	}

	sealed record Compare(string Key, string Operator, object Value) : Filter
	{
		public override ChromaWhereOperator Build() => Operator switch
		{
			"$eq" => ChromaWhereOperator.Equal(Key, Value),
			"$ne" => ChromaWhereOperator.NotEqual(Key, Value),
			"$gt" => ChromaWhereOperator.GreaterThan(Key, Value),
			"$gte" => ChromaWhereOperator.GreaterThanOrEqual(Key, Value),
			"$lt" => ChromaWhereOperator.LessThan(Key, Value),
			"$lte" => ChromaWhereOperator.LessThanOrEqual(Key, Value),
			"$contains" => ChromaWhereOperator.Contains(Key, Value),
			_ => ChromaWhereOperator.NotContains(Key, Value),
		};

		public override string ToString() => $"{Key} {Operator} {Text(Value)}";
	}

	sealed record InList(string Key, bool In, object[] Values) : Filter
	{
		public override ChromaWhereOperator Build() => In ? ChromaWhereOperator.In(Key, Values) : ChromaWhereOperator.NotIn(Key, Values);

		public override string ToString() => $"{Key} {(In ? "$in" : "$nin")} [{string.Join(",", Values.Select(Text))}]";
	}

	sealed record Constant(bool All) : Filter
	{
		public override ChromaWhereOperator Build() => All ? ChromaWhereOperator.All : ChromaWhereOperator.None;

		public override string ToString() => All ? "All" : "None";
	}

	// With Conditional, && or || in place of & or |.
	sealed record Logical(bool And, Filter Left, Filter Right, bool Conditional) : Filter
	{
		public override ChromaWhereOperator Build()
		{
			var (left, right) = (Left.Build(), Right.Build());
			return (And, Conditional) switch
			{
				(true, false) => left & right,
				(true, true) => left && right,
				(false, false) => left | right,
				(false, true) => left || right,
			};
		}

		public override string ToString() => $"({Left} {(And ? "&" : "|")}{(Conditional ? (And ? "&" : "|") : "")} {Right})";
	}

	sealed record Negation(Filter Inner) : Filter
	{
		public override ChromaWhereOperator Build() => ChromaWhereOperator.Not(Inner.Build());

		public override string ToString() => $"Not({Inner})";
	}

	static readonly string[] Comparisons = ["$eq", "$ne", "$gt", "$gte", "$lt", "$lte", "$contains", "$not_contains"];

	static readonly Gen<string> Keys = Gen.Elements("a", "b", "c");

	// Values whose JSON has one way to be written: whole numbers, numbers with a fraction, texts of letters, booleans.
	static readonly Gen<object>[] ValuesOfAKind =
	[
		Gen.Choose(-1000, 1000).Select(x => (object)(long)x),
		Gen.Choose(-400, 400).Select(x => (object)(x + 0.25)),
		Gen.Elements("x", "y", "zz").Select(x => (object)x),
		Gen.Elements(true, false).Select(x => (object)x),
	];

	static readonly Gen<Filter> Leaves = Gen.Frequency(
		(5, from key in Keys from @operator in Gen.Elements(Comparisons) from value in Gen.OneOf(ValuesOfAKind) select (Filter)new Compare(key, @operator, value)),
		(2, from key in Keys
			from @in in Gen.Elements(true, false)
			from values in Gen.Elements(ValuesOfAKind).SelectMany(kind => Gen.Choose(0, 3).SelectMany(count => Gen.ArrayOf(kind, count)))
			select (Filter)new InList(key, @in, values)),
		(1, Gen.Elements(true, false).Select(all => (Filter)new Constant(all))));

	static Gen<Filter> Trees(int depth)
	{
		if (depth == 0)
		{
			return Leaves;
		}
		var inner = Trees(depth - 1);
		return Gen.Frequency(
			(2, Leaves),
			(4, from and in Gen.Elements(true, false)
				from left in inner
				from right in inner
				from conditional in Gen.Elements(false, false, false, true)
				select (Filter)new Logical(and, left, right, conditional)),
			(1, inner.Select(x => (Filter)new Negation(x))));
	}

	static readonly Gen<Filter> Filters = Gen.Sized(size => Trees(Math.Min(5, 1 + size / 20)));

	// The oracle: a filter as Chroma takes it. Chroma has no $not, so a negation goes into the operators and swaps $and and $or; All and
	// None go away inside & and |; a chain of the same operator is one list.
	abstract record Node;

	sealed record ConstantNode(bool All) : Node;

	sealed record LeafNode(string Key, string Operator, string Value) : Node;

	sealed record ListNode(string Operator, IReadOnlyList<Node> Items) : Node;

	static readonly Dictionary<string, string> Negated = new()
	{
		["$eq"] = "$ne", ["$ne"] = "$eq", ["$gt"] = "$lte", ["$lte"] = "$gt", ["$gte"] = "$lt", ["$lt"] = "$gte",
		["$in"] = "$nin", ["$nin"] = "$in", ["$contains"] = "$not_contains", ["$not_contains"] = "$contains",
	};

	static Node Normal(Filter filter, bool negated) => filter switch
	{
		Compare x => new LeafNode(x.Key, negated ? Negated[x.Operator] : x.Operator, Text(x.Value)),
		// In without values matches no record, and NotIn without values every record.
		InList { Values.Length: 0 } x => new ConstantNode(x.In == negated),
		InList x => new LeafNode(x.Key, (x.In != negated) ? "$in" : "$nin", $"[{string.Join(",", x.Values.Select(Text))}]"),
		Constant x => new ConstantNode(x.All != negated),
		Logical x => Join(x.And != negated, Normal(x.Left, negated), Normal(x.Right, negated)),
		Negation x => Normal(x.Inner, !negated),
		_ => throw new ArgumentException(filter.ToString()),
	};

	static Node Join(bool and, Node left, Node right)
	{
		if (left is ConstantNode l)
		{
			return l.All == and ? right : left;
		}
		if (right is ConstantNode r)
		{
			return r.All == and ? left : right;
		}
		var @operator = and ? "$and" : "$or";
		return new ListNode(@operator, [.. Items(left, @operator), .. Items(right, @operator)]);
	}

	static IReadOnlyList<Node> Items(Node node, string @operator) => node is ListNode list && list.Operator == @operator ? list.Items : [node];

	static string Json(Node node) => node switch
	{
		ConstantNode x => x.All ? "true" : "false",
		LeafNode x => $"{{\"{x.Key}\":{{\"{x.Operator}\":{x.Value}}}}}",
		ListNode x => $"{{\"{x.Operator}\":[{string.Join(",", x.Items.Select(Json))}]}}",
		_ => throw new ArgumentException(node.ToString()),
	};

	static string Text(object value) => value switch
	{
		long x => x.ToString(CultureInfo.InvariantCulture),
		double x => x.ToString("R", CultureInfo.InvariantCulture),
		string x => $"\"{x}\"",
		bool x => x ? "true" : "false",
		_ => throw new ArgumentException(value.ToString()),
	};
}
