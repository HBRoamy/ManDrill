using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Mscc.GenerativeAI;

namespace ManDrill
{
    class Program1
    {
        private static string _apiKey;
        private static GoogleAI _genAi;
        private static GenerativeModel _model;

        public static async Task Main1()
        {
            _apiKey = Environment.GetEnvironmentVariable("APIKEY:Gemini");
            _genAi = new GoogleAI(_apiKey);
            _model = _genAi.GenerativeModel(Model.Gemini15Flash);

            Console.WriteLine("Enter path to C# file:");
            var path = Console.ReadLine();
            var code = await File.ReadAllTextAsync(path);

            var tree = CSharpSyntaxTree.ParseText(code);
            var root = await tree.GetRootAsync();

            var methodSummaries = new Dictionary<string, string>();
            var methodCalls = new List<(string Caller, string Callee)>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var name = method.Identifier.Text;
                var description = await SummarizeCode(method.ToFullString());
                methodSummaries[name] = description;

                foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var callee = invocation.Expression.ToString().Split('.').Last();
                    methodCalls.Add((name, callee));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("graph TD");
            foreach (var kvp in methodSummaries)
                sb.AppendLine($"{kvp.Key}[\"{kvp.Key}: {Escape(kvp.Value)}\"]");

            foreach (var (caller, callee) in methodCalls)
                sb.AppendLine($"{caller} --> {callee}");

            File.WriteAllText("ai_code_flowchart.mmd", sb.ToString());
            Console.WriteLine("Mermaid file generated: ai_code_flowchart.mmd");
        }

        static async Task<string> SummarizeCode(string code)
        {
            var prompt = $"Explain in 1 line what this method does:\n{code}";
            var result = await _model.GenerateContent(prompt);
            return result.Text.Trim();
        }

        static string Escape(string text) => text.Replace("\"", "\\\"").Replace("\n", " ").Replace("[", "").Replace("]", "");
    }
}
