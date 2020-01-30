using System;

namespace Assets.TransformSetterCallRedirector.Attribute
{
    /// <summary>
    /// Indicates that the property's backing field is serialized.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class TransformSetterCallRedirectorAttribute : System.Attribute
    {
        public string EventInterceptorTypeName { get; }
        public string ReplaceCallsFromNamespacesRegex { get; }
        public string ExcludeFullMethodNameRegex { get; set; } = "";

        public TransformSetterCallRedirectorAttribute(string eventInterceptorTypeName, string replaceCallsFromNamespacesRegex)
        {
            EventInterceptorTypeName = eventInterceptorTypeName;
            ReplaceCallsFromNamespacesRegex = replaceCallsFromNamespacesRegex;
        }
    }
}
