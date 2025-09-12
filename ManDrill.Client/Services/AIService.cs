using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace ManDrill.Client.Services
{
    /// <summary>
    /// Service for generating AI-powered method summaries using Amazon Bedrock Claude API
    /// </summary>
    public class AIService
    {
        #region Private Fields

        /// <summary>
        /// The Amazon Bedrock runtime client for making API calls to Claude
        /// </summary>
        private readonly AmazonBedrockRuntimeClient _bedrockClient;

        /// <summary>
        /// The Claude model ID to use for generating summaries
        /// </summary>
        private const string ClaudeModelId = "us.anthropic.claude-3-7-sonnet-20250219-v1:0";// TODO: Try moving it to the config

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the AIService class
        /// </summary>
        /// <param name="bedrockClient">Optional Bedrock client. If null, creates a new instance with default configuration</param>
        public AIService()
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(
                Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"),
                Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));

            _bedrockClient = new AmazonBedrockRuntimeClient(
                awsCredentials,
                Amazon.RegionEndpoint.USEast1);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Generates an AI-powered HTML summary for a C# method using Amazon Bedrock Claude API
        /// </summary>
        /// <param name="methodSymbol">The method symbol to analyze and summarize</param>
        /// <returns>
        /// An HTML summary string suitable for display in a data flow graph node,
        /// or "Method summary unavailable" if generation fails
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when methodSymbol is null</exception>
        public async Task<string?> GenerateMethodSummary(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                throw new ArgumentNullException(nameof(methodSymbol));

            try
            {
                // Get method implementation details
                var methodBody = await GetMethodImplementation(methodSymbol);
                if (string.IsNullOrEmpty(methodBody))
                    return "Method implementation not available";

                // Obfuscate any sensitive data in the method body
                var obfuscatedBody = ObfuscateOrgData(methodBody);

                // Create the prompt for Claude
                var prompt = CreateAnalysisPrompt(methodSymbol, obfuscatedBody);

                // Generate the summary using Claude
                var summary = await GenerateSummaryWithClaude(prompt);
                return summary;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating method summary: {ex.Message}");
                return "Method summary unavailable";
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Extracts the method implementation from the method symbol
        /// </summary>
        /// <param name="methodSymbol">The method symbol to extract implementation from</param>
        /// <returns>The method implementation as a string, or null if not available</returns>
        private async Task<string?> GetMethodImplementation(IMethodSymbol methodSymbol)
        {
            var methodLocation = methodSymbol.Locations.FirstOrDefault();
            if (methodLocation == null || !methodLocation.IsInSource)
                return null;

            var syntaxTree = await methodLocation.SourceTree.GetRootAsync();
            var methodNode = syntaxTree.FindNode(methodLocation.SourceSpan);
            return methodNode.ToString();
        }

        /// <summary>
        /// Creates the analysis prompt for Claude
        /// </summary>
        /// <param name="methodSymbol">The method symbol being analyzed</param>
        /// <param name="obfuscatedBody">The obfuscated method implementation</param>
        /// <returns>The formatted prompt string</returns>
        private string CreateAnalysisPrompt(IMethodSymbol methodSymbol, string obfuscatedBody)
        {
            return $"""
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
        }

        /// <summary>
        /// Generates a summary using Amazon Bedrock Claude API
        /// </summary>
        /// <param name="prompt">The prompt to send to Claude</param>
        /// <returns>The generated summary text</returns>
        private async Task<string> GenerateSummaryWithClaude(string prompt)
        {
            // Create the request payload for Claude
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 1000,
                temperature = 0.1,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            var requestBodyJson = JsonSerializer.Serialize(requestBody);
            var requestBodyBytes = System.Text.Encoding.UTF8.GetBytes(requestBodyJson);
            var request = new InvokeModelRequest
            {
                ModelId = ClaudeModelId,
                ContentType = "application/json",
                Body = new MemoryStream(requestBodyBytes)
            };

            var response = await _bedrockClient.InvokeModelAsync(request);
            
            using var reader = new StreamReader(response.Body);
            var responseJson = await reader.ReadToEndAsync();
            
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);
            return responseData.GetProperty("content")[0].GetProperty("text").GetString() ?? "Summary generation failed";
        }

        /// <summary>
        /// Obfuscates organization-specific data in method implementations to protect sensitive information
        /// </summary>
        /// <param name="input">The input string to obfuscate</param>
        /// <returns>The obfuscated string with sensitive data replaced with generic placeholders</returns>
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

        #endregion
    }
}
