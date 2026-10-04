namespace ChromaDB.Client;

public abstract class ChromaWhereOperator
{
	protected string Operator { get; }

	protected ChromaWhereOperator(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhere();

	// Every tested Chroma rejects $in and $nin without values, so the client rejects them before the request.
	public static ChromaWhereOperator In(string key, params object[] values)
		=> values is { Length: > 0 } ? new ChromaWhereValueOperator(key, "$in", values) : throw new ArgumentException("In needs at least one value: Chroma rejects $in without values.", nameof(values));

	public static ChromaWhereOperator NotIn(string key, params object[] values)
		=> values is { Length: > 0 } ? new ChromaWhereValueOperator(key, "$nin", values) : throw new ArgumentException("NotIn needs at least one value: Chroma rejects $nin without values.", nameof(values));

	// The JSON of the filter, as the client sends it in "where".
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToWhere(), Common.HttpClientHelpers.PostJsonSerializerOptions);

	public static ChromaWhereOperator GreaterThan(string key, object value)
		=> new ChromaWhereValueOperator(key, "$gt", value);

	public static ChromaWhereOperator GreaterThanOrEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$gte", value);

	public static ChromaWhereOperator LessThan(string key, object value)
		=> new ChromaWhereValueOperator(key, "$lt", value);

	public static ChromaWhereOperator LessThanOrEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$lte", value);

	public static ChromaWhereOperator Equal(string key, object value)
		=> new ChromaWhereValueOperator(key, "$eq", value);

	public static ChromaWhereOperator NotEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$ne", value);

	// The records whose list in the metadata contains the value: Chroma 1.5.0 and later.
	public static ChromaWhereOperator Contains(string key, object value)
		=> new ChromaWhereValueOperator(key, "$contains", value);

	// The records whose list in the metadata does not contain the value: Chroma 1.5.0 and later.
	public static ChromaWhereOperator NotContains(string key, object value)
		=> new ChromaWhereValueOperator(key, "$not_contains", value);

	public static bool operator true(ChromaWhereOperator _)
		=> false;
	public static bool operator false(ChromaWhereOperator _)
		=> false;

	public static ChromaWhereOperator operator &(ChromaWhereOperator lhs, ChromaWhereOperator rhs)
		=> new ChromaWhereLogicalOperator("$and", lhs, rhs);

	public static ChromaWhereOperator operator |(ChromaWhereOperator lhs, ChromaWhereOperator rhs)
		=> new ChromaWhereLogicalOperator("$or", lhs, rhs);
}

internal class ChromaWhereLogicalOperator : ChromaWhereOperator
{
	protected ChromaWhereOperator Lhs { get; }
	protected ChromaWhereOperator Rhs { get; }

	internal ChromaWhereLogicalOperator(string @operator, ChromaWhereOperator lhs, ChromaWhereOperator rhs)
		: base(@operator)
	{
		Lhs = lhs;
		Rhs = rhs;
	}

	internal override Dictionary<string, object> ToWhere()
		=> new()
		{
			{ Operator, new object[] { Lhs.ToWhere(), Rhs.ToWhere() } }
		};
}

internal class ChromaWhereValueOperator : ChromaWhereOperator
{
	protected string Key { get; }
	protected object Value { get; }

	internal ChromaWhereValueOperator(string key, string @operator, object value)
		: base(@operator)
	{
		Key = key;
		Value = value;
	}

	internal override Dictionary<string, object> ToWhere()
		=> new()
		{
			{ Key, new Dictionary<string, object> { { Operator, Value } } }
		};
}
