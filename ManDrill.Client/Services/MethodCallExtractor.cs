using ManDrill.Client.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ManDrill.Client.Services
{
    /// <summary>
    /// Service responsible for extracting method call information and building a hierarchical call tree
    /// from C# source code using Roslyn analysis.
    /// </summary>
    public class MethodCallExtractor
    {
        #region Private Fields

        /// <summary>
        /// The solution containing the source code to analyze
        /// </summary>
        private readonly Solution _solution;

        /// <summary>
        /// Tracks visited method symbols to prevent infinite recursion in circular dependencies
        /// </summary>
        private readonly HashSet<IMethodSymbol> _visited;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the MethodCallExtractor class
        /// </summary>
        /// <param name="solution">The Roslyn solution containing the source code to analyze</param>
        /// <exception cref="ArgumentNullException">Thrown when solution is null</exception>
        public MethodCallExtractor(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Extracts method call information and builds a hierarchical call tree for the specified method symbol
        /// </summary>
        /// <param name="methodSymbol">The method symbol to analyze</param>
        /// <returns>
        /// A MethodCallInfo object containing the method details and its internal calls, 
        /// or null if the method has already been visited (prevents circular dependencies)
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when methodSymbol is null</exception>
        public async Task<MethodCallInfo> ExtractAsync(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                throw new ArgumentNullException(nameof(methodSymbol));

            // Prevent infinite recursion by checking if method has already been visited
            if (IsMethodAlreadyVisited(methodSymbol))
                return null;// TODO: think of returning the methodInfo instead of null

            // Mark method as visited to prevent circular dependencies
            MarkMethodAsVisited(methodSymbol);

            // Create the base method information
            var methodInfo = CreateMethodInfo(methodSymbol);

            // Process all syntax references for this method
            await ProcessMethodSyntaxReferences(methodSymbol, methodInfo);

            return methodInfo;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Checks if a method symbol has already been visited to prevent circular dependencies
        /// </summary>
        /// <param name="methodSymbol">The method symbol to check</param>
        /// <returns>True if the method has already been visited, false otherwise</returns>
        private bool IsMethodAlreadyVisited(IMethodSymbol methodSymbol)
        {
            return _visited.Contains(methodSymbol);
        }

        /// <summary>
        /// Marks a method symbol as visited to prevent circular dependencies
        /// </summary>
        /// <param name="methodSymbol">The method symbol to mark as visited</param>
        private void MarkMethodAsVisited(IMethodSymbol methodSymbol)
        {
            _visited.Add(methodSymbol);
        }

        /// <summary>
        /// Creates a MethodCallInfo object from a method symbol with basic method information
        /// </summary>
        /// <param name="methodSymbol">The method symbol to extract information from</param>
        /// <returns>A new MethodCallInfo object populated with method details</returns>
        private MethodCallInfo CreateMethodInfo(IMethodSymbol methodSymbol)
        {
            return new MethodCallInfo
            {
                Name = methodSymbol.Name,
                ClassName = methodSymbol.ContainingType.Name,
                Namespace = methodSymbol.ContainingNamespace.ToString(),
                ReturnType = methodSymbol.ReturnType.ToString()
            };
        }

        /// <summary>
        /// Processes all syntax references for a method symbol to find and analyze method invocations
        /// </summary>
        /// <param name="methodSymbol">The method symbol to process</param>
        /// <param name="methodInfo">The MethodCallInfo object to populate with internal calls</param>
        private async Task ProcessMethodSyntaxReferences(IMethodSymbol methodSymbol, MethodCallInfo methodInfo)
        {
            foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
            {
                var methodNode = await GetMethodDeclarationSyntax(syntaxRef);
                if (methodNode == null) continue;

                var document = GetDocumentFromSyntaxTree(methodNode.SyntaxTree);
                if (document == null) continue;

                var semanticModel = await GetSemanticModelFromDocument(document);
                if (semanticModel == null) continue;

                await ProcessMethodInvocations(methodNode, semanticModel, methodInfo);
            }
        }

        /// <summary>
        /// Retrieves the MethodDeclarationSyntax from a syntax reference
        /// </summary>
        /// <param name="syntaxRef">The syntax reference to process</param>
        /// <returns>The MethodDeclarationSyntax if found, null otherwise</returns>
        private async Task<MethodDeclarationSyntax> GetMethodDeclarationSyntax(SyntaxReference syntaxRef)
        {
            var syntax = await syntaxRef.GetSyntaxAsync();
            return syntax as MethodDeclarationSyntax;
        }

        /// <summary>
        /// Gets the document from a syntax tree
        /// </summary>
        /// <param name="syntaxTree">The syntax tree to get the document for</param>
        /// <returns>The document if found, null otherwise</returns>
        private Document GetDocumentFromSyntaxTree(SyntaxTree syntaxTree)
        {
            return _solution.GetDocument(syntaxTree);
        }

        /// <summary>
        /// Gets the semantic model from a document
        /// </summary>
        /// <param name="document">The document to get the semantic model for</param>
        /// <returns>The semantic model if found, null otherwise</returns>
        private async Task<SemanticModel> GetSemanticModelFromDocument(Document document)
        {
            return await document.GetSemanticModelAsync();
        }

        /// <summary>
        /// Processes all method invocations within a method declaration and extracts their call information
        /// </summary>
        /// <param name="methodNode">The method declaration syntax node to analyze</param>
        /// <param name="semanticModel">The semantic model for symbol resolution</param>
        /// <param name="methodInfo">The MethodCallInfo object to populate with internal calls</param>
        private async Task ProcessMethodInvocations(MethodDeclarationSyntax methodNode, SemanticModel semanticModel, MethodCallInfo methodInfo)
        {
            var invocations = GetMethodInvocations(methodNode);

            foreach (var invocation in invocations)
            {
                var methodSymbol = GetMethodSymbolFromInvocation(invocation, semanticModel);
                if (methodSymbol == null) continue;

                // Skip external metadata methods (from referenced assemblies)
                if (IsExternalMetadataMethod(methodSymbol))
                    continue;

                var childInfo = await ExtractAsync(methodSymbol);
                if (childInfo != null)
                    methodInfo.InternalCalls.Add(childInfo);
            }
        }

        /// <summary>
        /// Extracts all invocation expressions from a method declaration
        /// </summary>
        /// <param name="methodNode">The method declaration syntax node to search</param>
        /// <returns>An enumerable of InvocationExpressionSyntax nodes</returns>
        private IEnumerable<InvocationExpressionSyntax> GetMethodInvocations(MethodDeclarationSyntax methodNode)
        {
            return methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>();
        }

        /// <summary>
        /// Gets the method symbol from an invocation expression using the semantic model
        /// </summary>
        /// <param name="invocation">The invocation expression to analyze</param>
        /// <param name="semanticModel">The semantic model for symbol resolution</param>
        /// <returns>The method symbol if found, null otherwise</returns>
        private IMethodSymbol GetMethodSymbolFromInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            return symbolInfo.Symbol as IMethodSymbol;
        }

        /// <summary>
        /// Determines if a method symbol is from external metadata (referenced assemblies)
        /// </summary>
        /// <param name="methodSymbol">The method symbol to check</param>
        /// <returns>True if the method is from external metadata, false otherwise</returns>
        private bool IsExternalMetadataMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Locations.All(location => location.IsInMetadata);
        }

        #endregion
    }
}
