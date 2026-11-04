using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

// The get of a conditional transaction: a get, with the read token of the first read of the transaction, null before it.
internal class ConditionalGetRequest : CollectionGetRequest
{
	[JsonPropertyName("read_token")]
	public long? ReadToken { get; init; }
}
