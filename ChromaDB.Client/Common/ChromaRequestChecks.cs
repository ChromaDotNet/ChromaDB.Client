using System.Collections;
using System.Text.Json;

namespace ChromaDB.Client.Common;

// What a server would drop, change or fail on, stopped before the request with a message that says why.
internal static class ChromaRequestChecks
{
	// An empty list in the metadata of a record: Chroma 0.6.3 and 1.5.9 drop the key without an error, 1.0 to 1.4
	// reject it, Chroma Cloud stores it, and the Python client rejects it. Rejected here too, so that the same code does
	// the same on every server.
	public static void NoEmptyLists(IReadOnlyList<IReadOnlyDictionary<string, object>?>? metadatas, string paramName)
	{
		if (metadatas is null)
		{
			return;
		}
		foreach (var metadata in metadatas)
		{
			if (metadata is null)
			{
				continue;
			}
			foreach (var pair in metadata)
			{
				if (IsList(pair.Value) && IsEmpty(pair.Value))
				{
					throw new ArgumentException($"The list of the metadata key \"{pair.Key}\" is empty: Chroma takes lists with at least one value, as the Python client checks, and a single server drops an empty one without an error.", paramName);
				}
			}
		}
	}

	// No Chroma stores a list in the metadata of a collection: 0.x and 1.0 to 1.4 reject it, Chroma Cloud answers 500
	// and 1.5.9 closes the connection.
	public static void NoLists(IReadOnlyDictionary<string, object>? metadata, string paramName)
	{
		if (metadata is null)
		{
			return;
		}
		foreach (var pair in metadata)
		{
			if (IsList(pair.Value))
			{
				throw new ArgumentException($"The metadata key \"{pair.Key}\" of the collection has a list: Chroma stores lists only in the metadata of records.", paramName);
			}
		}
	}

	// A JsonElement array is what the client returns for a list read with ChromaMetadataValues.Inferred.
	public static bool IsList(object? value)
		=> value is JsonElement { ValueKind: JsonValueKind.Array }
			or IEnumerable and not string and not IDictionary;

	private static bool IsEmpty(object? value)
	{
		if (value is JsonElement element)
		{
			return element.GetArrayLength() == 0;
		}
		var enumerator = ((IEnumerable)value!).GetEnumerator();
		try
		{
			return !enumerator.MoveNext();
		}
		finally
		{
			(enumerator as IDisposable)?.Dispose();
		}
	}

	// A lone half of a surrogate pair: UTF-8 has no form for it, and System.Text.Json would send U+FFFD in its place, so
	// an id or a document would come back changed.
	public static void NoLoneSurrogates(string value, string what)
	{
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];
			if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
			{
				i++;
				continue;
			}
			if (char.IsSurrogate(c))
			{
				throw new ArgumentException($"{what} has a lone surrogate, U+{(int)c:X4} at {i}: UTF-8 cannot carry it, and it would reach the server as U+FFFD.");
			}
		}
	}
}
