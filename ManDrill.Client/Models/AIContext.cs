namespace ManDrill.Client.Models
{
    public class AIContext
    {
        public string InterfaceMethod { get; set; }
        public string CallSite { get; set; }
        public string CallContext { get; set; }
        public List<ImplementationInfo> AvailableImplementations { get; set; }
    }

    public class ImplementationInfo
    {
        public string TypeName { get; set; }
        public string FullTypeName { get; set; }
        public string MethodSignature { get; set; }
    }
}
