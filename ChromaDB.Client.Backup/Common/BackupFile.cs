using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ChromaDB.Client.Models;

namespace ChromaDB.Client.Backup.Common;

// The file of a backup, a zip:
//   manifest.json                       the format, the server, the times, and for each collection its counts and the digest of its records
//   collections/0001/collection.json    name, metadata, configuration_json and schema as the server sent them, dimension
//   collections/0001/records.jsonl      a record a line (RecordJson)
internal static class BackupFile
{
	public const string Format = "chromadotnet-backup";
	public const int FormatVersion = 1;
	public const string ManifestEntry = "manifest.json";

	public static string CollectionEntry(string folder) => folder + "collection.json";

	public static string RecordsEntry(string folder) => folder + "records.jsonl";

	public static string Folder(int index) => $"collections/{index + 1:D4}/";

	public static void WriteCollection(ZipArchive zip, string folder, ChromaCollection collection)
	{
		using var stream = zip.CreateEntry(CollectionEntry(folder), CompressionLevel.Optimal).Open();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = RecordJson.WriterOptions.Encoder, Indented = true });
		writer.WriteStartObject();
		writer.WriteString("name", collection.Name);
		writer.WriteString("id", collection.Id.ToString());
		writer.WritePropertyName("metadata");
		RecordJson.WriteMetadata(writer, collection.Metadata is { Count: > 0 } metadata ? metadata : null);
		WriteElement(writer, "configuration_json", collection.ConfigurationJson);
		WriteElement(writer, "schema", collection.SchemaJson);
		if (collection.Dimension is { } dimension)
		{
			writer.WriteNumber("dimension", dimension);
		}
		else
		{
			writer.WriteNull("dimension");
		}
		writer.WriteEndObject();
	}

	public static SavedCollection ReadCollection(ZipArchive zip, ManifestCollection entry)
	{
		var json = ReadJson(zip, CollectionEntry(entry.Folder));
		var metadata = json.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? RecordJson.ReadMetadata(m) : null;
		return new(
			json.GetProperty("name").GetString()!,
			metadata,
			Element(json, "configuration_json"),
			Element(json, "schema"),
			json.TryGetProperty("dimension", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null);
	}

	public static void WriteManifest(ZipArchive zip, Manifest manifest)
	{
		using var stream = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = RecordJson.WriterOptions.Encoder, Indented = true });
		writer.WriteStartObject();
		writer.WriteString("format", Format);
		writer.WriteNumber("format_version", FormatVersion);
		writer.WriteString("created_by", manifest.CreatedBy);
		writer.WriteString("started_at", manifest.StartedAt);
		writer.WriteString("finished_at", manifest.FinishedAt);
		writer.WriteStartObject("server");
		writer.WriteString("version", manifest.ServerVersion);
		writer.WriteString("name", manifest.ServerName);
		writer.WriteString("server_version", manifest.ServerOwnVersion);
		if (manifest.MaxBatchSize is { } size)
		{
			writer.WriteNumber("max_batch_size", size);
		}
		writer.WriteEndObject();
		writer.WriteString("tenant", manifest.Tenant);
		writer.WriteString("database", manifest.Database);
		writer.WriteBoolean("changed_during_backup", manifest.Collections.Any(x => x.Result.ChangedDuringBackup));
		writer.WriteStartArray("collections");
		foreach (var collection in manifest.Collections)
		{
			var result = collection.Result;
			writer.WriteStartObject();
			writer.WriteString("name", result.Name);
			writer.WriteString("folder", collection.Folder);
			writer.WriteNumber("records", result.Records);
			writer.WriteNumber("count_at_start", result.CountAtStart);
			writer.WriteNumber("count_at_end", result.CountAtEnd);
			writer.WriteNumber("duplicate_ids", result.DuplicateIds);
			writer.WriteNumber("null_embeddings", result.NullEmbeddings);
			writer.WriteStartArray("null_embedding_ids");
			foreach (var id in result.NullEmbeddingIds)
			{
				writer.WriteStringValue(id);
			}
			writer.WriteEndArray();
			writer.WriteBoolean("changed_during_backup", result.ChangedDuringBackup);
			writer.WriteString("records_sha256", collection.Sha256);
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
	}

	public static IReadOnlyList<ManifestCollection> ReadManifest(ZipArchive zip)
	{
		JsonElement json;
		try
		{
			json = ReadJson(zip, ManifestEntry);
		}
		catch (Exception ex) when (ex is FileNotFoundException or JsonException)
		{
			throw new ChromaBackupFormatException($"The file is not a backup of ChromaDotNet: {ex.Message}");
		}
		if (!json.TryGetProperty("format", out var format) || format.GetString() != Format)
		{
			throw new ChromaBackupFormatException("The file is not a backup of ChromaDotNet: its manifest has another format.");
		}
		if (!json.TryGetProperty("format_version", out var version) || version.GetInt32() > FormatVersion)
		{
			throw new ChromaBackupFormatException($"The backup has version {version} of the format, and this library reads up to version {FormatVersion}: a later version of ChromaDotNet reads it.");
		}
		return json.GetProperty("collections").EnumerateArray().Select(x => new ManifestCollection(
			x.GetProperty("name").GetString()!,
			x.GetProperty("folder").GetString()!,
			x.GetProperty("records").GetInt64(),
			x.GetProperty("records_sha256").GetString()!)).ToList();
	}

	// The records of a collection of the backup, a line at a time. At the end, the digest of what was read must be the one of the
	// manifest: a file cut or changed throws then.
	public static IEnumerable<BackupRecord> ReadRecords(ZipArchive zip, ManifestCollection entry)
	{
		var zipEntry = zip.GetEntry(RecordsEntry(entry.Folder)) ?? throw new ChromaBackupFormatException($"The backup has no records for the collection {entry.Name}.");
		using var hash = new Sha256();
		using var stream = zipEntry.Open();
		var buffer = new List<byte>();
		var chunk = new byte[64 * 1024];
		int read;
		while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
		{
			hash.Append(chunk, 0, read);
			for (var i = 0; i < read; i++)
			{
				if (chunk[i] != (byte)'\n')
				{
					buffer.Add(chunk[i]);
					continue;
				}
				if (buffer.Count > 0)
				{
					using var line = JsonDocument.Parse(buffer.ToArray(), RecordJson.DocumentOptions);
					yield return RecordJson.Read(line.RootElement);
					buffer.Clear();
				}
			}
		}
		if (buffer.Count > 0)
		{
			throw new ChromaBackupFormatException($"The records of the collection {entry.Name} end in the middle of a line.");
		}
		if (hash.Hex() != entry.Sha256)
		{
			throw new ChromaBackupFormatException($"The records of the collection {entry.Name} do not match the digest of the manifest: the file is damaged.");
		}
	}

	public static string Hex(byte[] bytes)
	{
		var text = new System.Text.StringBuilder(bytes.Length * 2);
		foreach (var b in bytes)
		{
			text.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
		}
		return text.ToString();
	}

	private static JsonElement ReadJson(ZipArchive zip, string name)
	{
		var entry = zip.GetEntry(name) ?? throw new FileNotFoundException($"The backup has no {name}.");
		using var stream = entry.Open();
		using var document = JsonDocument.Parse(stream);
		return document.RootElement.Clone();
	}

	private static void WriteElement(Utf8JsonWriter writer, string name, JsonElement? element)
	{
		writer.WritePropertyName(name);
		if (element is { } value)
		{
			value.WriteTo(writer);
		}
		else
		{
			writer.WriteNullValue();
		}
	}

	private static JsonElement? Element(JsonElement json, string name)
		=> json.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element.Clone() : null;
}

internal sealed record SavedCollection(string Name, Dictionary<string, object>? Metadata, JsonElement? ConfigurationJson, JsonElement? SchemaJson, int? Dimension);

internal sealed record ManifestCollection(string Name, string Folder, long Records, string Sha256);

internal sealed class Manifest
{
	public required string CreatedBy { get; init; }
	public required string StartedAt { get; init; }
	public string FinishedAt { get; set; } = "";
	public string? ServerVersion { get; init; }
	public string? ServerName { get; init; }
	public string? ServerOwnVersion { get; init; }
	public int? MaxBatchSize { get; init; }
	public string? Tenant { get; set; }
	public string? Database { get; set; }
	public List<(string Folder, string Sha256, ChromaBackupCollection Result)> Collections { get; } = [];
}
