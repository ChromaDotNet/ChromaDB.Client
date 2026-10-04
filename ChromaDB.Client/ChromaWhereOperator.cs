namespace ChromaDB.Client;

/// <summary>
/// A filter on the metadata of the records, sent as <c>where</c>.
/// The <c>&amp;</c> and <c>|</c> operators combine filters with <c>$and</c> and <c>$or</c>.
/// </summary>
public abstract class ChromaWhereOperator
{
	/// <summary>
	/// The Chroma operator of the filter, like <c>$eq</c> or <c>$and</c>.
	/// </summary>
	protected string Operator { get; }

	/// <summary>
	/// Creates a filter with the given Chroma operator.
	/// </summary>
	protected ChromaWhereOperator(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhere();

	/// <summary>
	/// The records whose value for the key is one of the values, with <c>$in</c>.
	/// Every tested Chroma rejects <c>$in</c> and <c>$nin</c> without values, so the client rejects them before the request, with an <c>ArgumentException</c>.
	/// </summary>
	public static ChromaWhereOperator In(string key, params object[] values)
		=> values is { Length: > 0 } ? new ChromaWhereValueOperator(key, "$in", values) : throw new ArgumentException("In needs at least one value: Chroma rejects $in without values.", nameof(values));

	/// <summary>
	/// The records whose value for the key is not one of the values, with <c>$nin</c>.
	/// Without values it throws an <c>ArgumentException</c>: every tested Chroma rejects <c>$nin</c> without values.
	/// </summary>
	public static ChromaWhereOperator NotIn(string key, params object[] values)
		=> values is { Length: > 0 } ? new ChromaWhereValueOperator(key, "$nin", values) : throw new ArgumentException("NotIn needs at least one value: Chroma rejects $nin without values.", nameof(values));

	/// <summary>
	/// The JSON of the filter, as the client sends it in <c>where</c>.
	/// </summary>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToWhere(), Common.HttpClientHelpers.TypeInfo<Dictionary<string, object>>(Common.HttpClientHelpers.PostJsonSerializerOptions));

	/// <summary>
	/// The records whose value for the key is greater than the value, with <c>$gt</c>.
	/// </summary>
	public static ChromaWhereOperator GreaterThan(string key, object value)
		=> new ChromaWhereValueOperator(key, "$gt", value);

	/// <summary>
	/// The records whose value for the key is greater than or equal to the value, with <c>$gte</c>.
	/// </summary>
	public static ChromaWhereOperator GreaterThanOrEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$gte", value);

	/// <summary>
	/// The records whose value for the key is less than the value, with <c>$lt</c>.
	/// </summary>
	public static ChromaWhereOperator LessThan(string key, object value)
		=> new ChromaWhereValueOperator(key, "$lt", value);

	/// <summary>
	/// The records whose value for the key is less than or equal to the value, with <c>$lte</c>.
	/// </summary>
	public static ChromaWhereOperator LessThanOrEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$lte", value);

	/// <summary>
	/// The records whose value for the key equals the value, with <c>$eq</c>.
	/// </summary>
	public static ChromaWhereOperator Equal(string key, object value)
		=> new ChromaWhereValueOperator(key, "$eq", value);

	/// <summary>
	/// The records whose value for the key does not equal the value, with <c>$ne</c>.
	/// </summary>
	public static ChromaWhereOperator NotEqual(string key, object value)
		=> new ChromaWhereValueOperator(key, "$ne", value);

	/// <summary>
	/// The records whose list in the metadata contains the value, with <c>$contains</c>: Chroma 1.5.0 and later.
	/// </summary>
	public static ChromaWhereOperator Contains(string key, object value)
		=> new ChromaWhereValueOperator(key, "$contains", value);

	/// <summary>
	/// The records whose list in the metadata does not contain the value, with <c>$not_contains</c>: Chroma 1.5.0 and later.
	/// </summary>
	public static ChromaWhereOperator NotContains(string key, object value)
		=> new ChromaWhereValueOperator(key, "$not_contains", value);

	/// <summary>
	/// Always <c>false</c>, so that <c>||</c> combines two filters with <c>$or</c>, like <c>|</c>.
	/// </summary>
	public static bool operator true(ChromaWhereOperator _)
		=> false;
	/// <summary>
	/// Always <c>false</c>, so that <c>&amp;&amp;</c> combines two filters with <c>$and</c>, like <c>&amp;</c>.
	/// </summary>
	public static bool operator false(ChromaWhereOperator _)
		=> false;

	/// <summary>
	/// The records that match both filters, with <c>$and</c>.
	/// </summary>
	public static ChromaWhereOperator operator &(ChromaWhereOperator lhs, ChromaWhereOperator rhs)
		=> new ChromaWhereLogicalOperator("$and", lhs, rhs);

	/// <summary>
	/// The records that match either filter, with <c>$or</c>.
	/// </summary>
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
