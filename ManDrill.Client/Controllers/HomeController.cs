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
        //private DependencyInjectionAnalyzer? _diAnalyzer;

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
        public async Task<IActionResult> Index(string solutionPath, string namespaceName, string className, string methodNameInput, int selectedOverload = 1, bool includeAISummary = false)
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

            //if (_diAnalyzer == null)
            //{
            //    _diAnalyzer = new DependencyInjectionAnalyzer();
            //    await _diAnalyzer.AnalyzeSolutionAsync(currentLoadedSoln, progress =>
            //        _hub.Clients.All.SendAsync("ReportProgress", progress.Message, progress.Percentage));
            //}

            //var operatorMap = new Dictionary<string, string>
            //    {
            //        { "+", "op_Addition" }, { "-", "op_Subtraction" }, { "*", "op_Multiply" }, { "/", "op_Division" },
            //        { "==", "op_Equality" }, { "!=", "op_Inequality" }, { "<", "op_LessThan" }, { ">", "op_GreaterThan" }
            //    };

            //if (operatorMap.TryGetValue(methodNameInput, out string? value))
            //    methodNameInput = value;

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

            selectedOverload = 1;

            if (overloads.Count == 0)
            {
                throw new Exception("Method not found");
            }

            var methodSymbol = overloads[0];
            var extractor = new MethodCallExtractor2(currentLoadedSoln);
            var rootInfo = await extractor.ExtractAsync(methodSymbol);

            string json = JsonSerializer.Serialize(rootInfo, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var methodSummary = includeAISummary ? await (new AIService()).GenerateMethodSummary(methodSymbol) : null;
            return View(new AnalyzerViewModel
            {
                SolutionPath = solutionPath,
                NamespaceName = namespaceName,
                ClassName = className,
                MethodName = methodNameInput,
                Overloads = overloads.Select((m, i) => new OverloadInfo
                {
                    Index = i + 1,
                    Signature = $"{m.ReturnType} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"))})"
                }).ToList() ?? [],
                SelectedOverload = selectedOverload,
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
            //_diAnalyzer = null; // Reset analyzer when solution changes

            await _hub.Clients.All.SendAsync("ReportProgress", "Loaded solution.", 100);
            return loadedSolution;
        }
    }

    #region DI Analysis Implementation
    public class DependencyInjectionAnalyzer
    {
        private readonly ConcurrentDictionary<ITypeSymbol, ITypeSymbol> _diMappings = new(SymbolEqualityComparer.Default);

        public async Task AnalyzeSolutionAsync(Solution solution, Action<(string Message, int Percentage)>? progressReporter = null)
        {
            var projects = solution.Projects.ToList();
            int total = projects.Count;
            int done = 0;

            foreach (var project in projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                AnalyzeProject(compilation);
                done++;

                progressReporter?.Invoke(($"Analyzed DI registrations in {project.Name}",
                    20 + (int)(80.0 * done / total)));
            }
        }

        private void AnalyzeProject(Compilation compilation)
        {
            // 1. Analyze MVC controllers
            AnalyzeControllers(compilation);

            // 2. Analyze DI container registrations
            AnalyzeServiceRegistrations(compilation);
        }

        private void AnalyzeControllers(Compilation compilation)
        {
            var controllerBase = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase");
            if (controllerBase == null) return;

            foreach (var type in compilation.GlobalNamespace.GetAllTypes())
            {
                if (type.InheritsFrom(controllerBase) && !type.IsAbstract)
                {
                    // Map controller to itself
                    _diMappings[type] = type;

                    // Map all implemented interfaces to controller
                    foreach (var iface in type.AllInterfaces)
                    {
                        _diMappings[iface] = type;
                    }
                }
            }
        }

        private void AnalyzeServiceRegistrations(Compilation compilation)
        {
            var serviceCollectionType = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection");
            if (serviceCollectionType == null) return;

            foreach (var type in compilation.GlobalNamespace.GetAllTypes())
            {
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (IsConfigureServicesMethod(method, serviceCollectionType))
                    {
                        AnalyzeMethodRegistrations(method, compilation);
                    }
                }
            }
        }

        private static bool IsConfigureServicesMethod(IMethodSymbol method, ITypeSymbol serviceCollectionType)
        {
            return method.Name.Contains("ConfigureServices", StringComparison.OrdinalIgnoreCase) &&
                   method.Parameters.Length > 0 &&
                   SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, serviceCollectionType);
        }

        private void AnalyzeMethodRegistrations(IMethodSymbol method, Compilation compilation)
        {
            var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (syntax is not MethodDeclarationSyntax methodSyntax) return;

            var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            var registrations = methodSyntax.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => IsServiceRegistrationCall(inv, semanticModel));

            foreach (var registration in registrations)
            {
                AnalyzeRegistration(registration, semanticModel);
            }
        }

        private bool IsServiceRegistrationCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            return symbol?.ContainingType?.Name == "ServiceCollectionServiceExtensions" ||
                   symbol?.Name.StartsWith("Add") == true;
        }

        private void AnalyzeRegistration(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol == null) return;

            var serviceType = GetServiceType(methodSymbol, invocation, semanticModel);
            var implementationType = GetImplementationType(methodSymbol, invocation, semanticModel);

            if (serviceType != null && implementationType != null)
            {
                _diMappings[serviceType] = implementationType;
            }
        }

        private static ITypeSymbol? GetServiceType(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            // Generic method: services.AddScoped<IService, Service>()
            if (method.TypeArguments.Length > 0)
                return method.TypeArguments[0];

            // Non-generic: services.AddScoped(typeof(IService), typeof(Service))
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var argType = semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type;
                if (argType != null && argType.TypeKind != TypeKind.Error)
                    return argType;
            }

            return null;
        }

        private static ITypeSymbol? GetImplementationType(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            // Generic with implementation: services.AddScoped<IService, Service>()
            if (method.TypeArguments.Length > 1)
                return method.TypeArguments[1];

            // Factory method: services.AddScoped(sp => new Service())
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var lastArg = invocation.ArgumentList.Arguments.Last().Expression;

                // Lambda expression
                if (lastArg is SimpleLambdaExpressionSyntax lambda)
                {
                    return GetLambdaReturnType(lambda, semanticModel);
                }
                // Object creation
                else if (lastArg is ObjectCreationExpressionSyntax objectCreation)
                {
                    return semanticModel.GetTypeInfo(objectCreation).Type;
                }
            }

            // Same type registration: services.AddScoped<Service>()
            return method.TypeArguments.Length == 1 ? method.TypeArguments[0] : null;
        }

        private static ITypeSymbol? GetLambdaReturnType(SimpleLambdaExpressionSyntax lambda, SemanticModel semanticModel)
        {
            switch (lambda.Body)
            {
                case ObjectCreationExpressionSyntax creation:
                    return semanticModel.GetTypeInfo(creation).Type;

                case InvocationExpressionSyntax invocation:
                    var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    return symbol?.ReturnType;

                case MemberAccessExpressionSyntax memberAccess:
                    return semanticModel.GetTypeInfo(memberAccess).Type;

                default:
                    return semanticModel.GetTypeInfo(lambda.Body).Type;
            }
        }

        public ITypeSymbol? GetConcreteType(ITypeSymbol serviceType)
        {
            if (_diMappings.TryGetValue(serviceType, out var concreteType))
                return concreteType;

            // Fallback to implementation search
            return FindImplementationInSolution(serviceType);
        }

        private ITypeSymbol? FindImplementationInSolution(ITypeSymbol interfaceType)
        {
            if (interfaceType.TypeKind != TypeKind.Interface)
                return null;

            // Search all projects for implementations
            foreach (var (_, implementation) in _diMappings)
            {
                if (implementation.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))
                    return implementation;
            }

            return null;
        }
    }

    public static class SymbolExtensions
    {
        public static bool InheritsFrom(this ITypeSymbol type, ITypeSymbol baseType)
        {
            while (type != null)
            {
                if (SymbolEqualityComparer.Default.Equals(type, baseType))
                    return true;

                type = type.BaseType;
            }
            return false;
        }

        public static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                yield return type;
            }

            foreach (var nestedNs in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(nestedNs))
                {
                    yield return type;
                }
            }
        }
    }
    #endregion

    #region Enhanced MethodCallExtractor
    public class MethodCallExtractor
    {
        private readonly Solution _solution;
        private readonly DependencyInjectionAnalyzer _diAnalyzer;

        public MethodCallExtractor(Solution solution, DependencyInjectionAnalyzer diAnalyzer)
        {
            _solution = solution;
            _diAnalyzer = diAnalyzer;
        }

        public async Task<MethodCallInfo> ExtractAsync(IMethodSymbol methodSymbol)
        {
            var concreteMethod = GetConcreteImplementation(methodSymbol);
            return await BuildCallTreeAsync(concreteMethod ?? methodSymbol);
        }

        private IMethodSymbol? GetConcreteImplementation(IMethodSymbol method)
        {
            if (method.ReceiverType?.TypeKind != TypeKind.Interface)
                return method;

            var concreteType = _diAnalyzer.GetConcreteType(method.ReceiverType);
            if (concreteType == null)
                return method;

            return FindMethodInType(concreteType, method);
        }

        private IMethodSymbol? FindMethodInType(ITypeSymbol type, IMethodSymbol interfaceMethod)
        {
            // Find explicit implementation
            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.ExplicitInterfaceImplementations.Any(impl =>
                    SymbolEqualityComparer.Default.Equals(impl, interfaceMethod)))
                {
                    return member;
                }
            }

            // Find implicit implementation
            return type.GetMembers(interfaceMethod.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m =>
                    m.Name == interfaceMethod.Name &&
                    ParametersMatch(m.Parameters, interfaceMethod.Parameters));
        }

        private static bool ParametersMatch(ImmutableArray<IParameterSymbol> implParams,
                                     ImmutableArray<IParameterSymbol> interfaceParams)
        {
            if (implParams.Length != interfaceParams.Length)
                return false;

            for (int i = 0; i < implParams.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(implParams[i].Type, interfaceParams[i].Type))
                    return false;
            }
            return true;
        }

        private async Task<MethodCallInfo> BuildCallTreeAsync(IMethodSymbol methodSymbol)
        {
            var info = new MethodCallInfo
            {
                Name = methodSymbol.Name,
                ClassName = methodSymbol.ContainingType.Name,
                InternalCalls = [],
                Namespace = methodSymbol.ContainingNamespace.ToString(),
                ReturnType = methodSymbol.ReturnType.ToString()
            };

            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null) return info;

            var syntax = await syntaxReference.GetSyntaxAsync();
            var semanticModel = await _solution.GetDocument(syntaxReference.SyntaxTree).GetSemanticModelAsync();
            if (semanticModel == null) return info;

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                if (symbolInfo.Symbol is IMethodSymbol calledMethod)
                {
                    var concreteMethod = GetConcreteImplementation(calledMethod);
                    if (concreteMethod != null)
                    {
                        info.InternalCalls.Add(await BuildCallTreeAsync(concreteMethod));
                    }
                }
            }

            return info;
        }
    }
    #endregion
}