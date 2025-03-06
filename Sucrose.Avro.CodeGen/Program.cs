using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avro;
using Confluent.SchemaRegistry;
using Schema = Avro.Schema;

namespace Sucrose.Avro.CodeGen
{
	class Program
	{
		/// <summary>
		/// avromagic auto-magically generates C# classes from an Avro Schema Registry.
		/// </summary>
		/// <param name="schemaPath">The schema registry URL or File Path</param>
		/// <param name="subjectPattern">Regex pattern to determine which schemas to retrieve</param>
		/// <param name="outputDir">Output directory where to output the generated C# classes</param>
		/// <param name="namespaceMapping">Map namespace from Producer's to Consumer's e.g. com.user:Sucrose.User</param>
		static async Task<int> Main(
			string schemaPath,
			string subjectPattern = ".*",
			string outputDir = ".",
			string[] namespaceMapping = null
		)
		{
			try
			{
				Console.WriteLine($"Loading schemas from [{schemaPath}]");
				var schemas = await GetSchemas(schemaPath, subjectPattern);

				var codeGen = new global::Avro.CodeGen();
				if (namespaceMapping != null)
				{
					foreach (var mapping in namespaceMapping)
					{
						var parts = mapping.Split(':');
						codeGen.NamespaceMapping[parts[0]] = parts[1];
					}
				}

				ParseSchemas(codeGen, schemas);

				Console.WriteLine($"Generating code ...");
				codeGen.GenerateCode();

				Console.WriteLine($"Output code to [{Path.GetFullPath(outputDir)}]");
				codeGen.WriteTypes(outputDir);

				Console.WriteLine("Done");

				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"ERROR: {ex.Message}");
				return -1;
			}
		}

		private static void ParseSchemas(global::Avro.CodeGen codeGen, IEnumerable<(string subject, string content)> schemas)
		{
			var schemaList = schemas.ToList();
			var parsed = new SchemaNames();
			List<(string subject, string content)> successful;

			do
			{
				successful = [];

				foreach (var schema in schemaList)
				{
					try
					{
						ParseSchema(codeGen, schema, parsed);
						successful.Add(schema);
					}
					catch (SchemaParseException e)
					{
						Console.WriteLine($"[{schema.subject}] Failed parsing Schema; '{e.Message}'. Will retry ...");
					}
				}

				schemaList = schemaList.Except(successful).ToList();

			} while (schemaList.Count > 0 && successful.Count > 0);

			if (schemaList.Count <= 0) return;

			var schemaNames = schemaList.Select(x => x.subject);
			var failedList = string.Join(", ", schemaNames);
			Console.WriteLine($"The following schema subjects were not successfully parsed: [{failedList}]");
		}

		private static void ParseSchema(global::Avro.CodeGen codeGen, (string subject, string content) schema, SchemaNames parsedSchemas)
		{
			Console.WriteLine($"[{schema.subject}] Parsing Schema ...");

			var parsedSchema = Schema.Parse(schema.content, parsedSchemas);
			parsedSchemas.Add((NamedSchema) parsedSchema);
			codeGen.AddSchema(parsedSchema);

			Console.WriteLine($"[{schema.subject}] Done parsing Schema ...");
		}

		private static async Task<(string subject, string content)> ReadSchemaFromFileAsync(FileInfo file)
		{
			using var textFile = file.OpenText();
			var content = await textFile.ReadToEndAsync();

			return (file.Name, content);
		}

		private static async Task<(string subject, string content)> ReadSchemaFromRegistryAsync(
			ISchemaRegistryClient schemaRegistryClient,
			string subject
		)
		{
			var schema = await schemaRegistryClient.GetLatestSchemaAsync(subject);
			return (subject, schema.SchemaString);
		}

		private static async Task<IEnumerable<(string subject, string content)>> GetSchemas(string schemaPath, string subjectPattern = ".*")
		{
			IEnumerable<Task<(string subject, string content)>> promises;
			if (!schemaPath.StartsWith("http"))
			{
				if (schemaPath.StartsWith("~/"))
				{
					// hack around the fact that ~/ is not evaluated by the runtime
					schemaPath = schemaPath.Replace(
						"~",
						Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
					);
				}

				var directoryInfo = new DirectoryInfo(schemaPath);

				promises = directoryInfo
					.GetFiles("*.avsc", SearchOption.AllDirectories)
					.Where(path => Regex.IsMatch(path.Name, subjectPattern))
					.Select(ReadSchemaFromFileAsync);
			}
			else
			{
				var registry = new CachedSchemaRegistryClient(
					new SchemaRegistryConfig
					{
						Url = schemaPath
					});

				promises = await registry.GetAllSubjectsAsync()
					.ContinueWith(subjectPromise => subjectPromise.Result
						.Where(subject => Regex.IsMatch(subject, subjectPattern))
						.Select(subject => ReadSchemaFromRegistryAsync(registry, subject))
					);
			}

			return await Task.WhenAll(promises)
				.ContinueWith(schemaPromise => schemaPromise.Result);
		}
	}
}
