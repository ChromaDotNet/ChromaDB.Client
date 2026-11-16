using System.CommandLine;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using System.Diagnostics;
using ChromaDB.Client.Backup;

namespace ChromaDB.Client.Cli;

// The command chromadotnet: backup, restore and verify of the collections of a database, with ChromaDotNet.Client.Backup.
// Exit codes: 0 done; 1 an error, or a collection that a restore left as it was without being told; 2 a backup of a server
// that took writes meanwhile, or a verification that found differences.
internal static class Program
{
	// The servers the tests try, besides Chroma Cloud: with another one, distributed Chroma installed by yourself included, the command warns and goes on.
	internal const string TestedChroma = "1.5.9";
	internal const string TestedVecturaDb = "1.0.0-preview.10.post1";

	public static Task<int> Main(string[] args)
		=> RunAsync(args, Console.Out, Console.Error, Console.IsInputRedirected ? null : Console.In, null);

	// input is null without a terminal to ask in; newClient makes the client of the options, for the tests.
	internal static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, TextReader? input, Func<ChromaConfigurationOptions, ChromaClient>? newClient)
	{
		var url = new Option<string>("--url") { Description = "The address of the server, like http://localhost:8000 or https://api.trychroma.com.", Required = true };
		var token = new Option<string?>("--token") { Description = "The API key or token, sent as X-Chroma-Token. Without it, the environment variable CHROMA_API_KEY, so that it stays out of the history of the shell." };
		var tenant = new Option<string?>("--tenant") { Description = "The tenant. Without it, CHROMA_TENANT; on Chroma Cloud the one of the API key; otherwise default_tenant." };
		var database = new Option<string?>("--database") { Description = "The database. Without it, CHROMA_DATABASE; on Chroma Cloud the one of the API key, when it has one; otherwise default_database." };
		var distributed = new Option<bool>("--distributed") { Description = "The server is distributed Chroma, not on Chroma Cloud." };
		var collections = new Option<string[]>("--collections") { Description = "A collection of the backup, once for each one; without it, all of them." };
		var saved_ = new Option<string[]>("--collections") { Description = "A collection to save, once for each one; or --all." };
		var batch = new Option<int>("--batch") { Description = "The records of one request, from 1 to 300.", DefaultValueFactory = _ => 100 };

		var all = new Option<bool>("--all") { Description = "Every collection of the database." };
		var output_ = new Option<string>("--out") { Description = "The file of the backup, which must not exist.", Required = true };
		var concurrent = new Option<int>("--concurrent") { Description = "The gets at the same time, from 1 to 8.", DefaultValueFactory = _ => 5 };
		var backup = new Command("backup", "Saves collections of a database into a zip file. If the server takes writes meanwhile, the file can hold records of different moments: the command then says so and exits with 2.")
		{
			url, token, tenant, database, distributed, all, saved_, output_, batch, concurrent,
		};
		backup.Validators.Add(result =>
		{
			if (result.GetValue(all) == (result.GetValue(saved_) is { Length: > 0 }))
			{
				result.AddError("Name the collections with --collections, once for each one, or save all of them with --all.");
			}
		});

		var input_ = new Option<string>("--in") { Description = "The file of the backup.", Required = true };
		var resume = new Option<bool>("--resume") { Description = "A collection that exists and is not the same gets the records it does not have; the rest of it stays." };
		var force = new Option<bool>("--force") { Description = "A collection that exists and is not the same becomes the same as the backup." };
		var yes = new Option<bool>("--yes") { Description = "With --resume or --force, without asking." };
		var restore = new Command("restore", "Writes the collections of a backup into a database. Every collection that exists is compared first; one that is not the same is changed only with --resume or --force.")
		{
			url, token, tenant, database, distributed, input_, collections, resume, force, yes, batch,
		};
		restore.Validators.Add(result =>
		{
			if (result.GetValue(resume) && result.GetValue(force))
			{
				result.AddError("--resume and --force go one at a time.");
			}
			if (result.GetValue(yes) && !result.GetValue(resume) && !result.GetValue(force))
			{
				result.AddError("--yes goes with --resume or --force.");
			}
		});

		var verify = new Command("verify", "Compares the collections of a backup with the ones of a database, and exits with 2 when they are not the same. It writes nothing.")
		{
			url, token, tenant, database, distributed, input_, collections,
		};

		backup.SetAction((result, cancellationToken) => Run(error, async () =>
		{
			var client = await Connect(result.GetValue(url)!, result.GetValue(token), result.GetValue(tenant), result.GetValue(database), result.GetValue(distributed), newClient, error, cancellationToken);
			var names = result.GetValue(saved_);
			var saved = await ChromaBackup.BackupAsync(client, result.GetValue(output_)!, new() { Collections = result.GetValue(all) ? null : names, BatchSize = result.GetValue(batch), Concurrency = result.GetValue(concurrent) }, new Progress(output, error), cancellationToken);
			foreach (var collection in saved.Collections)
			{
				output.WriteLine($"{collection.Name}: {collection.Records} records");
				if (collection.NullEmbeddings > 0)
				{
					error.WriteLine($"{collection.Name}: the server sent no embedding for {collection.NullEmbeddings} records, which a restore cannot write, like {string.Join(", ", collection.NullEmbeddingIds.Take(5))}.");
				}
				if (collection.ChangedDuringBackup)
				{
					error.WriteLine($"{collection.Name}: {collection.CountAtStart} records at the start, {collection.CountAtEnd} at the end, {collection.Records} in the file, {collection.DuplicateIds} ids given twice.");
				}
			}
			output.WriteLine($"Backup written to {saved.Path}: {saved.Collections.Count} collections, {saved.Collections.Sum(x => x.Records)} records.");
			if (saved.ChangedDuringBackup)
			{
				error.WriteLine("The server took writes during the backup: the file can hold records of different moments. For a consistent backup, stop the writes, or the server.");
				return 2;
			}
			return 0;
		}));

		restore.SetAction((result, cancellationToken) => Run(error, async () =>
		{
			var client = await Connect(result.GetValue(url)!, result.GetValue(token), result.GetValue(tenant), result.GetValue(database), result.GetValue(distributed), newClient, error, cancellationToken);
			var chosen = result.GetValue(resume) ? ChromaRestoreExisting.Resume : result.GetValue(force) ? ChromaRestoreExisting.Force : (ChromaRestoreExisting?)null;
			var leftWithoutAnswer = 0;
			ChromaRestoreExisting Choose(ChromaVerifyCollection collection)
			{
				var summary = $"{collection.Name} exists and is not the same as in the backup: {Describe(collection)}.";
				if (chosen is { } existing && result.GetValue(yes))
				{
					return existing;
				}
				if (input is null)
				{
					leftWithoutAnswer++;
					error.WriteLine($"{summary} It stays as it is: without a terminal, run again with {(chosen == ChromaRestoreExisting.Force ? "--force" : chosen == ChromaRestoreExisting.Resume ? "--resume" : "--resume or --force")} and --yes.");
					return ChromaRestoreExisting.Skip;
				}
				if (chosen is { } flag)
				{
					output.Write($"{summary} {(flag == ChromaRestoreExisting.Resume ? "Add the records it does not have" : "Make it the same as the backup")}? [y/N] ");
					return input.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes" ? flag : ChromaRestoreExisting.Skip;
				}
				while (true)
				{
					output.Write($"{summary} [r]esume: add the records it does not have; [f]orce: make it the same as the backup; [l]eave it as it is. ");
					switch (input.ReadLine()?.Trim().ToLowerInvariant())
					{
						case "r" or "resume":
							return ChromaRestoreExisting.Resume;
						case "f" or "force":
							return ChromaRestoreExisting.Force;
						case "l" or "leave" or null:
							return ChromaRestoreExisting.Skip;
					}
				}
			}
			var restored = await ChromaBackup.RestoreAsync(client, result.GetValue(input_)!, new() { Collections = Names(result.GetValue(collections)), ChooseExisting = Choose, BatchSize = result.GetValue(batch) }, new Progress(output, error), cancellationToken);
			foreach (var collection in restored.Collections)
			{
				output.WriteLine(collection switch
				{
					{ AlreadySame: true } => $"{collection.Name}: already the same",
					{ Skipped: true } => $"{collection.Name}: left as it is",
					_ => $"{collection.Name}: {(collection.Created ? "created, " : "")}{collection.Added} records added, {collection.Rewritten} written again, {collection.Deleted} deleted",
				});
				if (collection.WithoutEmbedding > 0)
				{
					error.WriteLine($"{collection.Name}: {collection.WithoutEmbedding} records of the backup have no embedding, which the server had sent as null, and were not written.");
				}
			}
			return leftWithoutAnswer > 0 ? 1 : 0;
		}));

		verify.SetAction((result, cancellationToken) => Run(error, async () =>
		{
			var client = await Connect(result.GetValue(url)!, result.GetValue(token), result.GetValue(tenant), result.GetValue(database), result.GetValue(distributed), newClient, error, cancellationToken);
			var compared = await ChromaBackup.VerifyAsync(client, result.GetValue(input_)!, Names(result.GetValue(collections)), new Progress(output, error), cancellationToken);
			foreach (var collection in compared.Collections)
			{
				output.WriteLine(collection.Same ? $"{collection.Name}: the same, {collection.SameRecords} records" : $"{collection.Name}: not the same: {Describe(collection)}");
			}
			return compared.Same ? 0 : 2;
		}));

		var root = new RootCommand("Backup, restore and verify of Chroma collections through the API, tested with Chroma 1.5.9, VecturaDB and Chroma Cloud. A backup of a server that takes writes meanwhile can hold records of different moments.")
		{
			backup, restore, verify,
		};
		return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
	}

	private static async Task<ChromaClient> Connect(string url, string? token, string? tenant, string? database, bool distributed, Func<ChromaConfigurationOptions, ChromaClient>? newClient, TextWriter error, CancellationToken cancellationToken)
	{
		var options = new ChromaConfigurationOptions(url);
		if ((token ?? Environment.GetEnvironmentVariable("CHROMA_API_KEY")) is { Length: > 0 } key)
		{
			options = options.WithChromaToken(key);
		}
		if ((tenant ?? Environment.GetEnvironmentVariable("CHROMA_TENANT")) is { Length: > 0 } tenantName)
		{
			options = options.WithTenant(tenantName);
		}
		if ((database ?? Environment.GetEnvironmentVariable("CHROMA_DATABASE")) is { Length: > 0 } databaseName)
		{
			options = options.WithDatabase(databaseName);
		}
		if (distributed)
		{
			options = options.WithDistributedChroma(true);
		}
		var client = newClient?.Invoke(options) ?? new ChromaClient(options);
		if (options.IsChromaCloud && options.Tenant is null or [])
		{
			client = await client.WithTenantAndDatabaseFromIdentityAsync(cancellationToken);
		}
		await WarnIfNotTested(client, options, error, cancellationToken);
		return client;
	}

	private static async Task WarnIfNotTested(ChromaClient client, ChromaConfigurationOptions options, TextWriter error, CancellationToken cancellationToken)
	{
		if (options.IsChromaCloud)
		{
			return;
		}
		if (options.IsDistributedChroma)
		{
			error.WriteLine("Distributed Chroma installed by yourself has not been tested with chromadotnet, which goes on: the tests use Chroma Cloud, which runs on distributed Chroma.");
			return;
		}
		ChromaPreFlightChecks? preFlight = null;
		try
		{
			preFlight = await client.GetPreFlightChecksAsync(cancellationToken);
		}
		catch (ChromaException)
		{
		}
		if (preFlight?.Server is { Name: "VecturaDB" } server)
		{
			if (server.Version != TestedVecturaDb)
			{
				error.WriteLine($"VecturaDB {server.Version} has not been tested with chromadotnet, which goes on: the tests use VecturaDB {TestedVecturaDb}.");
			}
			return;
		}
		var version = await client.GetVersionAsync(cancellationToken);
		// Every Chroma 1.x answers 1.0.0, so 1.5.9 cannot be told from another 1.x: only Chroma 0.x gets the warning.
		if (version.StartsWith("0.", StringComparison.Ordinal))
		{
			error.WriteLine($"Chroma {version} has not been tested with chromadotnet, which goes on: the tests use Chroma {TestedChroma}, VecturaDB {TestedVecturaDb} and Chroma Cloud. Every Chroma 1.x answers 1.0.0 as its version, so no warning tells one 1.x from another.");
		}
	}

	private static IReadOnlyList<string>? Names(string[]? names) => names is { Length: > 0 } ? names : null;

	private static string Describe(ChromaVerifyCollection collection)
		=> !collection.Exists ? "not on the server"
			: $"{collection.SameRecords} records the same, {collection.Missing} missing, {collection.Different} different, {collection.OnlyOnServer} only on the server"
				+ (collection.SchemaSame ? "" : ", another schema or dimension")
				+ (collection.MetadataSame ? "" : ", other metadata")
				+ (collection.FirstDifference is { } first ? $", the first difference {first}" : "");

	private static async Task<int> Run(TextWriter error, Func<Task<int>> body)
	{
		try
		{
			return await body();
		}
		catch (Exception ex) when (ex is ChromaException or ChromaBackupFormatException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or HttpRequestException)
		{
			error.WriteLine(ex.Message);
			return 1;
		}
	}

	// The progress: a line every 5 seconds for the records of a collection, and every wait of a request.
	private sealed class Progress(TextWriter output, TextWriter error) : IProgress<ChromaBackupProgress>
	{
		private readonly Stopwatch _since = Stopwatch.StartNew();

		public void Report(ChromaBackupProgress value)
		{
			if (value.Message is { } message)
			{
				error.WriteLine($"{(value.Collection is { Length: > 0 } name ? name + ": " : "")}{message}");
				return;
			}
			if (_since.Elapsed >= TimeSpan.FromSeconds(5))
			{
				output.WriteLine($"{value.Collection}: {value.Records}{(value.Total is { } total ? $" of {total}" : "")} records");
				_since.Restart();
			}
		}
	}
}
