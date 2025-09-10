using Microsoft.CodeAnalysis;

namespace ManDrill.Client.Helpers
{
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
}
