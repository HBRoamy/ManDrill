using ManDrill.Client.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ManDrill.Client.Services
{
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
                if (await syntaxRef.GetSyntaxAsync() is not MethodDeclarationSyntax methodNode) continue;

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
}
