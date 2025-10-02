using ManDrill.Client.Models;
using ManDrill.Client.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

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

        /// <summary>
        /// Tracks dependency information during method traversal
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, int>> _dependencyMap;

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
            _dependencyMap = new Dictionary<string, Dictionary<string, int>>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Extracts method call information and builds a hierarchical call tree for the specified method symbol
        /// </summary>
        /// <param name="methodSymbol">The method symbol to analyze</param>
        /// <returns>
        /// A MethodCallInfo object containing the method details and its internal calls, 
        /// or a basic MethodCallInfo without internal calls if the method has already been visited (prevents circular dependencies)
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when methodSymbol is null</exception>
        public async Task<MethodCallInfo> ExtractAsync(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                return null;

            // Prevent infinite recursion by checking if method has already been visited
            if (IsMethodAlreadyVisited(methodSymbol))
            {
                // Return basic method info without processing internal calls to prevent circular dependencies
                return CreateMethodInfo(methodSymbol);
            }

            // Mark method as visited to prevent circular dependencies
            MarkMethodAsVisited(methodSymbol);

            // Create the base method information
            var methodInfo = CreateMethodInfo(methodSymbol);

            // Process all syntax references for this method
            await ProcessMethodSyntaxReferences(methodSymbol, methodInfo);

            return methodInfo;
        }

        public async Task<List<List<IMethodSymbol>>> ExtractSuperAncestorsAsync(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
            {
                return new List<List<IMethodSymbol>>();
            }

            var allPaths = new List<List<IMethodSymbol>>();
            var visitedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            await ExtractPathsRecursively(methodSymbol, new List<IMethodSymbol>(), allPaths, visitedMethods);

            //for(int i=allPaths.Count-1;i>=0;i--)
            //{
            //    var path = allPaths[i];
            //    var references = await SymbolFinder.FindReferencesAsync(path[^1], _solution);

            //    if (references != null && references.Any())
            //    {
            //        allPaths.RemoveAt(i);
            //    }
            //}

            return allPaths;
        }

        private async Task ExtractPathsRecursively(
            IMethodSymbol currentMethod,
            List<IMethodSymbol> currentPath,
            List<List<IMethodSymbol>> allPaths,
            HashSet<IMethodSymbol> visitedMethods)
        {
            // Prevent infinite recursion by checking if we've already visited this method in the current path
            if (visitedMethods.Contains(currentMethod))
            {
                return;
            }

            // Add current method to the path and visited set
            var newPath = new List<IMethodSymbol>(currentPath) { currentMethod };
            visitedMethods.Add(currentMethod);

            try
            {
                // Find all references to the current method
                var references = await SymbolFinder.FindReferencesAsync(currentMethod, _solution);

                var callingMethods = new List<IMethodSymbol>();

                if (references != null && references.Any())
                {
                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            // Get the document and semantic model for this reference
                            var document = _solution.GetDocument(location.Document.Id);
                            if (document == null) continue;

                            var semanticModel = await document.GetSemanticModelAsync();
                            if (semanticModel == null) continue;

                            var syntaxRoot = await document.GetSyntaxRootAsync();
                            if (syntaxRoot == null) continue;

                            // Find the syntax node at the reference location
                            var node = syntaxRoot.FindNode(location.Location.SourceSpan);

                            // Find the containing method symbol
                            var containingMethod = GetContainingMethod(node, semanticModel);
                            if (containingMethod != null &&
                                !SymbolEqualityComparer.Default.Equals(containingMethod, currentMethod))
                            {
                                callingMethods.Add(containingMethod);
                            }
                        }
                    }
                }

                // If no calling methods found, this is a super ancestor (like controller endpoints)
                if (!callingMethods.Any())
                {
                    allPaths.Add(newPath);
                }
                else
                {
                    // Recursively process each calling method
                    foreach (var callingMethod in callingMethods.Distinct(SymbolEqualityComparer.Default))
                    {
                        if (callingMethod is IMethodSymbol methodSymbol)
                        {
                            await ExtractPathsRecursively(methodSymbol, newPath, allPaths, new HashSet<IMethodSymbol>(visitedMethods, SymbolEqualityComparer.Default));
                        }
                    }
                }
            }
            finally
            {
                // Remove from visited set when backtracking (allows the same method in different paths)
                visitedMethods.Remove(currentMethod);
            }
        }

        private IMethodSymbol GetContainingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            // Traverse up the syntax tree to find the containing method
            var current = node;
            while (current != null)
            {
                if (current is MethodDeclarationSyntax ||
                    current is ConstructorDeclarationSyntax ||
                    current is AccessorDeclarationSyntax ||
                    current is LocalFunctionStatementSyntax ||
                    current is AnonymousFunctionExpressionSyntax)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(current);
                    if (symbol is IMethodSymbol methodSymbol)
                    {
                        return methodSymbol;
                    }
                }
                current = current.Parent;
            }
            return null;
        }


        /// <summary>
        /// Gets the dependency index items collected during method traversal
        /// </summary>
        /// <returns>List of dependency index items</returns>
        public List<DependencyIndexItem> GetDependencyIndexItems()
        {
            var dependencyItems = new List<DependencyIndexItem>();
            foreach (var projectEntry in _dependencyMap)
            {
                foreach (var namespaceEntry in projectEntry.Value)
                {
                    dependencyItems.Add(new DependencyIndexItem
                    {
                        ProjectName = projectEntry.Key,
                        Namespace = namespaceEntry.Key,
                        TimesReferenced = namespaceEntry.Value
                    });
                }
            }

            // Sort by Times Referenced (descending), then by project name, then by namespace
            return dependencyItems.OrderByDescending(x => x.TimesReferenced).ThenBy(x => x.ProjectName).ThenBy(x => x.Namespace).ToList();
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
            var methodInfo = new MethodCallInfo
            {
                Name = methodSymbol.Name,
                ClassName = methodSymbol.ContainingType.Name,
                Namespace = methodSymbol.ContainingNamespace.ToString(),
                ParamsInfo = string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type} {p.Name}")),
                ReturnType = methodSymbol.ReturnType.ToString()
            };

            // Track dependency information
            TrackDependency(methodSymbol);

            return methodInfo;
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

                var callContext = "";//CreateMethodContext(rootMethod);
                var resolvedMethod = await ResolveMethodAsync(methodSymbol, invocation, semanticModel, callContext);
                var childInfo = await ExtractAsync(resolvedMethod);

                if(childInfo != null && methodSymbol.ContainingType.TypeKind == TypeKind.Interface && resolvedMethod.ContainingType.TypeKind == TypeKind.Class)
                {
                    childInfo.ResolvedFrom = methodSymbol.ContainingType?.ToDisplayString();
                }

                if (childInfo != null)
                {
                    methodInfo.InternalCalls.Add(childInfo);
                }
            }
        }

        private string CreateMethodContext(IMethodSymbol rootMethod)
        {
            return $"Namespace: {rootMethod.ContainingNamespace} | Calling Class: {rootMethod.ContainingType} | Method Name: {rootMethod.Name} | Params: {string.Join(",", rootMethod.Parameters)} | Method Body: " + (rootMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax)?.ToFullString();
        }

        private async Task<IMethodSymbol> ResolveMethodAsync(IMethodSymbol method,
        InvocationExpressionSyntax invocation, SemanticModel semanticModel, string? callContext)
        {
            // If it's not an interface method, return as-is
            if (method.ContainingType?.TypeKind != TypeKind.Interface)
            {
                return method;
            }

            // Find implementations of the interface method
            var implementations = await SymbolFinder.FindImplementationsAsync(
                method, _solution);

            var methodImplementations = implementations
                .OfType<IMethodSymbol>()
                .Where(impl => !impl.ContainingType.IsAbstract)
                .ToList();

            if (methodImplementations.Count < 1)
            {
                return method; // No concrete implementations found
            }

            if (methodImplementations.Count == 1)
            {
                return methodImplementations[0]; // Only one implementation
            }

            // Multiple implementations - use AI to decide
            var context = BuildContextForAI(method, invocation, semanticModel, methodImplementations, callContext);
            var selectedImplementation = await new AIService().SelectBestImplementationAsync(context);

            return methodImplementations.FirstOrDefault(impl =>
                impl.ContainingType.Name == selectedImplementation?.TypeName) ?? method;
        }

        private AIContext BuildContextForAI(IMethodSymbol interfaceMethod,
        InvocationExpressionSyntax invocation, SemanticModel semanticModel,
        List<IMethodSymbol> implementations, string callContext)
        {
            return new AIContext
            {
                InterfaceMethod = interfaceMethod.ToDisplayString(),
                CallSite = invocation.ToString(),
                CallContext = callContext,
                AvailableImplementations = implementations.Select(impl => new ImplementationInfo
                {
                    TypeName = impl.ContainingType.Name,
                    FullTypeName = impl.ContainingType.ToDisplayString(),
                    MethodSignature = impl.ToDisplayString()
                }).ToList()
            };
        }

        /// <summary>
        /// Extracts all invocation expressions from a method declaration
        /// </summary>
        /// <param name="methodNode">The method declaration syntax node to search</param>
        /// <returns>An enumerable of InvocationExpressionSyntax nodes</returns>
        private IEnumerable<InvocationExpressionSyntax> GetMethodInvocations(MethodDeclarationSyntax methodNode)
        {
            // Get all invocation expressions within the method body
            // This includes calls in nested blocks, loops, conditionals, etc.
            return methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => 
                    // Ensure we're not capturing invocations from method signatures or attributes
                    !invocation.IsPartOfStructuredTrivia() &&
                    // Make sure the invocation is within the method body
                    methodNode.Contains(invocation));
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
            
            // First try to get the resolved symbol
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                return methodSymbol;
            
            // If no resolved symbol, try to get the best candidate
            if (symbolInfo.CandidateSymbols.Length > 0)
            {
                // Look for method symbols in candidates
                var methodCandidates = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToList();
                if (methodCandidates.Count > 0)
                {
                    // Return the first method candidate
                    // In case of ambiguity, we'll take the first one
                    return methodCandidates[0];
                }
            }
            
            return null;
        }

        private async Task<IMethodSymbol?> GetMethodSymbolFromInvocationV2(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            // For dependency resolution, we need the calling method's body
            // calling method's namespace, class and method params
            // then we need the list of concrete implementation of the interface method,
            // all this we need to pass to the AI, to find the best match, additionally ask the AI percentage of match and use it to color the node

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            invocation.FirstAncestorOrSelf<SyntaxNode>();
            // Handle null symbol and non-method symbols
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                return null;

            // Skip if the method is not from an interface
            if (methodSymbol.ContainingType.TypeKind != TypeKind.Interface)
                return methodSymbol;

            // Find all concrete implementations and filter to method symbols
            var implementations = (await SymbolFinder.FindImplementationsAsync(methodSymbol, _solution))
                .OfType<IMethodSymbol>()
                .ToList();

            return implementations.Count switch
            {
                0 => null, // No implementations found
                1 => implementations[0], // Single implementation
                _ => await SelectBestImplementationAsync(methodSymbol, implementations) // Multiple implementations
            };
        }

        private async Task<IMethodSymbol?> SelectBestImplementationAsync(IMethodSymbol interfaceMethod, List<IMethodSymbol> implementations)
        {
            // Add custom logic to select the best implementation, e.g., based on context, project structure, or heuristics
            // For now, return the first implementation as a fallback

            return implementations.FirstOrDefault();
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

        /// <summary>
        /// Tracks dependency information for a method symbol
        /// </summary>
        /// <param name="methodSymbol">The method symbol to track</param>
        private void TrackDependency(IMethodSymbol methodSymbol)
        {
            var projectName = GetProjectNameFromMethodSymbol(methodSymbol);
            if (string.IsNullOrEmpty(projectName)) return;

            if (!_dependencyMap.ContainsKey(projectName))
            {
                _dependencyMap[projectName] = new Dictionary<string, int>();
            }

            var namespaceName = methodSymbol.ContainingNamespace.ToString();
            if (!_dependencyMap[projectName].ContainsKey(namespaceName))
            {
                _dependencyMap[projectName][namespaceName] = 0;
            }

            _dependencyMap[projectName][namespaceName]++;
        }

        /// <summary>
        /// Determines the project name from a method symbol by finding which project contains the method
        /// </summary>
        /// <param name="methodSymbol">The method symbol to find the project for</param>
        /// <returns>The project name, or empty string if not found</returns>
        private string GetProjectNameFromMethodSymbol(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null) return string.Empty;

            // Get the first source location (not metadata)
            var sourceLocation = methodSymbol.Locations.FirstOrDefault(loc => !loc.IsInMetadata);
            if (sourceLocation == null) return string.Empty;

            // Get the document from the syntax tree
            var document = _solution.GetDocument(sourceLocation.SourceTree);
            if (document == null) return string.Empty;

            // Return the project name
            return document.Project.Name;
        }

        #endregion
    }
}
