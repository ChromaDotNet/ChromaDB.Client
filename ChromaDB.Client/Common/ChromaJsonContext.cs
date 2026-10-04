using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ChromaDB.Client.Models;
using ChromaDB.Client.Models.Requests;
using ChromaDB.Client.Models.Responses;

namespace ChromaDB.Client.Common;

// The metadata of the types the client writes and reads, generated at build time, so that trimming and NativeAOT
// keep what the serialization needs. The values of metadata and filters are objects: their usual types are listed too.
[JsonSerializable(typeof(CollectionAddRequest))]
[JsonSerializable(typeof(CollectionDeleteRequest))]
[JsonSerializable(typeof(CollectionGetRequest))]
[JsonSerializable(typeof(CollectionModifyRequest))]
[JsonSerializable(typeof(CollectionPeekRequest))]
[JsonSerializable(typeof(CollectionQueryRequest))]
[JsonSerializable(typeof(CollectionUpdateRequest))]
[JsonSerializable(typeof(CollectionUpsertRequest))]
[JsonSerializable(typeof(CreateCollectionRequest))]
[JsonSerializable(typeof(CreateDatabaseRequest))]
[JsonSerializable(typeof(CreateTenantRequest))]
[JsonSerializable(typeof(GetOrCreateCollectionRequest))]
[JsonSerializable(typeof(ResetRequest))]
[JsonSerializable(typeof(UpdateTenantRequest))]
[JsonSerializable(typeof(ForkCollectionRequest))]
[JsonSerializable(typeof(AttachFunctionRequest))]
[JsonSerializable(typeof(DetachFunctionRequest))]
[JsonSerializable(typeof(ForkCountResponse))]
[JsonSerializable(typeof(AttachFunctionResponse))]
[JsonSerializable(typeof(GetAttachedFunctionResponse))]
[JsonSerializable(typeof(DetachFunctionResponse))]
[JsonSerializable(typeof(ChromaHealthcheck))]
[JsonSerializable(typeof(ChromaIndexingStatus))]
[JsonSerializable(typeof(CollectionEntriesGetResponse))]
[JsonSerializable(typeof(CollectionEntriesQueryResponse))]
[JsonSerializable(typeof(GeneralError))]
[JsonSerializable(typeof(ChromaCollection))]
[JsonSerializable(typeof(List<ChromaCollection>))]
[JsonSerializable(typeof(ChromaDatabase))]
[JsonSerializable(typeof(List<ChromaDatabase>))]
[JsonSerializable(typeof(ChromaTenant))]
[JsonSerializable(typeof(ChromaHeartbeat))]
[JsonSerializable(typeof(ChromaUserIdentity))]
[JsonSerializable(typeof(ChromaPreFlightChecks))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
// The values of metadata and filters.
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(long[]))]
[JsonSerializable(typeof(short[]))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(sbyte[]))]
[JsonSerializable(typeof(ushort[]))]
[JsonSerializable(typeof(uint[]))]
[JsonSerializable(typeof(ulong[]))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(decimal[]))]
[JsonSerializable(typeof(bool[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<long>))]
[JsonSerializable(typeof(List<short>))]
[JsonSerializable(typeof(List<byte>))]
[JsonSerializable(typeof(List<sbyte>))]
[JsonSerializable(typeof(List<ushort>))]
[JsonSerializable(typeof(List<uint>))]
[JsonSerializable(typeof(List<ulong>))]
[JsonSerializable(typeof(List<double>))]
[JsonSerializable(typeof(List<float>))]
[JsonSerializable(typeof(List<decimal>))]
[JsonSerializable(typeof(List<bool>))]
internal partial class ChromaJsonContext : JsonSerializerContext
{
}

// In a class of its own: the static initializers of the partial ChromaJsonContext run in no given order, so Default
// could still be null there.
internal static class ChromaJsonResolver
{
	// With reflection enabled, as without trimming, the other types of values keep working as before;
	// with trimming or NativeAOT, reflection is off and only the types of ChromaJsonContext are known.
	public static IJsonTypeInfoResolver Instance { get; } = JsonSerializer.IsReflectionEnabledByDefault
		? JsonTypeInfoResolver.Combine(ChromaJsonContext.Default, CreateReflectionResolver())
		: ChromaJsonContext.Default;

#if NET
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Used only when reflection-based serialization is enabled, which trimming and NativeAOT turn off.")]
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Used only when reflection-based serialization is enabled, which trimming and NativeAOT turn off.")]
#endif
	private static IJsonTypeInfoResolver CreateReflectionResolver() => new DefaultJsonTypeInfoResolver();
}
