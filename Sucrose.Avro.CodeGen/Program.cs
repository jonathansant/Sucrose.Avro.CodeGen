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
		/// avromagic auto-magically generates c# classes from an Avro Schema Registry.
		/// </summary>
		/// <param name="registryUrl">The Schema Registry URl</param>
		/// <param name="subjectPattern"></param>
		/// <param name="outputDir"></param>
		/// <returns></returns>
		static async Task<int> Main(
			string registryUrl,
			string subjectPattern = ".*",
			string outputDir = "."
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
				foreach (var schema in schemas)
				{
					GenerateClasses(
						schema.SchemaString,
						outputDir,
						new Dictionary<string, string>()
					);
				}

				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return -1;
			}
		}

		private static void GenerateClasses(
			string rawSchema,
			string outputDir,
			Dictionary<string, string> namespaceMapping
		)
		{
			var codeGen = new CodeGen();
			var schema = Schema.Parse(rawSchema);

			codeGen.AddSchema(schema);

			foreach (var (key, value) in namespaceMapping)
				codeGen.NamespaceMapping[key] = value;

			codeGen.GenerateCode();
			codeGen.WriteTypes(outputDir);
		}
	}
}
