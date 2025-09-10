using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

public class MethodCallInfo
{
    public string Name { get; set; }
    public string ClassName { get; set; }
    public string Namespace { get; set; }
    public string ReturnType { get; set; }
    public List<MethodCallInfo> InternalCalls { get; set; } = new();
}

public class MethodCallExtractor
{
    private readonly Solution _solution;
    private readonly HashSet<IMethodSymbol> _visited;

    public MethodCallExtractor(Solution solution)
    {
        _solution = solution;
        _visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    public async Task<MethodCallInfo> ExtractAsync(IMethodSymbol methodSymbol)
    {
        if (_visited.Contains(methodSymbol))
            return null;

        _visited.Add(methodSymbol);

        var info = new MethodCallInfo
        {
            Name = methodSymbol.Name,
            ClassName = methodSymbol.ContainingType.Name,
            Namespace = methodSymbol.ContainingNamespace.ToString(),
            ReturnType = methodSymbol.ReturnType.ToString()
        };

        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            var methodNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (methodNode == null) continue;

            var document = _solution.GetDocument(methodNode.SyntaxTree);
            if (document == null) continue;

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null) continue;

            var invocations = methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol == null) continue;

                // Skip external metadata methods
                if (symbol.Locations.All(l => l.IsInMetadata))
                    continue;

                var childInfo = await ExtractAsync(symbol);
                if (childInfo != null)
                    info.InternalCalls.Add(childInfo);
            }
        }

        return info;
    }
}
public static class SymbolExtensions
{
    public static IEnumerable<INamespaceSymbol> GetNamespaceMembersRecursive(this INamespaceSymbol ns)
    {
        yield return ns;
        foreach (var member in ns.GetNamespaceMembers())
            foreach (var child in member.GetNamespaceMembersRecursive())
                yield return child;
    }

    public static IEnumerable<INamedTypeSymbol> GetTypeMembersRecursive(this INamespaceOrTypeSymbol nsOrType)
    {
        foreach (var type in nsOrType.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in type.GetTypeMembersRecursive())
                yield return nested;
        }
    }
}

class Program
{
    static async Task Main()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            var instance = instances.FirstOrDefault(i => i.Version.Major >= 16) ?? instances.First();
            Console.WriteLine($"Using MSBuild at: {instance.MSBuildPath}");
            MSBuildLocator.RegisterInstance(instance);
        }

        Console.WriteLine("Enter full path to the solution (.sln) file:");
        string solutionPath = Console.ReadLine()?.Trim('"');

        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
            {
                {"AlwaysCompileMarkupFilesInSeparateDomain", "true"}
            });
        workspace.WorkspaceFailed += (o, e) =>
            Console.WriteLine($"[Workspace Diagnostic] {e.Diagnostic.Message}");

        Console.WriteLine("Loading solution...");
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        Console.WriteLine($"Loaded {solution.Projects.Count()} projects.");

        while (true)
        {
            Console.WriteLine("Enter Namespace (or blank to exit):");
            string namespaceName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(namespaceName)) break;

            Console.WriteLine("Enter Class Name:");
            string className = Console.ReadLine();

            Console.WriteLine("Enter Method Name (can also be operator symbol like '+', '-', '=='):");
            string methodNameInput = Console.ReadLine();

            // Operator name mapping
            var operatorMap = new Dictionary<string, string>
            {
                { "+", "op_Addition" },
                { "-", "op_Subtraction" },
                { "*", "op_Multiply" },
                { "/", "op_Division" },
                { "==", "op_Equality" },
                { "!=", "op_Inequality" },
                { "<", "op_LessThan" },
                { ">", "op_GreaterThan" }
            };
            if (operatorMap.ContainsKey(methodNameInput))
                methodNameInput = operatorMap[methodNameInput];

            var overloads = new List<IMethodSymbol>();

            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var ns in compilation.GlobalNamespace.GetNamespaceMembersRecursive())
                {
                    if (ns.ToDisplayString() == namespaceName)
                    {
                        foreach (var type in ns.GetTypeMembersRecursive())
                        {
                            // Remove generic arity suffix (MyClass`1 -> MyClass)
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
            }

            if (!overloads.Any())
            {
                Console.WriteLine("No matching methods found.\n");
                continue;
            }

            Console.WriteLine("\nOverloads found:");
            for (int i = 0; i < overloads.Count; i++)
            {
                var m = overloads[i];
                var parameters = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
                Console.WriteLine($"{i + 1}. {m.ReturnType} {m.Name}({parameters})");
            }

            Console.Write("\nEnter the number of the method to analyze: ");
            if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > overloads.Count)
            {
                Console.WriteLine("Invalid choice.\n");
                continue;
            }

            var methodSymbol = overloads[choice - 1];
            var extractor = new MethodCallExtractor(solution);
            var rootInfo = await extractor.ExtractAsync(methodSymbol);

            string json = JsonSerializer.Serialize(rootInfo, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine("\nGenerated JSON:");
            Console.WriteLine(json);
            Console.WriteLine("\n--- End of JSON ---\n");

            // 2. Path to View.json (adjust as needed)
            // 2. Save to Desktop/View.json
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string jsonPath = Path.Combine(desktopPath, "View.json");

            // 3. Overwrite the file
            File.WriteAllText(jsonPath, json);
            Console.WriteLine($"Updated: {jsonPath}");

            // 4. Open in VS Code (with JSON Crack extension)
            OpenInVSCode(json);
        }
    }

    static void OpenInVSCode(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = true
            });
            Console.WriteLine("Launched in VS Code!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open VS Code: {ex.Message}");
        }
    }
}
