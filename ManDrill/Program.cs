using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mscc.GenerativeAI;
using System.Data.SQLite;

namespace AICodeFlowchart
{
    class Program
    {
        private static string _apiKey = Environment.GetEnvironmentVariable("APIKEY:Gemini");
        private static GoogleAI _genAi;
        public static GenerativeModel _model;

        static async Task Main(string[] args)
        {
            await ManDrill.Program1.Main1();

            return;
            _genAi = new GoogleAI(_apiKey);
            _model = _genAi.GenerativeModel(Model.Gemini15Flash);

            Console.WriteLine("Enter the path to your C# codebase:");
            string path = Console.ReadLine();

            if (!Directory.Exists(path))
            {
                Console.WriteLine("Invalid path.");
                return;
            }

            List<MethodInfoModel> methods = new();
            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                methods.AddRange(CodeAnalyzer.ExtractMethodsFromFile(file));
            }

            CodeIndexer.StoreInDatabase(methods);

            Console.WriteLine("Enter full method name to analyze (e.g. Namespace.Class.Method):");
            string fullMethod = Console.ReadLine();

            var method = CodeIndexer.GetMethodFromDb(fullMethod);
            if (method == null)
            {
                Console.WriteLine("Method not found.");
                return;
            }

            var result = await GeminiService.GenerateMethodAnalysis(method);
            Console.WriteLine("\n--- Description ---\n" + result.Description);
            Console.WriteLine("\n--- Flowchart (Mermaid) ---\n" + result.Flowchart);
        }
    }

    public static class CodeAnalyzer
    {
        public static List<MethodInfoModel> ExtractMethodsFromFile(string filePath)
        {
            var code = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            var list = new List<MethodInfoModel>();

            foreach (var method in methods)
            {
                var classNode = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                var nsNode = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

                list.Add(new MethodInfoModel
                {
                    Namespace = nsNode?.Name.ToString() ?? "",
                    ClassName = classNode?.Identifier.Text ?? "",
                    MethodName = method.Identifier.Text,
                    FullName = $"{nsNode?.Name}.{classNode?.Identifier.Text}.{method.Identifier.Text}",
                    Code = method.ToFullString()
                });
            }
            return list;
        }
    }

    public static class CodeIndexer
    {
        private const string DbPath = "codeindex.db";

        public static void StoreInDatabase(List<MethodInfoModel> methods)
        {
            if (File.Exists(DbPath)) File.Delete(DbPath);

            SQLiteConnection.CreateFile(DbPath);
            using var conn = new SQLiteConnection($"Data Source={DbPath}");
            conn.Open();

            using var cmd = new SQLiteCommand(@"
                CREATE TABLE Methods (
                    FullName TEXT PRIMARY KEY,
                    Namespace TEXT,
                    ClassName TEXT,
                    MethodName TEXT,
                    Code TEXT
                );", conn);
            cmd.ExecuteNonQuery();

            foreach (var m in methods)
            {
                using var insertCmd = new SQLiteCommand(@"
                    INSERT INTO Methods (FullName, Namespace, ClassName, MethodName, Code)
                    VALUES (@FullName, @Namespace, @ClassName, @MethodName, @Code);", conn);
                insertCmd.Parameters.AddWithValue("@FullName", m.FullName);
                insertCmd.Parameters.AddWithValue("@Namespace", m.Namespace);
                insertCmd.Parameters.AddWithValue("@ClassName", m.ClassName);
                insertCmd.Parameters.AddWithValue("@MethodName", m.MethodName);
                insertCmd.Parameters.AddWithValue("@Code", m.Code);
                insertCmd.ExecuteNonQuery();
            }
        }

        public static MethodInfoModel GetMethodFromDb(string fullMethodName)
        {
            using var conn = new SQLiteConnection($"Data Source={DbPath}");
            conn.Open();
            using var cmd = new SQLiteCommand("SELECT * FROM Methods WHERE FullName = @name", conn);
            cmd.Parameters.AddWithValue("@name", fullMethodName);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new MethodInfoModel
                {
                    FullName = reader.GetString(0),
                    Namespace = reader.GetString(1),
                    ClassName = reader.GetString(2),
                    MethodName = reader.GetString(3),
                    Code = reader.GetString(4)
                };
            }
            return null;
        }
    }

    public static class GeminiService
    {
        public async static Task<(string Description, string Flowchart)> GenerateMethodAnalysis(MethodInfoModel method)
        {
            var prompt = $"""
You are a C# code expert.
Analyze the following method and:
1. Give a short description.
2. Generate a flowchart in mermaid format of its logic including method calls and conditions.

```
{method.Code}
```
""";
            var result = Program._model.GenerateContent(prompt);
            var response = (await result).Text;

            // crude split — real logic can use regex or LLM formatting convention
            var split = response.Split("flowchart", StringSplitOptions.RemoveEmptyEntries);
            var description = split[0].Trim();
            var flow = "flowchart" + (split.Length > 1 ? split[1] : "");

            return (description, flow);
        }
    }

    public class MethodInfoModel
    {
        public string Namespace { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public string FullName { get; set; }
        public string Code { get; set; }
    }
}
