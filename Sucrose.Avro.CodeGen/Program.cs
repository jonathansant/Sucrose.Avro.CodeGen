﻿using System;
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

				Console.WriteLine($"\nGenerating code ...");
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

		/// <summary>
		/// Iteratively parses a list of schemas.
		/// <i>On the first iteration all the schemas that do not reference other schemas should be successfully parsed.
		/// On subsequent iterations, the schemas that weren't parsed (ie. the ones referencing other schemas) are
		/// iteratively retried, until either all are successfully parsed or no more progress is made.</i>
		/// </summary>
		private static void ParseSchemas(
			global::Avro.CodeGen codeGen,
			IEnumerable<(string subject, string content)> schemas
		)
		{
			var schemaList = schemas.ToList();

			List<(string subject, string content)> successfullyParsed;
			var result = new ParsingResult(new SchemaNames(), true);
			var iterationCount = 0;

			do
			{
				Console.WriteLine($"\nParsing iteration {iterationCount++}");
				successfullyParsed = [];

				foreach (var schema in schemaList)
				{
					result = TryParseSchema(codeGen, schema, result.SchemaNames);

					if (result.Success)
					{
						successfullyParsed.Add(schema);
					}
				}

				// remove the successfully parsed schemas from the list for next iteration
				schemaList = schemaList.Except(successfullyParsed).ToList();

			} while (schemaList.Count > 0 && successfullyParsed.Count > 0); // repeat until no more schemas to parse, or no progress is made

			// all schemas successfully parsed
			if (schemaList.Count <= 0) return;

			var failedList = string.Join(", ", schemaList.Select(x => x.subject));
			Console.WriteLine($"The following schema subjects were not successfully parsed: [{failedList}]");
		}

		/// <summary>
		/// Tries to parse the schema and adds it to the codeGen.
		/// </summary>
		/// <returns>If parsing succeeds, returns the latest schemaNames, else returns the original.</returns>
		private static ParsingResult TryParseSchema(
			global::Avro.CodeGen codeGen,
			(string subject, string content) schema,
			SchemaNames schemaNames
		)
		{
			Console.WriteLine($"\n[{schema.subject}] Parsing Schema ...");

			// when Schema.Parse fails, it leaves partial results in schemaNames
			var originalSchemaNames = schemaNames.Clone();
			Schema parsedSchema;

			try
			{
				parsedSchema = Schema.Parse(schema.content, schemaNames);
			}
			catch (SchemaParseException e)
			{
				Console.WriteLine($"[{schema.subject}] Failed parsing Schema; '{e.Message}'.");

				// if we return the partial results of a failed parse (e.g. 2 out of 3 schema names added to schemaNames),
				// the next iteration will throw on duplicate schema names
				return new ParsingResult(originalSchemaNames, false);
			}

			codeGen.AddSchema(parsedSchema);

			Console.WriteLine($"[{schema.subject}] Done parsing Schema ...");
			return new ParsingResult(schemaNames, true);
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

internal record ParsingResult(SchemaNames SchemaNames, bool Success);

internal static class Extensions
{
	internal static SchemaNames Clone(this SchemaNames schemaNames)
	{
		var newSchemaNames = new SchemaNames();

		foreach (var schema in schemaNames)
		{
			newSchemaNames.Add(schema.Value);
		}

		return newSchemaNames;
	}
}