using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class CollectionDeleteRequest
{
	// Null to delete by the filters only.
	[JsonPropertyName("ids")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? Ids { get; init; }

	[JsonPropertyName("where")]
	public Dictionary<string, object>? Where { get; init; }

	[JsonPropertyName("where_document")]
	public Dictionary<string, object>? WhereDocument { get; init; }

	// Sent only when set: the earlier servers do not know it.
	[JsonPropertyName("limit")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Limit { get; init; }
}
