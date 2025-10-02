namespace ManDrill.Client.Models
{
    public class MethodCallInfo
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string ReturnType { get; set; }
        public string ParamsInfo { get; set; }
        /// <summary>
        /// Which Interface did it resolve for, if it was resolved.
        /// </summary>
        public string? ResolvedFrom { get; set; }
        public List<MethodCallInfo> InternalCalls { get; set; } = [];
    }
}
