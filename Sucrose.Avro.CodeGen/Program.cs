using Confluent.SchemaRegistry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sucrose.Avro.CodeGen
{
	using global::Avro;

	class Program
	{
		/// <summary>
		/// avromagic auto-magically generates C# classes from an Avro Schema Registry.
		/// </summary>
		/// <param name="registryUrl">The Schema Registry URl</param>
		/// <param name="subjectPattern">Regex pattern to determine which schemas to retrieve</param>
		/// <param name="outputDir">Output directory where to output the generated C# classes</param>
		/// <param name="namespaceMapping">Map namespace from Producer's to Consumer's e.g. com.user:Sucrose.User</param>
		static async Task<int> Main(
			string registryUrl,
			string subjectPattern = ".*",
			string outputDir = ".",
			IEnumerable<string> namespaceMapping = null
		)
		{
			try
			{
				var registry = new CachedSchemaRegistryClient(
					new SchemaRegistryConfig
					{
						SchemaRegistryUrl = registryUrl
					});

				var schemaPromises = (await registry.GetAllSubjectsAsync())
					.Where(subject => Regex.IsMatch(subject, subjectPattern))
					.Select(async subject => await registry.GetLatestSchemaAsync(subject));

				var schemas = await Task.WhenAll(schemaPromises);
				await Task.WhenAll(
					schemas.Select(schema => GenerateClasses(schema.SchemaString, outputDir, namespaceMapping))
				);

				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return -1;
			}
		}

		private static Task GenerateClasses(
			string rawSchema,
			string outputDir,
			IEnumerable<string> namespaceMapping
		) => Task.Run(() =>
			{
				var codeGen = new CodeGen();
				var schema = Schema.Parse(rawSchema);

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

	}
}
