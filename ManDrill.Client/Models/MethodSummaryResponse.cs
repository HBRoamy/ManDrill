using System.Text.Json.Serialization;

namespace ManDrill.Client.Models
{
    public class MethodSummaryResponse
    {
        public string Title { get; set; }
        public string BusinessContext { get; set; }
        public string TechnicalContext { get; set; }
        public List<string> KeyOperations { get; set; }
        public string FlowDiagram { get; set; }
        public Dictionary<string, string> Parameters { get; set; }
        public List<string> Dependencies { get; set; }
        public string PerformanceNotes { get; set; }
        public string Conclusion { get; set; }
        public TimeSaved TimeSaved { get; set; }
    }

    public class TimeSaved
    {
        [JsonPropertyName("estimateMinutes")] // makes sure JSON "estimateMinutes" binds correctly
        public string EstimateMinutes { get; set; }
    }
}
