using System.Diagnostics;
using System.Text.Json;
using ManDrill.Client.Models;
using ManDrill.Client.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis;
using ManDrill.Client.Helpers;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using Microsoft.Extensions.Caching.Memory;

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
                Overloads = [],
                SelectedOverload = 0,
                JsonOutput = "{}"
            });
        }

        [HttpPost]
        public async Task<IActionResult> Index(string solutionPath, string namespaceName, string className, string methodNameInput, bool includeAISummary = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(className);
            ArgumentException.ThrowIfNullOrWhiteSpace(methodNameInput);

            solutionPath = solutionPath.Trim('"');
            if (!_cache.TryGetValue<Solution>(solutionPath, out var currentLoadedSoln) || currentLoadedSoln == null)
            {
                currentLoadedSoln = await LoadSolution(solutionPath);
            }

            await _hub.Clients.All.SendAsync("ReportProgress", "Analyzing projects…", 40);

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
                            if (simpleTypeName == className)
                            {
                                overloads.AddRange(type.GetMembers()
                                    .OfType<IMethodSymbol>()
                                    .Where(m => m.Name == methodNameInput));
                            }
                        }
                    }
                }

                done++;
                var percent = 40 + (int)(60.0 * done / total);
                await _hub.Clients.All.SendAsync("ReportProgress", $"Processed {done}/{total} projects", percent);
            }

            if (overloads.Count == 0)
            {
                throw new Exception("Method not found");
            }

            var methodSymbol = overloads[0];
            var extractor = new MethodCallExtractor(currentLoadedSoln);
            var rootInfo = await extractor.ExtractAsync(methodSymbol);

            string json = JsonSerializer.Serialize(rootInfo, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var methodSummary = includeAISummary ? await (new AIService()).GenerateMethodSummary(methodSymbol) : null;
            return View(new AnalyzerViewModel
            {
                SolutionPath = solutionPath,
                NamespaceName = namespaceName,
                ClassName = className,
                MethodName = methodNameInput,
                //Overloads are currently not functional and not doing anything.
                Overloads = overloads.Select((m, i) => new OverloadInfo
                {
                    Index = i + 1,
                    Signature = $"{m.ReturnType} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))})"
                }).ToList() ?? [],
                SelectedOverload = 1,
                JsonOutput = DrawCallMapper.ConvertMethodCallJsonToDrawflow(json, methodSummary, solutionPath) ?? "{}"
            });
        }

        private async Task<Solution> LoadSolution(string solutionPath)
        {
            await _hub.Clients.All.SendAsync("ReportProgress", "Opening solution…", 20);

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
    }
}