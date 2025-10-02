namespace ManDrill.Client.Models
{
    /// <summary>
    /// Represents a row in the dependency index table showing project dependencies for a method
    /// </summary>
    public class DependencyIndexItem
    {
        /// <summary>
        /// Name of the project that the method depends on (directly or indirectly)
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Namespace within the project that the method depends on
        /// </summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>
        /// Number of times entities within this namespace are referenced in the method
        /// </summary>
        public int TimesReferenced { get; set; }
    }
}
