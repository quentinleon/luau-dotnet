using System.Reflection;

namespace Luau.Tests;

public sealed class PublicApiContractTests
{
    static readonly string[] ApprovedTransitionalNativeLeaks =
    [
        "Luau.LuauBuffer.AsPointer() -> System.Void*",
        "Luau.LuauCompileOptions..ctor(Luau.Native.lua_CompileOptions)",
        "Luau.LuauFunction.AsCFunction() -> Luau.Native.lua_CFunction",
        "Luau.LuauFunction.AsPointer() -> System.Void*",
        "Luau.LuauState.AsPointer() -> Luau.Native.lua_State*",
        "Luau.LuauState.PushCClosure(Luau.Native.lua_CFunction, System.ReadOnlySpan<System.Byte>, System.Int32) -> System.Void",
        "Luau.LuauState.PushCFunction(Luau.Native.lua_CFunction, System.ReadOnlySpan<System.Byte>) -> System.Void",
        "Luau.LuauState.PushLightUserData(System.Void*) -> System.Void",
        "Luau.LuauState.ToCFunction(System.Int32) -> Luau.Native.lua_CFunction",
        "Luau.LuauState.ToLightUserData(System.Int32) -> System.IntPtr",
        "Luau.LuauState.ToPointer(System.Int32) -> System.Void*",
        "Luau.LuauTable.AsPointer() -> System.Void*",
        "Luau.LuauUserData.AsPointer() -> System.Void*",
        "Luau.LuauValue.FromLightUserData(System.IntPtr) -> Luau.LuauValue",
    ];

    [Fact]
    public void ManagedPublicApiContainsNoUnapprovedNativeSignatures()
    {
        var actual = GetNativeLeakingMembers(typeof(LuauState).Assembly);

        Assert.Equal(ApprovedTransitionalNativeLeaks, actual);
    }

    [Fact]
    public void TransitionalNativeSignaturesCarryRemovalDiagnostics()
    {
        var members = GetNativeLeakingMemberInfos(typeof(LuauState).Assembly);

        Assert.All(members, member =>
        {
            var obsolete = member.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(obsolete);
            Assert.False(string.IsNullOrWhiteSpace(obsolete.Message));
            Assert.Contains("unsupported", obsolete.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    static string[] GetNativeLeakingMembers(Assembly assembly) =>
        GetNativeLeakingMemberInfos(assembly)
            .Select(FormatMember)
            .Order(StringComparer.Ordinal)
            .ToArray();

    static MemberInfo[] GetNativeLeakingMemberInfos(Assembly assembly)
    {
        const BindingFlags flags =
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        return assembly.GetExportedTypes()
            .SelectMany(type =>
                type.GetConstructors(flags).Cast<MemberInfo>()
                    .Concat(type.GetMethods(flags).Where(method => !method.IsSpecialName))
                    .Concat(type.GetProperties(flags))
                    .Concat(type.GetFields(flags)))
            .Where(MemberLeaksNativeType)
            .OrderBy(FormatMember, StringComparer.Ordinal)
            .ToArray();
    }

    static bool MemberLeaksNativeType(MemberInfo member) => member switch
    {
        ConstructorInfo constructor =>
            constructor.GetParameters().Any(parameter => IsNativeType(parameter.ParameterType)),
        MethodInfo method =>
            IsNativeType(method.ReturnType) ||
            method.GetParameters().Any(parameter => IsNativeType(parameter.ParameterType)),
        PropertyInfo property => IsNativeType(property.PropertyType),
        FieldInfo field => IsNativeType(field.FieldType),
        _ => false,
    };

    static bool IsNativeType(Type type)
    {
        if (type.IsPointer)
        {
            return true;
        }

        if (type.IsByRef || type.IsArray)
        {
            return IsNativeType(type.GetElementType()!);
        }

        if (string.Equals(type.Namespace, "Luau.Native", StringComparison.Ordinal))
        {
            return true;
        }

        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsNativeType);
    }

    static string FormatMember(MemberInfo member)
    {
        var owner = member.DeclaringType!.FullName;
        return member switch
        {
            ConstructorInfo constructor =>
                $"{owner}..ctor({FormatParameters(constructor.GetParameters())})",
            MethodInfo method =>
                $"{owner}.{method.Name}({FormatParameters(method.GetParameters())}) -> {FormatType(method.ReturnType)}",
            PropertyInfo property => $"{owner}.{property.Name} : {FormatType(property.PropertyType)}",
            FieldInfo field => $"{owner}.{field.Name} : {FormatType(field.FieldType)}",
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
    }

    static string FormatParameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(parameter => FormatType(parameter.ParameterType)));

    static string FormatType(Type type)
    {
        if (type.IsPointer)
        {
            return $"{FormatType(type.GetElementType()!)}*";
        }

        if (type.IsByRef)
        {
            return $"{FormatType(type.GetElementType()!)}&";
        }

        if (type.IsArray)
        {
            return $"{FormatType(type.GetElementType()!)}[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definitionName = type.GetGenericTypeDefinition().FullName!;
        var tick = definitionName.IndexOf('`');
        if (tick >= 0)
        {
            definitionName = definitionName[..tick];
        }

        return $"{definitionName}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }
}
