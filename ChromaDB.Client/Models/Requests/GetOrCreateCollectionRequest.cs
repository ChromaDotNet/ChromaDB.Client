using System.Text.Json.Serialization;

namespace ChromaDB.Client.Models.Requests;

internal class GetOrCreateCollectionRequest : GetOrCreateCollectionRequestBase
{
	// The name of the base property: without it, the serialization writes the override as GetOrCreate too.
	[JsonPropertyName("get_or_create")]
	public override bool GetOrCreate { get; } = true;
}
