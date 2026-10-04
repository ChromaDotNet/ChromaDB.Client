namespace ChromaDB.Client;

/// <summary>
/// A filter on the metadata of the records, with the same Chroma operators as <c>ChromaWhereOperator</c>.
/// The methods of the client take <c>ChromaWhereOperator</c>, not this type.
/// </summary>
public abstract class ChromaWhere
{
	/// <summary>
	/// The Chroma operator of the filter, like <c>$eq</c> or <c>$and</c>.
	/// </summary>
	protected string Operator { get; }

	/// <summary>
	/// Creates a filter with the given Chroma operator.
	/// </summary>
	protected ChromaWhere(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhere();

	/// <summary>
	/// The records whose value for the field is one of the values, with <c>$in</c>.
	/// </summary>
	public static ChromaWhere In(string field, params object[] values)
		=> new ChromaWhereValue<object>(field, "$in", values);

	/// <summary>
	/// The records whose value for the field is not one of the values, with <c>$nin</c>.
	/// </summary>
	public static ChromaWhere NotIn(string field, params object[] values)
		=> new ChromaWhereValue<object>(field, "$nin", values);

	/// <summary>
	/// The records whose value for the field is greater than the value, with <c>$gt</c>.
	/// </summary>
	public static ChromaWhere GreaterThan(string field, object value)
		=> new ChromaWhereValue<object>(field, "$gt", value);

	/// <summary>
	/// The records whose value for the field is greater than or equal to the value, with <c>$gte</c>.
	/// </summary>
	public static ChromaWhere GreaterThanOrEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$gte", value);

	/// <summary>
	/// The records whose value for the field is less than the value, with <c>$lt</c>.
	/// </summary>
	public static ChromaWhere LessThan(string field, object value)
		=> new ChromaWhereValue<object>(field, "$lt", value);

	/// <summary>
	/// The records whose value for the field is less than or equal to the value, with <c>$lte</c>.
	/// </summary>
	public static ChromaWhere LessThanOrEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$lte", value);

	/// <summary>
	/// The records whose value for the field equals the value, with <c>$eq</c>.
	/// </summary>
	public static ChromaWhere Equal(string field, object value)
		=> new ChromaWhereValue<object>(field, "$eq", value);

	/// <summary>
	/// The records whose value for the field does not equal the value, with <c>$ne</c>.
	/// </summary>
	public static ChromaWhere NotEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$ne", value);

	/// <summary>
	/// Always <c>false</c>, so that <c>||</c> combines two filters with <c>$or</c>, like <c>|</c>.
	/// </summary>
	public static bool operator true(ChromaWhere _)
		=> false;
	/// <summary>
	/// Always <c>false</c>, so that <c>&amp;&amp;</c> combines two filters with <c>$and</c>, like <c>&amp;</c>.
	/// </summary>
	public static bool operator false(ChromaWhere _)
		=> false;

	/// <summary>
	/// The records that match both filters, with <c>$and</c>.
	/// </summary>
	public static ChromaWhere operator &(ChromaWhere lhs, ChromaWhere rhs)
		=> new ChromaWhereLogical("$and", lhs, rhs);

	/// <summary>
	/// The records that match either filter, with <c>$or</c>.
	/// </summary>
	public static ChromaWhere operator |(ChromaWhere lhs, ChromaWhere rhs)
		=> new ChromaWhereLogical("$or", lhs, rhs);
}

internal class ChromaWhereLogical : ChromaWhere
{
	protected ChromaWhere Lhs { get; }
	protected ChromaWhere Rhs { get; }

	internal ChromaWhereLogical(string @operator, ChromaWhere lhs, ChromaWhere rhs)
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

internal class ChromaWhereValue<T> : ChromaWhere
{
	protected string Field { get; }
	protected T Value { get; }

	internal ChromaWhereValue(string field, string @operator, T value)
		: base(@operator)
	{
		Field = field;
		Value = value;
	}

	internal override Dictionary<string, object> ToWhere()
		=> new()
		{
			{ Field, new Dictionary<string, T> { { Operator, Value } } }
		};
}
