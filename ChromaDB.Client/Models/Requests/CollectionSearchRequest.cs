using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

// The request of the Search API, as the Python client of Chroma sends it: "filter" is the where clause itself, not the
// {"where_clause": ...} of the OpenAPI description of Chroma 1.5.9, which Chroma Cloud rejects with 422.
internal class CollectionSearchRequest
{
	[JsonPropertyName("searches")]
	public required List<CollectionSearchPayload> Searches { get; init; }

	// Sent only when set.
	[JsonPropertyName("read_level")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ReadLevel { get; init; }
}

internal class CollectionSearchPayload
{
	[JsonPropertyName("filter")]
	public Dictionary<string, object>? Filter { get; init; }

	[JsonPropertyName("rank")]
	public Dictionary<string, object>? Rank { get; init; }

	// {} when there is no grouping, as in the Python client.
	[JsonPropertyName("group_by")]
	public required Dictionary<string, object> GroupBy { get; init; }

	[JsonPropertyName("limit")]
	public required CollectionSearchLimit Limit { get; init; }

	[JsonPropertyName("select")]
	public required CollectionSearchSelect Select { get; init; }
}

internal class CollectionSearchLimit
{
	[JsonPropertyName("offset")]
	public int Offset { get; init; }

	[JsonPropertyName("limit")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Limit { get; init; }
}

internal class CollectionSearchSelect
{
	[JsonPropertyName("keys")]
	public required List<string> Keys { get; init; }
}
