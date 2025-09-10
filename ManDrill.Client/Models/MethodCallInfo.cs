namespace ManDrill.Client.Models
{
    public class MethodCallInfo
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string ReturnType { get; set; }
        public List<MethodCallInfo> InternalCalls { get; set; } = [];
    }
}
