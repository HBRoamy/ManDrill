using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManDrill.Client.Models;
using Microsoft.CodeAnalysis;

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
    public static string ConvertMethodCallJsonToDrawflow(string methodCallJson, string solutionPath)
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
            //var root = JsonSerializer.Deserialize<MethodCallTree>(methodCallJson);

            if (root == null)
            {
                return CreateEmptyDrawflow();
            }

            // 2. Create nodes from method calls
            var nodes = new Dictionary<int, object>();
            int nodeCounter = 1;

            CreateNodesRecursively(root, nodes, ref nodeCounter, 300, 150);

            var helpSection = @"
            <div class='card border-info' style='max-width: 350px; font-size: 0.9rem;'>
                <div class='card-body p-3'>
                    <div class='mb-3'>
                        <div class='d-flex align-items-center mb-2'>
                            <i class='fas fa-arrow-right text-primary me-2'></i>
                            <strong class='text-primary'>Left to Right Flow</strong>
                        </div>
                        <p class='mb-0 ms-4 text-light small'>
                            Represents parent-to-child method calls.
                        </p>
                    </div>
                    
                    <div class='mb-3'>
                        <div class='d-flex align-items-center mb-2'>
                            <i class='fas fa-arrow-down text-info me-2'></i>
                            <strong class='text-info'>Top to Bottom Flow</strong>
                        </div>
                        <p class='mb-0 ms-4 text-light small'>
                            Shows order of sibling method calls.
                        </p>
                    </div>
                    
                    <div class='mb-3'>
                        <div class='d-flex align-items-center mb-2'>
                            <div class='border border-warning rounded px-2 py-1 me-2' 
                                 style='background: rgba(255, 193, 7, 0.1);'>
                                <i class='fas fa-syringe text-warning' style='font-size: 0.75rem;'></i>
                            </div>
                            <strong class='text-warning'>DI Resolved</strong>
                        </div>
                        <p class='mb-0 ms-4 text-light small'>
                            Yellow borders indicate methods resolved via dependency injection.
                        </p>
                    </div>

                    <div class='mb-0'>
                        <div class='d-flex align-items-center mb-2'>
                            <strong class='text-info'>Tips</strong>
                        </div>
                        <p class='mb-0 ms-4 text-light small'>
                            1. Right-click any node and click X to close it.
                            </br>
                            2. Click the bottom of any node to see additional details.
                        </p>
                    </div>
                </div>
            </div>";
            if (!string.IsNullOrWhiteSpace(helpSection))
            {

                int summaryNodeId = nodeCounter;
                var summaryNode = new
                {
                    id = summaryNodeId,
                    name = "Flow Info",
                    data = new
                    {
                        info = helpSection
                    },
                    @class = "summary-node",
                    // Wrap summary in a scrollable div to prevent overflow
                    html = $@"
<div class='df-node custom-node bg-dark text-light border border-secondary rounded'>
    <div class='df-node-title bg-secondary text-light fw-bold p-2 rounded-top'>
        Flow Info
    </div>
    <div class='df-node-body p-2'>
        <div class='df-node-field' 
             style='max-width:300px; white-space:normal; word-wrap:break-word; overflow:visible;'>
            {helpSection}
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
                    pos_x = -100,
                    pos_y = -100
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
            CreateNodesRecursively(child, nodes, ref nodeCounter, x + 450, y + (childNodes.Count * 150), currentNodeId, "output_1");
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
        var collapseId = $"collapse_{currentNodeId}";

        // Start node wrapper
        var resolvedFromStyle = !string.IsNullOrWhiteSpace(method.ResolvedFrom) ? "border border-warning border-5 rounded" : "";
        sb.Append($"<div class='df-node custom-node {resolvedFromStyle}' style='position: relative;'>");

        // ───── Title ─────
        sb.Append("<div class='df-node-title text-light'>");
        sb.Append($@"{method.Name + "(..)" ?? "Unknown"}");
        sb.Append("</div>");

        // ───── Node Body ─────
        sb.Append("<div class='df-node-body'>");
        sb.Append($@"
    <div class='df-node-field'>
        <span>Class Name &nbsp;</span>
        <span class='text-warning ms-1'>{method.ClassName}</span>
    </div>
    <div class='df-node-field'>
        <span>Namespace</span>
        <span class='text-info ms-1'>{method.Namespace}</span>
    </div>
");

        // ───── Bottom Toggle Border ─────
        sb.Append($@"
    <div data-bs-toggle='collapse'
         data-bs-target='#{collapseId}'
         role='button'
         aria-expanded='false'
         aria-controls='{collapseId}'
         style='position: absolute; bottom: 0; left: 0; right: 0;
                height: 17px; background-color: transparent; border-radius: 0 0 8px 8px;
                text-align: center; font-size: 10px; color: #ccc; cursor: pointer;
                line-height: 15px; border-top: 1px solid #1e1e1e;'>
        <i class='fas fa-chevron-down' style='transition: transform 0.2s;'></i>
    </div>
");

        // ───── Collapse Content ─────
        sb.Append($@"
    <div id='{collapseId}' class='collapse'
         style='position: absolute; top: 100%; left: 0; right: 0; z-index: 10;
                background-color: #1e1e1e; border-radius: 0 0 8px 8px;'>
        <div style='padding: 0.75rem; border-top: 1px solid #333;'>
            <div class='df-node-field'>
                <span>Return Type &nbsp;</span>
                <span class='text-primary ms-1'>{method.ReturnType}</span>
            </div>
            <div class='df-node-field'>
                <span>Parameters &nbsp;</span>
                <span class='text-info ms-1'>{method.ParamsInfo}</span>
            </div>
            <div class='df-node-field'>
                <span>Resolved From &nbsp;</span>
                <span class='text-info ms-1'>{method.ResolvedFrom ?? ""}</span>
            </div>
        </div>
    </div>
");

        sb.Append("</div>"); // Close df-node-body
        sb.Append("</div>"); // Close df-node


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
            pos_x = x,
            pos_y = y
        };

        nodes[currentNodeId] = node;
    }

    /// <summary>
    /// Creates a comprehensive Mermaid graph TD for C# solution and project relationships
    /// </summary>
    /// <param name="solution">The C# solution object</param>
    /// <returns>Mermaid graph TD string representation</returns>
    public static string CreateSolutionProjectDependencyGraph(Solution solution)
    {
        if (solution == null)
        {
            return "graph TD\n    A[No solution provided]";
        }

        var mermaid = new StringBuilder();
        mermaid.AppendLine("graph TD");

        // Add styling for different project types

        // Enhanced styling with better visual hierarchy
        mermaid.AppendLine("    classDef libraryClass fill:#1e1e1e,stroke:#2e7d32,stroke-width:2px,color:#fff");
        mermaid.AppendLine("    classDef executableClass fill:#1e1e1e,stroke:#4FC3F7,stroke-width:2px,color:#fff");
        mermaid.AppendLine("    classDef testClass fill:#1e1e1e,stroke:#00BCD4,stroke-width:2px,color:#fff");
        mermaid.AppendLine("    classDef webClass fill:#1e1e1e,stroke:#26A69A,stroke-width:2px,color:#fff");
        mermaid.AppendLine("    classDef solutionClass fill:#1e1e1e,stroke:#ffc107,stroke-width:3px,color:#fff");
        mermaid.AppendLine("    classDef namespaceClass fill:#1e1e1e,stroke:#80DEEA,stroke-width:1px,stroke-dasharray:5 5");

        // Layout directives to reduce clutter
        mermaid.AppendLine("    linkStyle default stroke:#666,stroke-width:1px");

        var declaredNodes = new HashSet<string>();
        var namespaceGroups = new Dictionary<string, List<ProjectInfo>>();
        var projectInfoMap = new Dictionary<string, ProjectInfo>();

        // First pass: collect all projects and organize by namespace
        foreach (var project in solution.Projects)
        {
            var projectInfo = new ProjectInfo
            {
                Project = project,
                NodeId = Sanitize(project.Name),
                FullName = project.Name,
                Namespace = ExtractNamespace(project.Name),
                ShortName = ExtractShortName(project.Name),
                ProjectType = DetermineProjectType(project),
                ReferenceCount = project.ProjectReferences.Count(),
                MetadataReferences = project.MetadataReferences.Count()
            };

            projectInfoMap[projectInfo.NodeId] = projectInfo;

            var ns = projectInfo.Namespace;
            if (!namespaceGroups.ContainsKey(ns))
                namespaceGroups[ns] = new List<ProjectInfo>();

            namespaceGroups[ns].Add(projectInfo);
            declaredNodes.Add(projectInfo.NodeId);
        }

        // Add solution node
        var solutionNodeId = Sanitize(solution.FilePath ?? "Solution");
        mermaid.AppendLine($"    {solutionNodeId}[\"Solution<br/>📁 {ExtractFileName(solution.FilePath ?? "Unknown")}<br/>Projects: {solution.Projects.Count()}\"]");
        mermaid.AppendLine($"    class {solutionNodeId} solutionClass");

        // Second pass: build the graph structure with enhanced organization
        foreach (var namespaceGroup in namespaceGroups.OrderBy(kvp => kvp.Key))
        {
            var namespaceName = namespaceGroup.Key;
            var projects = namespaceGroup.Value.OrderBy(p => p.ShortName).ToList();

            if (string.IsNullOrEmpty(namespaceName))
            {
                // Projects without namespace - add directly
                foreach (var projectInfo in projects)
                {
                    AddProjectNode(projectInfo, mermaid);
                    mermaid.AppendLine($"    {solutionNodeId} --> {projectInfo.NodeId}");
                }
            }
            else
            {
                // Create namespace subgraph
                var namespaceId = Sanitize(namespaceName);
                mermaid.AppendLine($"    subgraph {namespaceId}[\"{namespaceName}\"]");
                
                foreach (var projectInfo in projects)
                {
                    AddProjectNode(projectInfo, mermaid);
                }
                
                mermaid.AppendLine("    end");
                
                // Connect solution to namespace
                mermaid.AppendLine($"    {solutionNodeId} --> {namespaceId}");
            }
        }

        // Third pass: add project dependencies
        foreach (var projectInfo in projectInfoMap.Values)
        {
            foreach (var reference in projectInfo.Project.ProjectReferences)
            {
                var referencedProject = solution.GetProject(reference.ProjectId);
                if (referencedProject != null)
                {
                    var referencedNodeId = Sanitize(referencedProject.Name);
                    if (declaredNodes.Contains(referencedNodeId))
                    {
                        mermaid.AppendLine($"    {projectInfo.NodeId} --> {referencedNodeId}");
                    }
                }
            }
        }

        return mermaid.ToString();
    }

    /// <summary>
    /// Legacy method - kept for backward compatibility
    /// </summary>
    public static string CreateProjectsDependencyFlow(Solution solution)
    {
        return CreateSolutionProjectDependencyGraph(solution);
    }

    public static string CreateMethodCallSequenceDiagram(string methodCallJson)
    {
        if (string.IsNullOrEmpty(methodCallJson))
        {
            return "graph TD\n    A[No method calls found]";
        }

        try
        {
            var root = JsonSerializer.Deserialize<MethodCallInfo>(methodCallJson);
            if (root == null)
            {
                return "graph TD\n    A[No method calls found]";
            }

            var mermaid = new StringBuilder();
            mermaid.AppendLine("graph TD");
            
            var nodeMap = new Dictionary<string, string>();
            var nodeCounter = 1;
            
            // Generate graph TD recursively
            GenerateGraphTDRecursively(root, mermaid, nodeMap, ref nodeCounter, 0);
            
            return mermaid.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating method call graph: {ex.Message}");
            return "graph TD\n    A[Error generating method call graph]";
        }
    }

    private static void GenerateGraphTDRecursively(MethodCallInfo method, StringBuilder mermaid, 
        Dictionary<string, string> nodeMap, ref int nodeCounter, int depth)
    {
        // Limit depth to prevent massive diagrams
        if (depth > 10)
        {
            mermaid.AppendLine($"    Truncated[Truncated at depth {depth}]");
            return;
        }

        // Create unique node ID for this method
        var methodKey = $"{method.ClassName}.{method.Name}";
        if (!nodeMap.ContainsKey(methodKey))
        {
            var nodeId = $"N{nodeCounter++}";
            nodeMap[methodKey] = nodeId;
            
            // Create node with method details
            var methodName = method.Name ?? "Unknown";
            var className = method.ClassName ?? "Unknown";
            var paramsInfo = string.IsNullOrEmpty(method.ParamsInfo) ? "" : $"({method.ParamsInfo})";
            var returnType = string.IsNullOrEmpty(method.ReturnType) ? "void" : method.ReturnType;
            
            mermaid.AppendLine($"    {nodeId}[\"{className}.{methodName}{paramsInfo}<br/>Returns: {returnType}\"]");
        }

        var currentNodeId = nodeMap[methodKey];
        
        // Process internal calls
        if (method.InternalCalls != null && method.InternalCalls.Any())
        {
            foreach (var childCall in method.InternalCalls)
            {
                // Create unique node ID for child method
                var childMethodKey = $"{childCall.ClassName}.{childCall.Name}";
                if (!nodeMap.ContainsKey(childMethodKey))
                {
                    var newChildNodeId = $"N{nodeCounter++}";
                    nodeMap[childMethodKey] = newChildNodeId;
                    
                    // Create child node
                    var childMethodName = childCall.Name ?? "Unknown";
                    var childClassName = childCall.ClassName ?? "Unknown";
                    var childParamsInfo = string.IsNullOrEmpty(childCall.ParamsInfo) ? "" : $"({childCall.ParamsInfo})";
                    var childReturnType = string.IsNullOrEmpty(childCall.ReturnType) ? "void" : childCall.ReturnType;
                    
                    mermaid.AppendLine($"    {newChildNodeId}[\"{childClassName}.{childMethodName}{childParamsInfo}<br/>Returns: {childReturnType}\"]");
                }

                var currentChildNodeId = nodeMap[childMethodKey];
                
                // Create edge from current method to child method
                mermaid.AppendLine($"    {currentNodeId} --> {currentChildNodeId}");
                
                // Recursively process child calls
                GenerateGraphTDRecursively(childCall, mermaid, nodeMap, ref nodeCounter, depth + 1);
            }
        }
    }

    static string Sanitize(string name)
    {
        var sanitized = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sanitized.Append(ch);
            else
                sanitized.Append('_');
        }
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized.Insert(0, '_');
        return sanitized.ToString();
    }

    #region Helper Classes and Methods for Solution Project Graph

    /// <summary>
    /// Represents project information for graph generation
    /// </summary>
    private class ProjectInfo
    {
        public Project Project { get; set; }
        public string NodeId { get; set; }
        public string FullName { get; set; }
        public string Namespace { get; set; }
        public string ShortName { get; set; }
        public ProjectType ProjectType { get; set; }
        public int ReferenceCount { get; set; }
        public int MetadataReferences { get; set; }
    }

    /// <summary>
    /// Project type enumeration for styling
    /// </summary>
    private enum ProjectType
    {
        Library,
        Executable,
        Test,
        Web,
        Unknown
    }

    /// <summary>
    /// Extracts namespace from project name
    /// </summary>
    private static string ExtractNamespace(string projectName)
    {
        var lastDot = projectName.LastIndexOf('.');
        if (lastDot == -1)
            return string.Empty;
        return projectName.Substring(0, lastDot);
    }

    /// <summary>
    /// Extracts short name from project name
    /// </summary>
    private static string ExtractShortName(string projectName)
    {
        var lastDot = projectName.LastIndexOf('.');
        if (lastDot == -1)
            return projectName;
        return projectName.Substring(lastDot + 1);
    }

    /// <summary>
    /// Extracts file name from file path
    /// </summary>
    private static string ExtractFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "Unknown";
        
        var lastSlash = Math.Max(filePath.LastIndexOf('\\'), filePath.LastIndexOf('/'));
        if (lastSlash == -1)
            return filePath;
        
        return filePath.Substring(lastSlash + 1);
    }

    /// <summary>
    /// Determines project type based on project properties
    /// </summary>
    private static ProjectType DetermineProjectType(Project project)
    {
        var projectName = project.Name.ToLower();
        var assemblyName = project.AssemblyName?.ToLower() ?? "";
        
        if (projectName.Contains("test") || projectName.Contains("spec") || assemblyName.Contains("test"))
            return ProjectType.Test;
        
        if (projectName.Contains("web") || projectName.Contains("api") || projectName.Contains("mvc") || 
            projectName.Contains("razor") || projectName.Contains("blazor"))
            return ProjectType.Web;
        
        if (projectName.Contains("exe") || projectName.Contains("console") || projectName.Contains("app"))
            return ProjectType.Executable;
        
        return ProjectType.Library;
    }

    /// <summary>
    /// Adds a project node to the Mermaid graph with appropriate styling
    /// </summary>
    private static void AddProjectNode(ProjectInfo projectInfo, StringBuilder mermaid)
    {
        var icon = GetProjectIcon(projectInfo.ProjectType);
        var className = GetProjectClassName(projectInfo.ProjectType);
        
        var nodeLabel = $"{icon} {projectInfo.ShortName}<br/>" +
                       $"📦 {projectInfo.ReferenceCount} refs<br/>" +
                       $"🔗 {projectInfo.MetadataReferences} metadata";
        
        mermaid.AppendLine($"    {projectInfo.NodeId}[\"{nodeLabel}\"]");
        mermaid.AppendLine($"    class {projectInfo.NodeId} {className}");
    }

    /// <summary>
    /// Gets appropriate icon for project type
    /// </summary>
    private static string GetProjectIcon(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.Library => "📚",
            ProjectType.Executable => "⚙️",
            ProjectType.Test => "🧪",
            ProjectType.Web => "🌐",
            _ => "📁"
        };
    }

    /// <summary>
    /// Gets appropriate CSS class name for project type
    /// </summary>
    private static string GetProjectClassName(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.Library => "libraryClass",
            ProjectType.Executable => "executableClass",
            ProjectType.Test => "testClass",
            ProjectType.Web => "webClass",
            _ => "libraryClass"
        };
    }

    #endregion
}