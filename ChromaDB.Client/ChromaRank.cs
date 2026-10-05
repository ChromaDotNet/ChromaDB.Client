namespace ChromaDB.Client;

/// <summary>
/// An expression that ranks the records of a search, <c>rank</c> in the Search API of Chroma: the records with the lowest score
/// come first. The operators <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c> and the unary <c>-</c> combine expressions as the Python client of
/// Chroma does, and a number converts to <c>Value</c>. <c>ToString()</c> gives the JSON.
/// </summary>
public abstract class ChromaRank
{
	private protected ChromaRank()
	{ }

	internal Dictionary<string, object> ToRank()
		=> ToRank(null);

	// embedText gives the sparse vector of a text query on a key; without it, the text goes as it is.
	internal abstract Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText);

	/// <summary>
	/// The JSON of the expression, as the client sends it in <c>rank</c>. A text query of <c>SparseKnn</c> is in it as the text:
	/// <c>ChromaCollectionClient.SearchAsync</c> sends its sparse vector instead.
	/// </summary>
	/// <returns>The JSON.</returns>
	public override string ToString()
		=> System.Text.Json.JsonSerializer.Serialize(ToRank(), Common.HttpClientHelpers.TypeInfo<Dictionary<string, object>>(Common.HttpClientHelpers.PostJsonSerializerOptions));

	/// <summary>
	/// The distance of each record from the query, with <c>$knn</c>, among the <c>limit</c> nearest records of the key, <c>#embedding</c>
	/// by default. The other records get <c>defaultScore</c>, or are left out when it is null. With <c>returnRank</c> the score is the
	/// position of the record, 0 for the nearest, as <c>Rrf</c> needs.
	/// </summary>
	/// <param name="query">The query embedding.</param>
	/// <param name="key">The key of the index: <c>#embedding</c> for the embeddings, or a metadata key with a sparse vector index.</param>
	/// <param name="limit">How many nearest records the expression ranks.</param>
	/// <param name="defaultScore">The score of the records beyond the limit, or null to leave them out.</param>
	/// <param name="returnRank">Whether the score is the position of the record, 0 for the nearest, as <c>Rrf</c> needs.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Knn(ReadOnlyMemory<float> query, string key = ChromaSearchKeys.Embedding, int limit = 16, double? defaultScore = null, bool returnRank = false)
		=> new ChromaKnnRank(query.ToArray(), key, limit, defaultScore, returnRank);

	/// <summary>
	/// The same as <c>Knn</c> with a sparse vector as the query, on a metadata key that has a sparse vector index, like the BM25 vectors
	/// of a text: <c>$knn</c> with the sparse vector in <c>query</c>. A name of its own, so that <c>Knn(new(...), key)</c> stays unambiguous.
	/// </summary>
	/// <param name="query">The query: a sparse vector, or a text that <c>ChromaCollectionClient.SearchAsync</c> turns into one with the function of the index.</param>
	/// <param name="key">The key of the index: <c>#embedding</c> for the embeddings, or a metadata key with a sparse vector index.</param>
	/// <param name="limit">How many nearest records the expression ranks.</param>
	/// <param name="defaultScore">The score of the records beyond the limit, or null to leave them out.</param>
	/// <param name="returnRank">Whether the score is the position of the record, 0 for the nearest, as <c>Rrf</c> needs.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank SparseKnn(Models.ChromaSparseVector query, string key, int limit = 16, double? defaultScore = null, bool returnRank = false)
		=> new ChromaKnnRank(query, key, limit, defaultScore, returnRank);

	/// <summary>
	/// The same as <c>SparseKnn</c> with the sparse vector of a text, which <c>ChromaCollectionClient.SearchAsync</c> computes with the function
	/// of the sparse vector index of the key, <c>chroma_bm25</c>, as the Python client of Chroma does. The collection of the client needs
	/// its schema, as <c>GetCollectionAsync</c> and <c>CreateCollectionAsync</c> return it.
	/// </summary>
	/// <param name="query">The query: a sparse vector, or a text that <c>ChromaCollectionClient.SearchAsync</c> turns into one with the function of the index.</param>
	/// <param name="key">The key of the index: <c>#embedding</c> for the embeddings, or a metadata key with a sparse vector index.</param>
	/// <param name="limit">How many nearest records the expression ranks.</param>
	/// <param name="defaultScore">The score of the records beyond the limit, or null to leave them out.</param>
	/// <param name="returnRank">Whether the score is the position of the record, 0 for the nearest, as <c>Rrf</c> needs.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank SparseKnn(string query, string key, int limit = 16, double? defaultScore = null, bool returnRank = false)
		=> new ChromaKnnRank(query, key, limit, defaultScore, returnRank);

	/// <summary>
	/// A constant, with <c>$val</c>.
	/// </summary>
	/// <param name="value">The constant.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Value(double value)
		=> new ChromaValueRank(value);

	/// <summary>
	/// A number as a constant, with <c>$val</c>.
	/// </summary>
	/// <param name="value">The constant.</param>
	public static implicit operator ChromaRank(double value)
		=> Value(value);

	/// <summary>
	/// The sum of the expressions, with <c>$sum</c>.
	/// </summary>
	/// <param name="ranks">The expressions.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Sum(params ChromaRank[] ranks)
		=> new ChromaListRank("$sum", ranks);

	/// <summary>
	/// The product of the expressions, with <c>$mul</c>.
	/// </summary>
	/// <param name="ranks">The expressions.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Multiply(params ChromaRank[] ranks)
		=> new ChromaListRank("$mul", ranks);

	/// <summary>
	/// The largest of the expressions, with <c>$max</c>.
	/// </summary>
	/// <param name="ranks">The expressions.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Max(params ChromaRank[] ranks)
		=> new ChromaListRank("$max", ranks);

	/// <summary>
	/// The smallest of the expressions, with <c>$min</c>.
	/// </summary>
	/// <param name="ranks">The expressions.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Min(params ChromaRank[] ranks)
		=> new ChromaListRank("$min", ranks);

	/// <summary>
	/// The absolute value, with <c>$abs</c>.
	/// </summary>
	/// <param name="rank">The expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Abs(ChromaRank rank)
		=> new ChromaUnaryRank("$abs", rank);

	/// <summary>
	/// The exponential, with <c>$exp</c>.
	/// </summary>
	/// <param name="rank">The expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Exp(ChromaRank rank)
		=> new ChromaUnaryRank("$exp", rank);

	/// <summary>
	/// The natural logarithm, with <c>$log</c>.
	/// </summary>
	/// <param name="rank">The expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Log(ChromaRank rank)
		=> new ChromaUnaryRank("$log", rank);

	/// <summary>
	/// Reciprocal rank fusion of the expressions, built as the Python client of Chroma builds it: <c>-(w1 / (k + rank1) + w2 / (k + rank2) + ...)</c>.
	/// The expressions are usually <c>Knn</c> with <c>returnRank</c>. The weights are 1 by default, must be as many as the expressions and
	/// not negative; with <c>normalize</c> they are scaled to sum to 1. <c>k</c> must be positive.
	/// </summary>
	/// <param name="ranks">The expressions to fuse, usually <c>Knn</c> with <c>returnRank</c>.</param>
	/// <param name="k">The constant added to each rank; positive.</param>
	/// <param name="weights">The weight of each expression, not negative, or null for 1 each.</param>
	/// <param name="normalize">Whether the weights are scaled to sum to 1.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank Rrf(IReadOnlyList<ChromaRank> ranks, double k = 60, IReadOnlyList<double>? weights = null, bool normalize = false)
	{
		if (ranks is not { Count: > 0 })
		{
			throw new ArgumentException("RRF needs at least one rank.", nameof(ranks));
		}
		if (k <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(k), "The k of RRF must be positive.");
		}
		if (weights is not null && weights.Count != ranks.Count)
		{
			throw new ArgumentException("RRF needs one weight for each rank.", nameof(weights));
		}
		if (weights is not null && weights.Any(weight => weight < 0))
		{
			throw new ArgumentException("The weights of RRF cannot be negative.", nameof(weights));
		}
		var actual = weights?.ToList() ?? ranks.Select(_ => 1.0).ToList();
		if (normalize)
		{
			var total = actual.Sum();
			if (total == 0)
			{
				throw new ArgumentException("The weights of RRF must have a positive sum to be normalized.", nameof(weights));
			}
			actual = actual.Select(weight => weight / total).ToList();
		}
		var sum = ranks.Select((rank, i) => Value(actual[i]) / (Value(k) + rank)).Aggregate((left, right) => left + right);
		return -sum;
	}

	/// <summary>
	/// The sum, with <c>$sum</c>; sums are flattened into one, as in the Python client of Chroma.
	/// </summary>
	/// <param name="left">The left expression.</param>
	/// <param name="right">The right expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank operator +(ChromaRank left, ChromaRank right)
		=> new ChromaListRank("$sum", Flatten("$sum", left).Concat(Flatten("$sum", right)).ToArray());

	/// <summary>
	/// The difference, with <c>$sub</c>.
	/// </summary>
	/// <param name="left">The left expression.</param>
	/// <param name="right">The right expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank operator -(ChromaRank left, ChromaRank right)
		=> new ChromaBinaryRank("$sub", left, right);

	/// <summary>
	/// The product, with <c>$mul</c>; products are flattened into one, as in the Python client of Chroma.
	/// </summary>
	/// <param name="left">The left expression.</param>
	/// <param name="right">The right expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank operator *(ChromaRank left, ChromaRank right)
		=> new ChromaListRank("$mul", Flatten("$mul", left).Concat(Flatten("$mul", right)).ToArray());

	/// <summary>
	/// The quotient, with <c>$div</c>.
	/// </summary>
	/// <param name="left">The left expression.</param>
	/// <param name="right">The right expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank operator /(ChromaRank left, ChromaRank right)
		=> new ChromaBinaryRank("$div", left, right);

	/// <summary>
	/// The opposite, as the product with <c>-1</c>.
	/// </summary>
	/// <param name="rank">The expression.</param>
	/// <returns>The expression.</returns>
	public static ChromaRank operator -(ChromaRank rank)
		=> new ChromaListRank("$mul", [Value(-1), rank]);

	private static IEnumerable<ChromaRank> Flatten(string @operator, ChromaRank rank)
		=> rank is ChromaListRank list && list.Operator == @operator ? list.Ranks : [rank];
}

// The query is a float[], a ChromaSparseVector or a text.
internal sealed class ChromaKnnRank(object query, string key, int limit, double? defaultScore, bool returnRank) : ChromaRank
{
	// Like the Python client of Chroma: "default" and "return_rank" only when set.
	internal override Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText)
	{
		var vector = query is string text && embedText is not null ? embedText(key, text) : query;
		var knn = new Dictionary<string, object> { ["query"] = vector, ["key"] = key, ["limit"] = limit };
		if (defaultScore is { } score)
		{
			knn["default"] = score;
		}
		if (returnRank)
		{
			knn["return_rank"] = true;
		}
		return new() { ["$knn"] = knn };
	}
}

internal sealed class ChromaValueRank(double value) : ChromaRank
{
	internal override Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText)
		=> new() { ["$val"] = value };
}

internal sealed class ChromaListRank(string @operator, ChromaRank[] ranks) : ChromaRank
{
	public string Operator { get; } = @operator;
	public ChromaRank[] Ranks { get; } = ranks;

	internal override Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText)
		=> new() { [Operator] = Ranks.Select(rank => (object)rank.ToRank(embedText)).ToArray() };
}

internal sealed class ChromaUnaryRank(string @operator, ChromaRank rank) : ChromaRank
{
	internal override Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText)
		=> new() { [@operator] = rank.ToRank(embedText) };
}

internal sealed class ChromaBinaryRank(string @operator, ChromaRank left, ChromaRank right) : ChromaRank
{
	internal override Dictionary<string, object> ToRank(Func<string, string, Models.ChromaSparseVector>? embedText)
		=> new() { [@operator] = new Dictionary<string, object> { ["left"] = left.ToRank(embedText), ["right"] = right.ToRank(embedText) } };
}
