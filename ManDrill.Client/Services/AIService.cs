using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using ManDrill.Client.Models;
using Microsoft.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
        /// Atlassian Domain name
        /// </summary>
        private readonly string _domain = Environment.GetEnvironmentVariable("Atlassian_Domain") ?? string.Empty;
        /// <summary>
        /// Email address of registered atlassian account
        /// </summary>
        private readonly string _email = Environment.GetEnvironmentVariable("Par_Email") ?? string.Empty; 
        /// <summary>
        /// API Token of atlassian account
        /// </summary>
        private readonly string _apiToken = Environment.GetEnvironmentVariable("Atlassian_Api_Token") ?? string.Empty;
        /// <summary>
        /// The Claude model ID to use for generating summaries
        /// </summary>
        private const string ClaudeModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";// TODO: Try moving it to the config anthropic.claude-3-5-sonnet-20240620-v1:0

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

        public async Task<ImplementationInfo?> SelectBestImplementationAsync(AIContext context)
        {
            // For now, just return the first available implementation
            // TODO: Implement a more sophisticated selection mechanism using AI
            var prompt = $@"
You are an expert C# architect. Your task is to select the best implementation of an interface method for a given usage scenario.

Instructions:
- Carefully analyze the provided interface method signature, the call site, and the call context.
- Review the list of available implementations. Each implementation includes the type name and method signature.
- Consider which implementation is most likely used for the scenario, based on business logic, dependencies, and the calling context.
- Output ONLY a valid JSON object with this structure (no markdown, no extra text):

{{
""SelectedImplementation"": ""<FullTypeName of the chosen implementation>"",
""Reasoning"": ""<Your concise justification>"",
""Accuracy"": [""<FullTypeName>"", ...]
}}

Interface Method:
{context.InterfaceMethod}

Call Site:
{context.CallSite}

Call Context:
{context.CallContext}

Available Implementations:
{string.Join("\n", context.AvailableImplementations.Select(i => $"- {i.FullTypeName}: {i.MethodSignature}"))}

REMEMBER: Output ONLY the JSON object, no additional text or formatting.
";
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 30000,
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
            Console.WriteLine("-----------------------------");
            Console.WriteLine(responseJson);
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);
            var aiResponse = responseData.GetProperty("content")[0].GetProperty("text").GetString();

            if (string.IsNullOrEmpty(aiResponse))
                return null;

            // Parse the AI response JSON
            var aiResponseJson = JsonDocument.Parse(aiResponse);
            var selectedTypeName = aiResponseJson.RootElement.GetProperty("SelectedImplementation").GetString();

            // Find the matching implementation
            var selectedImpl = context.AvailableImplementations
                .FirstOrDefault(i => string.Equals(i.FullTypeName, selectedTypeName, StringComparison.OrdinalIgnoreCase));

            return selectedImpl;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Extracts the method implementation from the method symbol
        /// </summary>
        /// <param name="methodSymbol">The method symbol to extract implementation from</param>
        /// <returns>The method implementation as a string, or null if not available</returns>
        public async Task<string?> GetMethodImplementation(IMethodSymbol methodSymbol)
        {
            var methodLocation = methodSymbol.Locations.FirstOrDefault();//TODO: Look into it. Is this always the right one?
            if (methodLocation == null || !methodLocation.IsInSource)
                return null;

            var syntaxTree = await methodLocation.SourceTree.GetRootAsync();
            var methodNode = syntaxTree.FindNode(methodLocation.SourceSpan);
            return methodNode.ToString();
        }

        public async Task<string?> GetCompiledMethodHierarchySummary(string context)
        {
            var prompt = @$"You are a highly skilled C# code assistant. I will provide you with a detailed method call hierarchy along with the full implementation of each method. 

Your task is to produce a **concise, structured, and comprehensive markdown summary** of this codebase that preserves all information needed to understand the **behavior, call flow, and relationships between methods**, but **does not include the full method bodies**.

Instructions:

1. Summarize each method in 10-15 sentences describing:
   - What the method does
   - Its input/output
   - Method metadata (namespace, class, asynchrony, virtual, abstract, public, private etc)
   - Important side effects
   - Any methods it calls (just names, not full bodies)
   - Any data persistance related info
   - any performance related stuff
   - code quality
   - any external packages, dependencies, services
   - basically everything

2. Represent the call hierarchy clearly, e.g., using indentation, bullets, or a tree structure.

3. Do not include unnecessary comments, blank lines, or code unless needed for clarity.

4. Ensure that the summary is sufficient to understand the complete flow, so that in future prompts, only this summary can be sent to answer questions about the program logic.

5. Add any additional notes which should be included to make the context richer.

Here is the code context:
{context}
Return the result as a **structured summary**, ready to be included in future AI prompts.
";

            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 30000,
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
            var aiResponse = responseData.GetProperty("content")[0].GetProperty("text").GetString();

            if (string.IsNullOrEmpty(aiResponse))
                return "Summary generation failed";

            return aiResponse;
        }

        /// <summary>
        /// Creates the analysis prompt for Claude
        /// </summary>
        /// <param name="methodSymbol">The method symbol being analyzed</param>
        /// <param name="methodBody">The obfuscated method implementation</param>
        /// <returns>The formatted prompt string</returns>
        private string CreateAnalysisPrompt(IMethodSymbol methodSymbol, string methodBody, string methodCallJson)
        {
            var jsonTemplate = """
            {
                "Title": "2-3 word summary",
                "BusinessContext": "4-6 non-technical sentences about what this method achieves in business terms, why it matters, and when it is used",
                "TechnicalContext": "4-6 sentences describing the method's purpose, logic, and role in the overall system. Mention dependencies, data flow, or critical considerations",
                "KeyOperations": ["6-7 items each max 8 words highlighting main operations or transformations done by this method"],
                "FlowDiagram": "Convert the JSON method call data into a VALID, comprehensive and abstract Mermaid.js flowchart.
                    STRICT RULES:
                    - Use simple node labels (no spaces, dashes, or special characters in IDs)
                    - Keep it simple - no subgraphs, no complex labels
                    - Use single letters for node IDs (A, B, C...)
                    - Node labels must use only letters, numbers, and spaces - NO periods, parentheses, colons, or special characters
                    - Test each connection as you write it
                    - Exactly ONE diagram declaration: flowchart TD
                    - You MAY use inline CSS class markers appended to node lines (e.g. `:::processNode`, `:::decisionNode`, `:::dataNode`) — but DO NOT define them
                    - Use |labels| for decision branches
                    - Only use --> arrows (no dotted arrows)
                    - Output MUST contain ONLY the flowchart definition, nothing else
                    - Should cover everything in the flow",
                "Parameters": {"paramName": "explanation", "anotherParam": "explanation"},
                "Dependencies": ["list of internal/external libraries, services or methods this depends on"],
                "PerformanceNotes": "Highlight any performance-sensitive logic, caching, or scalability concerns",
                "Conclusion": "Meaningful conclusion in 3-4 sentences",
                "TimeSaved": {
                    "estimateMinutes": "Estimate, in minutes but as a string, how much time a senior developer would save by reading your generated report instead of manually analyzing the code.
                                        - Base your answer on the provided below method implementation, flow data depth, and their cyclomatic complexity. 
                                        - Give the best case estimate. 
                                        - Provide estimate between 1 minute to 120 minutes."
                }
            }
            """;

            return $"""
                You are analyzing a C# method for a code visualization tool. Create a detailed analysis in JSON format.DO NOT wrap in markdown code blocks. DO NOT use ``` markers. DO NOT add any explanatory text.

                CRITICAL: Your response must be ONLY valid JSON. Do not include any explanatory text or markdown.

                The response should follow this exact JSON structure:
                {jsonTemplate}

                METHOD TO ANALYZE:
                Signature: {methodSymbol}
                Implementation:
                {methodBody}

                Flow Data:
                {methodCallJson}

                REMEMBER: Return ONLY the JSON object, no additional text or formatting.
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
                max_tokens = 30000,
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
            var aiResponse = responseData.GetProperty("content")[0].GetProperty("text").GetString();

            if (string.IsNullOrEmpty(aiResponse))
                return "Summary generation failed";

            try
            {
                // Parse the AI response into our model
                var summaryResponse = JsonSerializer.Deserialize<MethodSummaryResponse>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // helps with camelCase vs PascalCase
                });
                return GenerateHtmlFromFile(summaryResponse, "wwwroot/pdf-template.html");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing AI response: {ex.Message}");
                return "Error parsing AI response";
            }
        }

        public async Task<string> GenerateAnswerWithClaude(string prompt)
        {
            // Create the request payload for Claude
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 30000,
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
            var aiResponse = responseData.GetProperty("content")[0].GetProperty("text").GetString();

            if (string.IsNullOrEmpty(aiResponse))
                return "Summary generation failed";

            return aiResponse;
        }

        public async Task<string> CreateDraftPage(string htmlContent)
        {
            string confluenceUrl = $"https://{_domain}/wiki/rest/api/content";
            using var client = new HttpClient();
            // Basic Auth
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_email}:{_apiToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Payload
            var payload = new
            {
                type = "page",
                title = "ManDrill Testing",
                space = new { key = "Mavericks" },
                status = "draft",
                ancestors = new[]
                {
                    new { id = 6844416004 } // Set parent page ID here
                },
                body = new
                {
                    storage = new
                    {
                        value = htmlContent,
                        representation = "storage"
                    }
                }
            };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            string jsonPayload = JsonSerializer.Serialize(payload, options);
            var response = await client.PostAsync(confluenceUrl, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create draft page: {error}");
            }

            // Parse the response to get the page URL
            string responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            string relativeLink = doc.RootElement.GetProperty("_links").GetProperty("webui").GetString();
            string fullPageUrl = $"https://{_domain}/wiki{relativeLink}";

            Console.WriteLine("Draft Confluence page created at: " + fullPageUrl);

            return fullPageUrl;

        }

        public static string GenerateHtmlFromFile(MethodSummaryResponse model, string templatePath)
        {
            // Read template from file
            string template = File.ReadAllText(templatePath);
            // Replace simple placeholders
            template = template.Replace("[Title]", model.Title ?? "")
                               .Replace("[BusinessContext]", model.BusinessContext ?? "")
                               .Replace("[TechnicalContext]", model.TechnicalContext ?? "")
                               .Replace("[FlowDiagram]", model.FlowDiagram ?? "")
                               .Replace("[PerformanceNotes]", model.PerformanceNotes ?? "")
                               .Replace("[Conclusion]", model.Conclusion ?? "")
                               .Replace("[EstimatedMinutes]", model.TimeSaved.EstimateMinutes.ToString());

            // Build KeyOperations
            var keyOps = new StringBuilder();
            foreach (var op in model.KeyOperations)
            {
                keyOps.AppendLine($"<li>{op}</li>");
            }
            template = template.Replace("[KeyOperations]", keyOps.ToString());
            // Build Parameters table rows
            var paramRows = new StringBuilder();
            foreach (var param in model.Parameters)
            {
                paramRows.AppendLine($"<tr><td>{param.Key}</td><td>{param.Value}</td></tr>");
            }
            template = template.Replace("[Parameters]", paramRows.ToString());
            // Build Dependencies
            var deps = new StringBuilder();
            foreach (var dep in model.Dependencies)
            {
                deps.AppendLine($"<li>{dep}</li>");
            }
            template = template.Replace("[Dependencies]", deps.ToString());
            return template;
        }
        #endregion
    }
}
