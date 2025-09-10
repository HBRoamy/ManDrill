using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Mscc.GenerativeAI;

namespace ManDrill.Client.Services
{
    public class AIService
    {
        private readonly GoogleAI _generativeAi;
        public AIService()
        {
            _generativeAi = new GoogleAI(Environment.GetEnvironmentVariable("APIKEY:Gemini"));
        }

        public async Task<string?> GenerateMethodSummary(IMethodSymbol methodSymbol)
        {
            try
            {
                // Get method implementation details
                var methodLocation = methodSymbol.Locations.FirstOrDefault();
                if (methodLocation == null || !methodLocation.IsInSource)
                    return "Method implementation not available";

                var syntaxTree = await methodLocation.SourceTree.GetRootAsync();
                var methodNode = syntaxTree.FindNode(methodLocation.SourceSpan);
                var methodBody = methodNode.ToString();

                // Obfuscate any sensitive data in the method body
                var obfuscatedBody = ObfuscateOrgData(methodBody);

                var model = _generativeAi.GenerativeModel(model: Model.Gemini15Flash);
                var prompt = $"""
You are analyzing a C# method for a code visualization tool. Create a HTML summary that will be displayed inside a data flow graph node.

CRITICAL: Your response must be ONLY the HTML content. DO NOT wrap in markdown code blocks. DO NOT use ``` markers. DO NOT add any explanatory text.

REQUIREMENTS:
- Around 150-200 words total
- Use Bootstrap 5 dark theme classes (bg-dark, text-light, text-info, text-warning)
- Structure as a compact card layout
- Must fit in a 300px wide node
- Start immediately with <div class="bg-dark...

METHOD TO ANALYZE:
Signature: {methodSymbol}
Implementation:
{obfuscatedBody}

YOUR RESPONSE MUST START WITH: <div class="bg-dark text-light p-2 rounded">

TEMPLATE TO FOLLOW EXACTLY:
<div class="bg-dark text-light p-2 rounded">
    <div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Purpose</div>
    <div class="mb-2" style="font-size: 0.8rem;">[1-2 sentences about what this method does]</div>
    
    <div class="text-warning fw-bold mb-1" style="font-size: 0.85rem;">Key Operations</div>
    <div class="mb-2" style="font-size: 0.8rem;">[2-3 bullet points with • symbol, each max 8 words]</div>
    
    <div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Parameters</div>
    <div style="font-size: 0.8rem;">[List important parameters only, max 3]</div>
</div>

REMEMBER: No markdown, no code blocks, no explanations. Just the HTML starting with <div class="bg-dark...
""";

                var response = await model.GenerateContent(prompt);
                return response.Text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating method summary: {ex.Message}");
                return "Method summary unavailable";
            }
        }

        private static string ObfuscateOrgData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Replace common organization-related keywords with generic placeholders
            var orgKeywords = new[] { "corp", "company", "org", "enterprise", "business", "firm", "AdminPortal", "Par", "Partech", "Parcorp", "UpgradePortal" };
            var pattern = string.Join("|", orgKeywords);
            var regex = new Regex($@"\b({pattern})\b", RegexOptions.IgnoreCase);

            // Obfuscate namespace declarations and usages
            var namespaceRegex = new Regex(@"namespace\s+([A-Za-z0-9_.]+)", RegexOptions.IgnoreCase);
            input = namespaceRegex.Replace(input, "namespace [REDACTED_NAMESPACE]");

            // Obfuscate namespace-like structures that might contain org names
            input = regex.Replace(input, match => "[REDACTED]")
                         .Replace("Company.", "Namespace.")
                         .Replace("Corp.", "Namespace.");

            return input;
        }
    }
}
