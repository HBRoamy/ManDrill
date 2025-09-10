using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManDrill.Client.Models;

public class DrawflowNode
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Data { get; set; }
    public List<string> Outputs { get; set; } = new();
}

public class DrawflowRoot
{
    [JsonPropertyName("Home")]
    public HomeData Home { get; set; }
}

public class HomeData
{
    public object data { get; set; }
}


public static class DrawCallMapper
{
    private static string _solutionPath { get; set; }
    public static string ConvertMethodCallJsonToDrawflow(string methodCallJson, string? summary, string solutionPath)
    {
        if (string.IsNullOrEmpty(methodCallJson))
        {
            return CreateEmptyDrawflow();
        }
        _solutionPath = solutionPath;
        try
        {
            // 1. Deserialize into a single MethodCallInfo
            var root = JsonSerializer.Deserialize<MethodCallInfo>(methodCallJson);

            if (root == null)
            {
                return CreateEmptyDrawflow();
            }

            // 2. Create nodes from method calls
            var nodes = new Dictionary<int, object>();
            int nodeCounter = 1;

            CreateNodesRecursively(root, nodes, ref nodeCounter, 300, 150);
            Console.WriteLine(summary);
            // 3. Add summary node (not connected)
            if (!string.IsNullOrWhiteSpace(summary))
            {
                // Replace the summaryNode creation block inside ConvertMethodCallJsonToDrawflow with the following:

                int summaryNodeId = nodeCounter;
                var summaryNode = new
                {
                    id = summaryNodeId,
                    name = "Summary",
                    data = new
                    {
                        info = summary
                    },
                    @class = "summary-node",
                    // Wrap summary in a scrollable div to prevent overflow
                    html = $@"
<div class='df-node custom-node bg-dark text-light border border-secondary rounded'>
    <div class='df-node-title bg-secondary text-light fw-bold p-2 rounded-top'>
        Summary
    </div>
    <div class='df-node-body p-2'>
        <div class='df-node-field' 
             style='max-width:300px; white-space:normal; word-wrap:break-word; overflow:visible;'>
            {summary}
        </div>
    </div>
</div>",
                    typenode = false,
                    inputs = new
                    {
                        input_1 = new
                        {
                            connections = new List<object>()
                        }
                    },
                    outputs = new
                    {
                        output_1 = new
                        {
                            connections = new List<object>()
                        }
                    },
                    pos_x = 1000,
                    pos_y = 20
                };
                nodes[summaryNodeId] = summaryNode;
                nodeCounter++;
            }

            // 4. Create proper Drawflow structure
            var drawflowStructure = new
            {
                drawflow = new DrawflowRoot
                {
                    Home = new HomeData
                    {
                        data = nodes
                    }
                }
            };

            return JsonSerializer.Serialize(drawflowStructure, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting to Drawflow: {ex.Message}");
            return CreateEmptyDrawflow();
        }
    }

    private static string CreateEmptyDrawflow()
    {
        var emptyStructure = new
        {
            drawflow = new
            {
                Home = new
                {
                    data = new Dictionary<string, object>()
                }
            }
        };

        return JsonSerializer.Serialize(emptyStructure);
    }

    private static void CreateNodesRecursively(MethodCallInfo method, Dictionary<int, object> nodes, ref int nodeCounter, int x, int y, int? parentNodeId = null, string parentOutput = null)
    {
        var currentNodeId = nodeCounter++;

        // Process child methods first to get their IDs
        var childNodes = new List<int>();
        foreach (var child in method.InternalCalls ?? new List<MethodCallInfo>())
        {
            var childId = nodeCounter;
            childNodes.Add(childId);
            CreateNodesRecursively(child, nodes, ref nodeCounter, x + 300, y + (childNodes.Count * 150), currentNodeId, "output_1");
        }

        // Create input connections (reference to parent)
        var inputConnections = new List<object>();
        if (parentNodeId.HasValue && !string.IsNullOrEmpty(parentOutput))
        {
            inputConnections.Add(new
            {
                node = parentNodeId.Value.ToString(),
                input = parentOutput  // Reference parent's output
            });
        }

        // Create output connections (references to children)
        var outputConnections = childNodes.Select(id => new
        {
            node = id.ToString(),
            input = "input_1" 
        }).ToList();

        var sb = new StringBuilder();

        var formId = $"form_{currentNodeId}";

        sb.Append("<div class='df-node custom-node'>");

        // Method name clickable and submits hidden form
        sb.Append($"<div class='df-node-title text-light'>");
        sb.Append($"<a href='#' class='text-light' onclick=\"document.getElementById('{formId}').submit(); return false;\">{method.Name + "(..)" ?? "Unknown"}</a>");
        sb.Append("</div>");

        // Hidden form for POST
        sb.Append($"<form id='{formId}' method='post' action='/Home/Index' style='display:none;'>");
        sb.Append($"<input type='hidden' name='solutionPath' value='{_solutionPath ?? ""}' />");
        sb.Append($"<input type='hidden' name='namespaceName' value='{method.Namespace ?? ""}' />");
        sb.Append($"<input type='hidden' name='methodNameInput' value='{method.Name ?? ""}' />");
        sb.Append($"<input type='hidden' name='className' value='{method.ClassName ?? ""}' />");
        //sb.Append($"<input type='checkbox' name='includeAISummary' value='true' />");
        sb.Append("</form>");

        // Rest of your node body
        sb.Append("<div class='df-node-body'>");
        sb.Append($"<div class='df-node-field'><span>Class Name &nbsp;</span><span class='text-success'>{method.ClassName}</span></div>");
        sb.Append($"<div class='df-node-field'><span>Namespace &nbsp;</span><span class='text-danger'>{method.Namespace}</span></div>");
        sb.Append($"<div class='df-node-field'><span>Return Type &nbsp;</span><span class='text-primary'>{method.ReturnType}</span></div>");
        sb.Append("</div></div>");

        // Create the node with proper bidirectional connections
        var node = new
        {
            id = currentNodeId,
            name = method.Name ?? "Unknown",
            data = new
            {
                info = $"{method.ReturnType ?? "void"} {method.ClassName}.{method.Name}()"
            },
            @class = "method-node",
            html = sb.ToString(),//$"<div><strong>{method.Name ?? "Unknown"}</strong><br/>{method.ClassName}</div>",
            typenode = false,
            inputs = new
            {
                input_1 = new
                {
                    connections = inputConnections
                }
            },
            outputs = new
            {
                output_1 = new
                {
                    connections = outputConnections
                }
            },
            pos_x =x,
            pos_y =y
        };

        nodes[currentNodeId] = node;
    }

}