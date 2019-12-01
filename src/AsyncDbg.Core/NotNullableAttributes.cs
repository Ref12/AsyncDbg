namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class AssertsTrueAttribute : Attribute
    {
        public AssertsTrueAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class AssertsFalseAttribute : Attribute
    {
        public AssertsFalseAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class EnsuresNotNullAttribute : Attribute
    {
        public EnsuresNotNullAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool predicate) { }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class NotNullAttribute : Attribute
    {
        public NotNullAttribute() { }
    }
}
