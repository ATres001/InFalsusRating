namespace System.Runtime.CompilerServices;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
    AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Parameter |
    AttributeTargets.ReturnValue | AttributeTargets.GenericParameter,
    AllowMultiple = false, Inherited = false)]
public sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;

    public NullableAttribute() { }
    public NullableAttribute(byte value) { NullableFlags = new[] { value }; }
    public NullableAttribute(byte[] value) { NullableFlags = value; }
}