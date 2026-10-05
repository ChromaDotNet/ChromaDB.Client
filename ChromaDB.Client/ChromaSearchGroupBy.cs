namespace ChromaDB.Client;

/// <summary>
/// Groups the results of a search by the values of metadata keys, and keeps in each group the records the aggregate chooses,
/// <c>group_by</c> in the Search API of Chroma.
/// </summary>
public sealed class ChromaSearchGroupBy
{
	private readonly string[] _keys;
	private readonly ChromaSearchAggregate _aggregate;

	/// <summary>
	/// Groups by the metadata keys, like <c>category</c>, and keeps the records the aggregate chooses in each group.
	/// </summary>
	/// <param name="aggregate">The records of each group that are kept.</param>
	/// <param name="keys">The metadata keys whose values make the groups.</param>
	public ChromaSearchGroupBy(ChromaSearchAggregate aggregate, params string[] keys)
	{
		if (keys is not { Length: > 0 })
		{
			throw new ArgumentException("A group by needs at least one key.", nameof(keys));
		}
		_keys = keys;
		_aggregate = aggregate;
	}

	internal Dictionary<string, object> ToGroupBy()
		=> new() { ["keys"] = _keys, ["aggregate"] = _aggregate.ToAggregate() };
}

/// <summary>
/// Which records a group keeps, in <c>ChromaSearchGroupBy</c>.
/// </summary>
public sealed class ChromaSearchAggregate
{
	private readonly string _operator;
	private readonly string[] _keys;
	private readonly int _k;

	private ChromaSearchAggregate(string @operator, int k, string[] keys)
	{
		if (k <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(k), "The k of an aggregate must be positive.");
		}
		if (keys is not { Length: > 0 })
		{
			throw new ArgumentException("An aggregate needs at least one key.", nameof(keys));
		}
		_operator = @operator;
		_k = k;
		_keys = keys;
	}

	/// <summary>
	/// The <c>k</c> records with the lowest values of the keys, compared in order, with <c>$min_k</c>; with <c>ChromaSearchKeys.Score</c>,
	/// the best ranked.
	/// </summary>
	/// <param name="k">How many records each group keeps.</param>
	/// <param name="keys">The keys that order the records of a group, like <c>#score</c>.</param>
	/// <returns>The aggregate.</returns>
	public static ChromaSearchAggregate MinK(int k, params string[] keys)
		=> new("$min_k", k, keys);

	/// <summary>
	/// The <c>k</c> records with the highest values of the keys, compared in order, with <c>$max_k</c>.
	/// </summary>
	/// <param name="k">How many records each group keeps.</param>
	/// <param name="keys">The keys that order the records of a group, like <c>#score</c>.</param>
	/// <returns>The aggregate.</returns>
	public static ChromaSearchAggregate MaxK(int k, params string[] keys)
		=> new("$max_k", k, keys);

	internal Dictionary<string, object> ToAggregate()
		=> new() { [_operator] = new Dictionary<string, object> { ["keys"] = _keys, ["k"] = _k } };
}
