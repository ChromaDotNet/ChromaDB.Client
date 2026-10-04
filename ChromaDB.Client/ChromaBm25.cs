using System.Text.Json;
using ChromaDB.Client.Common;
using ChromaDB.Client.Models;

namespace ChromaDB.Client;

/// <summary>
/// The BM25 function of Chroma, <c>chroma_bm25</c>: the sparse vector of a text, computed as the Python client of Chroma 1.5.9 computes it,
/// with the Snowball English stemmer of snowballstemmer 3.1.1, so that the vectors of .NET and of Python match. The same function gives the
/// vectors of the documents and of the queries; with <c>bm25</c> in the sparse vector index, the server applies the inverse document frequency.
/// </summary>
public sealed class ChromaBm25
{
	private readonly HashSet<string> _stopwords;
	private readonly IReadOnlyList<string>? _customStopwords;

	/// <summary>
	/// The English stopwords that the function drops by default, the list of the Python client of Chroma.
	/// </summary>
	public static IReadOnlyList<string> DefaultStopwords { get; } = Array.AsReadOnly(new[]
	{
		"a", "about", "above", "after", "again", "against", "ain", "all", "am", "an", "and", "any", "are", "aren", "aren't", "as", "at",
		"be", "because", "been", "before", "being", "below", "between", "both", "but", "by", "can", "couldn", "couldn't", "d", "did", "didn",
		"didn't", "do", "does", "doesn", "doesn't", "doing", "don", "don't", "down", "during", "each", "few", "for", "from", "further", "had",
		"hadn", "hadn't", "has", "hasn", "hasn't", "have", "haven", "haven't", "having", "he", "her", "here", "hers", "herself", "him",
		"himself", "his", "how", "i", "if", "in", "into", "is", "isn", "isn't", "it", "it's", "its", "itself", "just", "ll", "m", "ma", "me",
		"mightn", "mightn't", "more", "most", "mustn", "mustn't", "my", "myself", "needn", "needn't", "no", "nor", "not", "now", "o", "of", "off",
		"on", "once", "only", "or", "other", "our", "ours", "ourselves", "out", "over", "own", "re", "s", "same", "shan", "shan't", "she",
		"she's", "should", "should've", "shouldn", "shouldn't", "so", "some", "such", "t", "than", "that", "that'll", "the", "their", "theirs",
		"them", "themselves", "then", "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up", "ve", "very",
		"was", "wasn", "wasn't", "we", "were", "weren", "weren't", "what", "when", "where", "which", "while", "who", "whom", "why", "will",
		"with", "won", "won't", "wouldn", "wouldn't", "y", "you", "you'd", "you'll", "you're", "you've", "your", "yours", "yourself", "yourselves",
	});

	/// <summary>
	/// The function with the settings of the Python client of Chroma, which are its defaults: <c>k</c> 1.2, <c>b</c> 0.75,
	/// <c>avgDocLength</c> 256, <c>tokenMaxLength</c> 40 and <c>DefaultStopwords</c>. With <c>includeTokens</c> the vectors also hold
	/// their tokens. A collection whose schema declares other settings needs the same ones here: <c>Bm25Function</c> of its
	/// <c>ChromaCollection.SparseVectorIndexes</c> has them.
	/// </summary>
	public ChromaBm25(double k = 1.2, double b = 0.75, double avgDocLength = 256, int tokenMaxLength = 40, IEnumerable<string>? stopwords = null, bool includeTokens = false)
	{
		K = k;
		B = b;
		AvgDocLength = avgDocLength;
		TokenMaxLength = tokenMaxLength;
		IncludeTokens = includeTokens;
		_customStopwords = stopwords is null ? null : Array.AsReadOnly(stopwords.ToArray());
		_stopwords = new HashSet<string>((_customStopwords ?? DefaultStopwords).Select(Bm25Tokenizer.PythonLower), StringComparer.Ordinal);
	}

	internal const string Name = "chroma_bm25";

	// build_from_config of the Python client: the settings missing from the config get their default values, and null stopwords are the
	// default ones. Null when the config is not an object or a setting has the wrong type, where the Python client keeps no function.
	internal static ChromaBm25? FromConfig(JsonElement? config)
	{
		if (config is not { ValueKind: JsonValueKind.Object } settings)
		{
			return null;
		}
		double? Number(string name, double defaultValue)
			=> !settings.TryGetProperty(name, out var value) ? defaultValue
				: value.ValueKind == JsonValueKind.Number ? value.GetDouble()
				: null;
		if (Number("k", 1.2) is not { } k || Number("b", 0.75) is not { } b || Number("avg_doc_length", 256) is not { } avgDocLength
			|| Number("token_max_length", 40) is not { } tokenMaxLength || tokenMaxLength < int.MinValue || tokenMaxLength >= (double)int.MaxValue + 1)
		{
			return null;
		}
		var includeTokens = false;
		if (settings.TryGetProperty("include_tokens", out var include))
		{
			switch (include.ValueKind)
			{
				case JsonValueKind.True:
					includeTokens = true;
					break;
				case JsonValueKind.False or JsonValueKind.Null:
					break;
				default:
					return null;
			}
		}
		List<string>? stopwords = null;
		if (settings.TryGetProperty("stopwords", out var list) && list.ValueKind != JsonValueKind.Null)
		{
			if (list.ValueKind != JsonValueKind.Array || list.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
			{
				return null;
			}
			stopwords = list.EnumerateArray().Select(x => x.GetString()!).ToList();
		}
		// int() of Python truncates toward zero.
		return new ChromaBm25(k, b, avgDocLength, (int)Math.Truncate(tokenMaxLength), stopwords, includeTokens);
	}

	/// <summary>
	/// The saturation of the term frequency.
	/// </summary>
	public double K { get; }

	/// <summary>
	/// How much the length of the text weighs.
	/// </summary>
	public double B { get; }

	/// <summary>
	/// The length of an average text, in tokens.
	/// </summary>
	public double AvgDocLength { get; }

	/// <summary>
	/// The longest token kept, in characters; the longer ones are dropped.
	/// </summary>
	public int TokenMaxLength { get; }

	/// <summary>
	/// Whether the vectors hold their tokens.
	/// </summary>
	public bool IncludeTokens { get; }

	/// <summary>
	/// The stopwords given to the constructor, or null with <c>DefaultStopwords</c>.
	/// </summary>
	public IReadOnlyList<string>? Stopwords => _customStopwords;

	/// <summary>
	/// The function as a schema declares it, <c>chroma_bm25</c> with these settings, for
	/// <c>ChromaCollectionSchema.WithSparseVectorIndex</c>; the stopwords are in it only when they are not the default ones.
	/// </summary>
	public ChromaEmbeddingFunctionReference Reference
	{
		get
		{
			var config = new Dictionary<string, object>
			{
				["k"] = K,
				["b"] = B,
				["avg_doc_length"] = AvgDocLength,
				["token_max_length"] = TokenMaxLength,
				["include_tokens"] = IncludeTokens,
			};
			if (_customStopwords is not null)
			{
				config["stopwords"] = _customStopwords.ToArray();
			}
			return ChromaEmbeddingFunctionReference.Known(Name, config);
		}
	}

	/// <summary>
	/// The sparse vector of the text, for a document or a query: one index for each distinct token, the absolute value of the
	/// MurmurHash3 of the token, in ascending order, and its BM25 weight. An empty vector when no token is left.
	/// </summary>
	public ChromaSparseVector Embed(string text)
	{
		var tokens = new Bm25Tokenizer(new SnowballEnglishStemmer(), _stopwords, TokenMaxLength).Tokenize(text);
		if (tokens.Count == 0)
		{
			return new ChromaSparseVector([], []);
		}
		var length = (double)tokens.Count;
		// Like the Counter of Python: the tokens with the same hash count together, with the first of them as the token.
		var counts = new Dictionary<long, (int Count, string Token)>();
		foreach (var token in tokens)
		{
			var hash = Math.Abs((long)MurmurHash3.Hash32(token));
			counts[hash] = counts.TryGetValue(hash, out var entry) ? (entry.Count + 1, entry.Token) : (1, token);
		}
		var indices = new List<uint>(counts.Count);
		var values = new List<float>(counts.Count);
		var labels = IncludeTokens ? new List<string>(counts.Count) : null;
		foreach (var pair in counts.OrderBy(x => x.Key))
		{
			// The same operations in the same order as the Python client, in double, then the float the vector holds.
			var tf = (double)pair.Value.Count;
			var denominator = tf + K * (1 - B + (B * length) / AvgDocLength);
			var score = tf * (K + 1) / denominator;
			// Up to 2^31, the absolute value of the smallest hash, which the token "1872d942" has.
			indices.Add((uint)pair.Key);
			values.Add((float)score);
			labels?.Add(pair.Value.Token);
		}
		return new ChromaSparseVector(indices, values, labels);
	}

	/// <summary>
	/// The sparse vectors of the texts, in order.
	/// </summary>
	public List<ChromaSparseVector> Embed(IEnumerable<string> texts)
		=> texts.Select(Embed).ToList();
}
