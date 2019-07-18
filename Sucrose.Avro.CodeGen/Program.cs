using Confluent.SchemaRegistry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
				var schemas = await GetSchemas(schemaPath, subjectPattern);
				await Task.WhenAll(
					schemas.Select(schema => GenerateClasses(schema, outputDir, namespaceMapping))
				);

				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"ERROR: {ex.Message}");
				return -1;
			}
		}

		private static Task GenerateClasses(
			string rawSchema,
			string outputDir,
			IEnumerable<string> namespaceMapping
		) => Task.Run(() =>
			{
				var codeGen = new global::Avro.CodeGen();
				var schema = global::Avro.Schema.Parse(rawSchema);

				codeGen.AddSchema(schema);

				if (namespaceMapping != null)
				{
					foreach (var mapping in namespaceMapping)
					{
						var parts = mapping.Split(':');
						codeGen.NamespaceMapping[parts[0]] = parts[1];
					}
				}

				codeGen.GenerateCode();
				codeGen.WriteTypes(outputDir);
			});

		private static async Task<IEnumerable<string>> GetSchemas(string schemaPath, string subjectPattern = ".*")
		{
			if (!schemaPath.StartsWith("http"))
				return await Task.WhenAll(Directory
							.GetFiles(schemaPath)
							.Where(path => Regex.IsMatch(path, subjectPattern))
							.Select(path => File.ReadAllTextAsync(path)))
							.ContinueWith(schemaPromise => schemaPromise.Result);

			var registry = new CachedSchemaRegistryClient(
				new SchemaRegistryConfig
				{
					SchemaRegistryUrl = schemaPath
				});

			var promises = await registry.GetAllSubjectsAsync()
				.ContinueWith(subjectPromise => subjectPromise.Result
					.Where(subject => Regex.IsMatch(subject, subjectPattern))
					.Select(async subject => await registry.GetLatestSchemaAsync(subject))
				);

			return await Task.WhenAll(promises)
				.ContinueWith(schemaPromise => schemaPromise.Result.Select(schema => schema.SchemaString));
		}
	}
}
