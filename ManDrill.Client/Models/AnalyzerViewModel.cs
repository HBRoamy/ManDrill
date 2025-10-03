using System.ComponentModel.DataAnnotations;

namespace ManDrill.Client.Models
{
    public class AnalyzerViewModel
    {
        [Required(ErrorMessage = "Solution path is required.")]
        public string SolutionPath { get; set; }

        [Required(ErrorMessage = "Namespace name is required.")]
        public string NamespaceName { get; set; }

        [Required(ErrorMessage = "Class name is required.")]
        public string ClassName { get; set; }

        [Required(ErrorMessage = "Method name is required.")]
        public string MethodName { get; set; }

        public int SelectedOverload { get; set; }

        public string JsonOutput { get; set; }

        public string AISummary { get; set; } = string.Empty;

        public bool IncludeAISummary { get; set; }
        public string MethodSequenceDiagram { get; set; }
        public string ProjectDependencyDiagram { get; set; }
        /// <summary>
        /// Dictionary with KEY: Diagram Name, VALUE: Diagram code, mostly mermaid code
        /// </summary>
        public List<DiagramDetails> Diagrams { get; set; } = [];
        /// <summary>
        /// List of dependency index items showing project dependencies for the analyzed method
        /// </summary>
        public List<DependencyIndexItem> DependencyIndexItems { get; set; } = new List<DependencyIndexItem>();
        public List<List<string>> Ancestors { get; set; } = [];
        public string ChatBotContext { get; set; }
    }

    public class DiagramDetails
    {
        public string Name { get; set; }
        public object DiagramInputData { get; set; }
        public string DiagramPartialViewName { get; set; }
        public string AdditionalTitleAttributes { get; set; }
    }
}
