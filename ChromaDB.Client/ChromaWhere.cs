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
	/// <param name="operator">The Chroma operator, like <c>$eq</c> or <c>$and</c>.</param>
	protected ChromaWhere(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhere();

	/// <summary>
	/// The records whose value for the field is one of the values, with <c>$in</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="values">The values.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere In(string field, params object[] values)
		=> new ChromaWhereValue<object>(field, "$in", values);

	/// <summary>
	/// The records whose value for the field is not one of the values, with <c>$nin</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="values">The values.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere NotIn(string field, params object[] values)
		=> new ChromaWhereValue<object>(field, "$nin", values);

	/// <summary>
	/// The records whose value for the field is greater than the value, with <c>$gt</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere GreaterThan(string field, object value)
		=> new ChromaWhereValue<object>(field, "$gt", value);

	/// <summary>
	/// The records whose value for the field is greater than or equal to the value, with <c>$gte</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere GreaterThanOrEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$gte", value);

	/// <summary>
	/// The records whose value for the field is less than the value, with <c>$lt</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere LessThan(string field, object value)
		=> new ChromaWhereValue<object>(field, "$lt", value);

	/// <summary>
	/// The records whose value for the field is less than or equal to the value, with <c>$lte</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere LessThanOrEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$lte", value);

	/// <summary>
	/// The records whose value for the field equals the value, with <c>$eq</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere Equal(string field, object value)
		=> new ChromaWhereValue<object>(field, "$eq", value);

	/// <summary>
	/// The records whose value for the field does not equal the value, with <c>$ne</c>.
	/// </summary>
	/// <param name="field">The metadata key.</param>
	/// <param name="value">The value to compare with.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhere NotEqual(string field, object value)
		=> new ChromaWhereValue<object>(field, "$ne", value);

	/// <summary>
	/// Always <c>false</c>, so that <c>||</c> combines two filters with <c>$or</c>, like <c>|</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator true(ChromaWhere _)
		=> false;
	/// <summary>
	/// Always <c>false</c>, so that <c>&amp;&amp;</c> combines two filters with <c>$and</c>, like <c>&amp;</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator false(ChromaWhere _)
		=> false;

	/// <summary>
	/// The records that match both filters, with <c>$and</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that both filters pass, with <c>$and</c>.</returns>
	public static ChromaWhere operator &(ChromaWhere lhs, ChromaWhere rhs)
		=> new ChromaWhereLogical("$and", lhs, rhs);

	/// <summary>
	/// The records that match either filter, with <c>$or</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that either filter passes, with <c>$or</c>.</returns>
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
