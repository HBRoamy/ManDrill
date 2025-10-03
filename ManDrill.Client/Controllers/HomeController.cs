using ManDrill.Client.Helpers;
using ManDrill.Client.Models;
using ManDrill.Client.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ManDrill.Client.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHubContext<ProgressHub> _hub;
        private readonly IMemoryCache _cache;

        public HomeController(IHubContext<ProgressHub> hub, IMemoryCache cache)
        {
            _hub = hub;
            _cache = cache;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new AnalyzerViewModel
            {
                SelectedOverload = 1,
                JsonOutput = "{}"
            });
        }

        [HttpPost]
        public async Task<IActionResult> Index(string solutionPath, string namespaceName, string className, string methodNameInput, int overloadNumber = 1, bool includeAISummary = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(className);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodNameInput);

            if(overloadNumber<1)
            {
                overloadNumber = 1;
            }

            solutionPath = solutionPath.Trim('"');
            if (!_cache.TryGetValue<Solution>(solutionPath, out var currentLoadedSoln) || currentLoadedSoln == null)
            {
                currentLoadedSoln = await LoadSolution(solutionPath);
            }

            await _hub.Clients.All.SendAsync("ReportProgress", "Analyzing projects...", 20);

            var overloads = new List<IMethodSymbol>();
            int total = currentLoadedSoln.Projects.Count();
            int done = 0;
            foreach (var project in currentLoadedSoln.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var ns in compilation.GlobalNamespace.GetNamespaceMembersRecursive())
                {
                    if (ns.ToDisplayString() == namespaceName)
                    {
                        foreach (var type in ns.GetTypeMembersRecursive())
                        {
                            var simpleTypeName = type.Name.Split('`')[0];
                            if (simpleTypeName == className)//handles the partial classes as well
                            {
                                overloads.AddRange(type.GetMembers()
                                    .OfType<IMethodSymbol>()
                                    .Where(m => m.Name == methodNameInput));
                            }
                        }
                    }
                }

                done++;
                var percent = 20 + (int)(60.0 * done / total);
                await _hub.Clients.All.SendAsync("ReportProgress", $"Processed {done}/{total} projects", percent);
            }

            if (overloads.Count <= 0 || overloadNumber > overloads.Count)
            {
                throw new Exception("Method not found");
            }

            var methodSymbol = overloads[overloadNumber-1];
            var extractor = new MethodCallExtractor(currentLoadedSoln);
            var rootInfo = await extractor.ExtractAsync(methodSymbol);

            string json = JsonSerializer.Serialize(rootInfo, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            
            // Get dependency index items from the extractor
            await _hub.Clients.All.SendAsync("ReportProgress", "Generating dependency index...", 90);
            var dependencyIndexItems = extractor.GetDependencyIndexItems();
            var paths = await extractor.ExtractSuperAncestorsAsync(methodSymbol);

            var ancestorPaths = paths.Select(path => path.Select(m => $"{m.ContainingType.Name}.{m.Name}").ToList())
                        .GroupBy(list => list[^1])
                        .Select(group => group.First())
                        .ToList();
            string chatBotContext = string.Empty;
            if (true)//based on user preference
            {
                var contextBuilderInput = json + await BuildMethodBodiesStringSimplified(rootInfo);
                chatBotContext = contextBuilderInput; // await(new AIService()).GetCompiledMethodHierarchySummary(contextBuilderInput) ??;
            }
            await _hub.Clients.All.SendAsync("ReportProgress", "Generating method summary using AI...", 95);
            var methodSummary = includeAISummary ? await (new AIService()).GenerateMethodSummary(methodSymbol, json) : null;
            await _hub.Clients.All.SendAsync("ReportProgress", "Embedded Response.", 100);
            return View(new AnalyzerViewModel
            {
                SolutionPath = solutionPath,
                NamespaceName = namespaceName,
                ClassName = className,
                MethodName = methodNameInput,
                AISummary = methodSummary ?? string.Empty,
                JsonOutput = DrawCallMapper.ConvertMethodCallJsonToDrawflow(json, solutionPath) ?? "{}",
                IncludeAISummary = includeAISummary,
                ProjectDependencyDiagram = DrawCallMapper.CreateProjectsDependencyFlow(currentLoadedSoln) ?? "",
                MethodSequenceDiagram = DrawCallMapper.CreateMethodCallSequenceDiagram(json) ?? "",
                //Diagrams = [
                //    new DiagramDetails() {
                //        Name = "Interactive Flow Diagram",
                //        DiagramInputData = DrawCallMapper.ConvertMethodCallJsonToDrawflow(json, solutionPath) ?? "{}",
                //        DiagramPartialViewName = "_DrawFlowChartPartial"
                //    },
                //    new DiagramDetails() {
                //        Name = "Method Call Flow",
                //        DiagramInputData = DrawCallMapper.CreateMethodCallSequenceDiagram(json) ?? "",
                //        DiagramPartialViewName = "_MermaidChartPartial"
                //    },
                //    new DiagramDetails() {
                //        Name = "Project Topology",
                //        DiagramInputData = DrawCallMapper.CreateProjectsDependencyFlow(currentLoadedSoln) ?? "",
                //        DiagramPartialViewName = "_MermaidChartPartial"
                //    },
                //    new DiagramDetails() {
                //        Name = "Method Dependencies",
                //        DiagramInputData = dependencyIndexItems,
                //        DiagramPartialViewName = "_DependencyIndexTablePartial",
                //        AdditionalTitleAttributes = dependencyIndexItems.Count.ToString()
                //    }
                //],
                DependencyIndexItems = dependencyIndexItems,
                Ancestors = ancestorPaths,
                ChatBotContext = chatBotContext
            });
        }

        // Simplified version using the stored symbol
        public async Task<string> BuildMethodBodiesStringSimplified(MethodCallInfo rootMethod)
        {
            var sb = new StringBuilder();
            await BuildMethodBodiesRecursiveSimplified(rootMethod, sb, 0, new());
            return sb.ToString();
        }

        private async Task BuildMethodBodiesRecursiveSimplified(MethodCallInfo methodCall, StringBuilder sb, int indentLevel, AIService aIService)
        {
            string indent = new string(' ', indentLevel * 2);

            // Method header
            sb.AppendLine($"{indent}=== {methodCall.Namespace}.{methodCall.ClassName}.{methodCall.Name} ===");
            sb.AppendLine($"{indent}Signature: {methodCall.ReturnType} {methodCall.Name}({methodCall.ParamsInfo})");

            if (!string.IsNullOrEmpty(methodCall.ResolvedFrom))
            {
                sb.AppendLine($"{indent}Interface: {methodCall.ResolvedFrom}");
            }

            sb.AppendLine();

            // Method implementation
            if (methodCall.MethodSymbol != null)
            {
                var implementation = await aIService.GetMethodImplementation(methodCall.MethodSymbol);
                if (!string.IsNullOrEmpty(implementation))
                {
                    sb.AppendLine($"{indent}Implementation:");
                    var implementationLines = implementation.Split('\n');
                    foreach (var line in implementationLines)
                    {
                        sb.AppendLine($"{indent}{line.TrimEnd()}");
                    }
                }
                else
                {
                    sb.AppendLine($"{indent}// Implementation not available in source");
                }
            }
            else
            {
                sb.AppendLine($"{indent}// Method symbol not available");
            }

            sb.AppendLine();

            // Process internal calls
            if (methodCall.InternalCalls != null && methodCall.InternalCalls.Any())
            {
                sb.AppendLine($"{indent}--- Called Methods ---");
                foreach (var internalCall in methodCall.InternalCalls)
                {
                    await BuildMethodBodiesRecursiveSimplified(internalCall, sb, indentLevel + 1, aIService);
                }
            }
        }

        private async Task<Solution> LoadSolution(string solutionPath)
        {
            await _hub.Clients.All.SendAsync("ReportProgress", "Opening solution...", 20);

            using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
                {
                    {"AlwaysCompileMarkupFilesInSeparateDomain", "true"}
                });

            workspace.WorkspaceFailed += (o, e) => Console.WriteLine($"[Workspace Diagnostic] {e.Diagnostic.Message}");

            var loadedSolution = await workspace.OpenSolutionAsync(solutionPath);
            _cache.Set(solutionPath, loadedSolution, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30)
            });

            await _hub.Clients.All.SendAsync("ReportProgress", "Loaded solution.", 100);
            return loadedSolution;
        }

        [HttpPost]
        public async Task<string> CreateDraftPage([FromBody] HtmlRequestModel model)
        {
            try
            {
                var url = await (new AIService()).CreateDraftPage(model.HtmlContent);
                return url;
            }
            catch(Exception)
            {
                return null;
            }
        }
    }
}