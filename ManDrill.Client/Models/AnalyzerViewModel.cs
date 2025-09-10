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

        public List<OverloadInfo> Overloads { get; set; } = new();

        public int SelectedOverload { get; set; }

        public string JsonOutput { get; set; }

        public bool IncludeAISummary { get; set; }
    }
}
