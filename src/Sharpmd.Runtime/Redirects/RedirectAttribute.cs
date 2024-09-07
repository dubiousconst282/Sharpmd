namespace Sharpmd.Redirects;

/// <summary> Specifies a class that implements vector-amenable method implementations for another type. </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RedirectAttribute(Type targetClass) : Attribute {
    public Type TargetClass { get; } = targetClass;
}