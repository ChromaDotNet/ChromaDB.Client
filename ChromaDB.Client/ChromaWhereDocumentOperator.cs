namespace ChromaDB.Client;

/// <summary>
/// A filter on the documents of the records, sent as <c>where_document</c>.
/// The <c>&amp;</c> and <c>|</c> operators combine filters with <c>$and</c> and <c>$or</c>.
/// </summary>
public abstract class ChromaWhereDocumentOperator
{
	/// <summary>
	/// The Chroma operator of the filter, like <c>$contains</c> or <c>$and</c>.
	/// </summary>
	protected string Operator { get; }

	/// <summary>
	/// Creates a filter with the given Chroma operator.
	/// </summary>
	/// <param name="operator">The Chroma operator, like <c>$eq</c> or <c>$and</c>.</param>
	protected ChromaWhereDocumentOperator(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhereDocument();

	// The same filter in the where clause of the Search API, on the #document key: {"#document": {"$contains": "..."}}.
	internal abstract Dictionary<string, object> ToSearchWhere();

	/// <summary>
	/// The JSON of the filter, as the client sends it in <c>where_document</c>.
	/// </summary>
	/// <returns>The JSON.</returns>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToWhereDocument(), Common.HttpClientHelpers.TypeInfo<Dictionary<string, object>>(Common.HttpClientHelpers.PostJsonSerializerOptions));

	/// <summary>
	/// The documents that contain the character, with <c>$contains</c>.
	/// </summary>
	/// <param name="value">The text the documents contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator Contains(char value)
		=> Contains(value.ToString());
	/// <summary>
	/// The documents that contain the text, with <c>$contains</c>.
	/// </summary>
	/// <param name="value">The text the documents contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator Contains(string value)
		=> new ChromaWhereDocumentStringOperator("$contains", value);

	/// <summary>
	/// The documents that do not contain the character, with <c>$not_contains</c>.
	/// </summary>
	/// <param name="value">The text the documents do not contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator NotContains(char value)
		=> NotContains(value.ToString());
	/// <summary>
	/// The documents that do not contain the text, with <c>$not_contains</c>.
	/// </summary>
	/// <param name="value">The text the documents do not contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator NotContains(string value)
		=> new ChromaWhereDocumentStringOperator("$not_contains", value);

	/// <summary>
	/// The documents that match the regular expression, with <c>$regex</c>, from Chroma 1.0.12; the earlier versions reject it.
	/// </summary>
	/// <param name="pattern">The regular expression.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator Regex(string pattern)
		=> new ChromaWhereDocumentStringOperator("$regex", pattern);
	/// <summary>
	/// The documents that do not match the regular expression, with <c>$not_regex</c>, from Chroma 1.0.12; the earlier versions reject it.
	/// </summary>
	/// <param name="pattern">The regular expression.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocumentOperator NotRegex(string pattern)
		=> new ChromaWhereDocumentStringOperator("$not_regex", pattern);

	/// <summary>
	/// Always <c>false</c>, so that <c>||</c> combines two filters with <c>$or</c>, like <c>|</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator true(ChromaWhereDocumentOperator _)
		=> false;
	/// <summary>
	/// Always <c>false</c>, so that <c>&amp;&amp;</c> combines two filters with <c>$and</c>, like <c>&amp;</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator false(ChromaWhereDocumentOperator _)
		=> false;

	/// <summary>
	/// The documents that match both filters, with <c>$and</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that both filters pass, with <c>$and</c>.</returns>
	public static ChromaWhereDocumentOperator operator &(ChromaWhereDocumentOperator lhs, ChromaWhereDocumentOperator rhs)
		=> new ChromaWhereDocumentLogicalOperator("$and", lhs, rhs);

	/// <summary>
	/// The documents that match either filter, with <c>$or</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that either filter passes, with <c>$or</c>.</returns>
	public static ChromaWhereDocumentOperator operator |(ChromaWhereDocumentOperator lhs, ChromaWhereDocumentOperator rhs)
		=> new ChromaWhereDocumentLogicalOperator("$or", lhs, rhs);
}

internal class ChromaWhereDocumentLogicalOperator : ChromaWhereDocumentOperator
{
	protected ChromaWhereDocumentOperator Lhs { get; }
	protected ChromaWhereDocumentOperator Rhs { get; }

	internal ChromaWhereDocumentLogicalOperator(string @operator, ChromaWhereDocumentOperator lhs, ChromaWhereDocumentOperator rhs)
		: base(@operator)
	{
		Lhs = lhs;
		Rhs = rhs;
	}

	internal override Dictionary<string, object> ToWhereDocument()
		=> new()
		{
			{ Operator, new object[] { Lhs.ToWhereDocument(), Rhs.ToWhereDocument() } }
		};

	internal override Dictionary<string, object> ToSearchWhere()
		=> new()
		{
			{ Operator, new object[] { Lhs.ToSearchWhere(), Rhs.ToSearchWhere() } }
		};
}

internal class ChromaWhereDocumentStringOperator : ChromaWhereDocumentOperator
{
	protected string String { get; }

	internal ChromaWhereDocumentStringOperator(string @operator, string @string)
		: base(@operator)
	{
		String = @string;
	}

	internal override Dictionary<string, object> ToWhereDocument()
		=> new()
		{
			{ Operator, String }
		};

	internal override Dictionary<string, object> ToSearchWhere()
		=> new()
		{
			{ ChromaSearchKeys.Document, new Dictionary<string, object> { { Operator, String } } }
		};
}
