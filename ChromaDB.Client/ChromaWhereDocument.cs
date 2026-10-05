namespace ChromaDB.Client;

/// <summary>
/// A filter on the documents of the records, with the same Chroma operators as <c>ChromaWhereDocumentOperator</c>.
/// The methods of the client take <c>ChromaWhereDocumentOperator</c>, not this type.
/// </summary>
public abstract class ChromaWhereDocument
{
	/// <summary>
	/// The Chroma operator of the filter, like <c>$contains</c> or <c>$and</c>.
	/// </summary>
	protected string Operator { get; }

	/// <summary>
	/// Creates a filter with the given Chroma operator.
	/// </summary>
	/// <param name="operator">The Chroma operator, like <c>$eq</c> or <c>$and</c>.</param>
	protected ChromaWhereDocument(string @operator)
	{
		Operator = @operator;
	}

	internal abstract Dictionary<string, object> ToWhereDocument();

	/// <summary>
	/// The documents that contain the character, with <c>$contains</c>.
	/// </summary>
	/// <param name="ch">The character the documents contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocument Contains(char ch)
		=> Contains(ch.ToString());
	/// <summary>
	/// The documents that contain the text, with <c>$contains</c>.
	/// </summary>
	/// <param name="str">The text the documents contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocument Contains(string str)
		=> new ChromaWhereDocumentStr("$contains", str);

	/// <summary>
	/// The documents that do not contain the character, with <c>$not_contains</c>.
	/// </summary>
	/// <param name="ch">The character the documents do not contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocument NotContains(char ch)
		=> NotContains(ch.ToString());
	/// <summary>
	/// The documents that do not contain the text, with <c>$not_contains</c>.
	/// </summary>
	/// <param name="str">The text the documents do not contain.</param>
	/// <returns>The filter.</returns>
	public static ChromaWhereDocument NotContains(string str)
		=> new ChromaWhereDocumentStr("$not_contains", str);

	/// <summary>
	/// Always <c>false</c>, so that <c>||</c> combines two filters with <c>$or</c>, like <c>|</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator true(ChromaWhereDocument _)
		=> false;
	/// <summary>
	/// Always <c>false</c>, so that <c>&amp;&amp;</c> combines two filters with <c>$and</c>, like <c>&amp;</c>.
	/// </summary>
	/// <param name="_">The filter.</param>
	/// <returns>Always false, so that <c>&amp;&amp;</c> and <c>||</c> combine both filters.</returns>
	public static bool operator false(ChromaWhereDocument _)
		=> false;

	/// <summary>
	/// The documents that match both filters, with <c>$and</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that both filters pass, with <c>$and</c>.</returns>
	public static ChromaWhereDocument operator &(ChromaWhereDocument lhs, ChromaWhereDocument rhs)
		=> new ChromaWhereDocumentLogical("$and", lhs, rhs);

	/// <summary>
	/// The documents that match either filter, with <c>$or</c>.
	/// </summary>
	/// <param name="lhs">The first filter.</param>
	/// <param name="rhs">The second filter.</param>
	/// <returns>The filter that either filter passes, with <c>$or</c>.</returns>
	public static ChromaWhereDocument operator |(ChromaWhereDocument lhs, ChromaWhereDocument rhs)
		=> new ChromaWhereDocumentLogical("$or", lhs, rhs);
}

internal class ChromaWhereDocumentLogical : ChromaWhereDocument
{
	protected ChromaWhereDocument Lhs { get; }
	protected ChromaWhereDocument Rhs { get; }

	internal ChromaWhereDocumentLogical(string @operator, ChromaWhereDocument lhs, ChromaWhereDocument rhs)
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
}

internal class ChromaWhereDocumentStr : ChromaWhereDocument
{
	protected string Str { get; }

	internal ChromaWhereDocumentStr(string @operator, string str)
		: base(@operator)
	{
		Str = str;
	}

	internal override Dictionary<string, object> ToWhereDocument()
		=> new()
		{
			{ Operator, Str }
		};
}
