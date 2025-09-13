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
        public async Task<string?> GenerateMethodSummary(IMethodSymbol methodSymbol, string json)
        {
            if (methodSymbol == null)
                throw new ArgumentNullException(nameof(methodSymbol));

            try
            {
                // Get method implementation details
                var methodBody = await GetMethodImplementation(methodSymbol);
                if (string.IsNullOrEmpty(methodBody))
                    return "Method implementation not available";

                // Create the prompt for Claude
                var prompt = CreateAnalysisPrompt(methodSymbol, methodBody, json);

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
        /// <param name="methodBody">The obfuscated method implementation</param>
        /// <returns>The formatted prompt string</returns>
        private string CreateAnalysisPrompt(IMethodSymbol methodSymbol, string methodBody, string methodCallJson)
        {
            return $"""
You are analyzing a C# method for a code visualization tool. Create a HTML summary that will be displayed inside a data flow graph node.

CRITICAL: Your response must be ONLY the HTML content. DO NOT wrap in markdown code blocks. DO NOT use ``` markers. DO NOT add any explanatory text.

REQUIREMENTS:
- Around 300-400 words total
- Use Bootstrap 5 dark theme classes (bg-dark, text-light, text-info, text-warning)
- Structure as a compact card layout
- Must fit in a 300px wide node
- Start immediately with <div class="bg-dark...
- Green	#34D399
- Blue #60A5FA
- Yellow #FACC15
- Red #F87171
- Purple #A78BFA
- Gray #9CA3AF

METHOD TO ANALYZE:
Signature: {methodSymbol}
Implementation:
{methodBody}

YOUR RESPONSE MUST START WITH: <div class="bg-dark text-light p-2 rounded">

TEMPLATE TO FOLLOW EXACTLY:
<div class="bg-dark text-light p-2 rounded">
    
<h3 class="h3 text-warning fw-bold mb-1 text-center">[Title in 2-3 words]</h3>
<hr class="border-white" />
<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Business Context</div>
<div style="font-size: 0.8rem;">[4–6 non-technical sentences about what this method achieves in business terms, why it matters, and when it is used.]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Technical Context</div>
<div class="mb-2" style="font-size: 0.8rem;">[4–6 sentences describing the method’s purpose, logic, and role in the overall system. Mention dependencies, data flow, or critical considerations.]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Key Operations</div>
<div class="mb-2" style="font-size: 0.8rem;">[6–7 bullet points with • symbol, each max 8 words, highlighting the main operations or transformations done by this method.]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Flow Diagram</div>
<div style="font-size: 0.8rem;">
  [Generate a visually clear, centrally-aligned, colorful, and professional-looking HTML-based flowchart from the given <code>{methodCallJson}</code>. 
  Requirements:
  <ul>
    <li>Color-coded nodes (input, process, output, decision). Colors are provided above</li>
    <li>Directional arrows to indicate execution flow.Should touch nodes, not overflow it.</li>
    <li>Readable text labels, short and concise.</li>
    <li>Responsive and scrollable for large diagrams.</li>
    <li>No emojis, clean professional look.</li>
  </ul>]
</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Parameters</div>
<div style="font-size: 0.8rem;">[List only the 2–3 most important parameters with short explanations (1 line each).]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Dependencies</div>
<div style="font-size: 0.8rem;">[Mention, in bullet points with • symbol, internal/external libraries, services, or methods this depends on.]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Performance Notes</div>
<div style="font-size: 0.8rem;">[Highlight any performance-sensitive logic, caching, or scalability concerns.]</div>

<div class="text-info fw-bold mb-1" style="font-size: 0.85rem;">Conclusion</div>
<div style="font-size: 0.8rem;">[Meaningful conclusion of the document in 3-4 sentences.]</div>

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
                max_tokens = 10000,
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
        #endregion
    }
}
