namespace Sharpmd;

using System;

/// <summary> Specifies that a variable will have the same value across all lanes in a dispatch subgroup. </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Field | AttributeTargets.Property)]
public class UniformAttribute : Attribute {
}
