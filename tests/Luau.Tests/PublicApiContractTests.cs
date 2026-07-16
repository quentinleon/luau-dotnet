using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Luau.Tests;

public sealed class PublicApiContractTests
{
    const BindingFlags PublicDeclared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    const string BaselineResource = "Luau.Tests.PublicApi.approved.txt";

    [Fact]
    public void ManagedPublicApiMatchesApprovedBaseline()
    {
        var actual = Snapshot(typeof(LuauState).Assembly);

        // Deliberate maintainer-only refresh; ordinary test runs are read-only.
        if (Environment.GetEnvironmentVariable("LUAU_UPDATE_PUBLIC_API") == "1")
        {
            File.WriteAllLines(BaselinePath(), actual, new UTF8Encoding(false));
            return;
        }

        using var stream = typeof(PublicApiContractTests).Assembly.GetManifestResourceStream(BaselineResource);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var expected = reader.ReadToEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(expected.SequenceEqual(actual, StringComparer.Ordinal), Diff(expected, actual));
    }

    [Fact]
    public void ManagedPublicApiContainsNoNativeSurface()
    {
        var assembly = typeof(LuauState).Assembly;
        var types = assembly.GetExportedTypes();
        var leaks = types
            .Where(type => IsNativeType(type) ||
                new[] { type.BaseType }.Where(candidate => candidate != null).Cast<Type>()
                    .Concat(type.GetInterfaces()).Any(IsNativeType))
            .Select(type => $"type {FormatType(type)}")
            .Concat(types
                .SelectMany(type => PublicMembers(type).Where(member => SignatureTypes(member).Any(IsNativeType)))
                .Select(FormatMember))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(leaks.Length == 0, string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public void InteropAssemblyExportsNoPublicTypes()
    {
        var reference = Assert.Single(
            typeof(LuauState).Assembly.GetReferencedAssemblies(),
            name => name.Name == "Luau.Interop");

        Assert.Empty(Assembly.Load(reference).ExportedTypes);
    }

    [Fact]
    public void RemovedRawAndAmbiguousEntryPointsStayRemoved()
    {
        var stateMethods = typeof(LuauState).GetMethods(PublicDeclared);
        string[] forbiddenNames =
        [
            "OpenLibraries", "Sandbox", "Call", "GetTop", "SetTop", "GetAbsIndex",
            "Insert", "Replace", "Remove", "CheckStack", "GetLuauType", "ToValue",
            "ToBoolean", "ToNumber", "ToInteger", "ToVector", "ToStringUtf8",
            "ToTable", "ToFunction", "ToUserData", "ToBuffer", "ToThread", "ToPointer",
            "ToCFunction", "ToLightUserData", "Push", "PushNil", "PushBoolean",
            "PushNumber", "PushInteger", "PushVector", "PushString", "PushThread",
            "PushTable", "PushFunction", "PushUserData", "PushBuffer", "PushCClosure",
            "PushCFunction", "PushLightUserData", "Pop", "XMove",
        ];

        Assert.DoesNotContain(stateMethods, method => forbiddenNames.Contains(method.Name));
        Assert.DoesNotContain(
            typeof(LuauFunction).GetMethods(PublicDeclared),
            method => method.Name == "InvokeAsync");

        Assert.All(
            stateMethods.Where(method => method.Name is "CreateFunction" or "CreateAsyncFunction"),
            method =>
            {
                var callback = Assert.Single(
                    method.GetParameters(),
                    parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
                var invoke = callback.ParameterType.GetMethod("Invoke")!;
                Assert.Equal(typeof(LuauCallContext), Assert.Single(invoke.GetParameters()).ParameterType);
                Assert.NotEqual(typeof(int), invoke.ReturnType);
                Assert.NotEqual(typeof(ValueTask<int>), invoke.ReturnType);
            });
    }

    [Fact]
    public void TrustedBytecodeCapabilitiesAreExplicitlyNamed()
    {
        var names = typeof(LuauState).GetMethods(PublicDeclared)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("LoadTrustedBytecode", names);
        Assert.Contains("ExecuteTrustedBytecode", names);
        Assert.Contains("ExecuteTrustedBytecodeAsync", names);
        Assert.DoesNotContain("LoadBytecode", names);
        Assert.DoesNotContain("ExecuteBytecode", names);
        Assert.DoesNotContain("ExecuteBytecodeAsync", names);
    }

    static string[] Snapshot(Assembly assembly) => assembly.GetExportedTypes()
        .SelectMany(type => new[] { FormatTypeDeclaration(type) }.Concat(PublicMembers(type).Select(FormatMember)))
        .Order(StringComparer.Ordinal)
        .ToArray();

    static MemberInfo[] PublicMembers(Type type) =>
        type.GetConstructors(PublicDeclared).Cast<MemberInfo>()
            .Concat(type.GetMethods(PublicDeclared).Where(method =>
                !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)))
            .Concat(type.GetProperties(PublicDeclared))
            .Concat(type.GetEvents(PublicDeclared))
            .Concat(type.GetFields(PublicDeclared).Where(field => !field.IsSpecialName))
            .OrderBy(FormatMember, StringComparer.Ordinal)
            .ToArray();

    static string FormatTypeDeclaration(Type type)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : typeof(MulticastDelegate).IsAssignableFrom(type.BaseType) ? "delegate"
            : type.IsValueType ? type.IsByRefLike ? "ref struct"
                : type.IsDefined(typeof(IsReadOnlyAttribute)) ? "readonly struct" : "struct"
            : type.IsAbstract && type.IsSealed ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";
        var bases = new[] { type.BaseType }
            .Where(candidate => candidate != null &&
                candidate != typeof(object) && candidate != typeof(ValueType) &&
                candidate != typeof(Enum) && candidate != typeof(MulticastDelegate))
            .Cast<Type>()
            .Concat(type.GetInterfaces().Where(IsExternallyVisible))
            .Distinct()
            .OrderBy(FormatType, StringComparer.Ordinal)
            .Select(FormatType)
            .ToArray();
        return $"type {kind} {FormatType(type)}" +
            (bases.Length == 0 ? string.Empty : $" : {string.Join(", ", bases)}");
    }

    static string FormatMember(MemberInfo member)
    {
        var kind = member switch
        {
            ConstructorInfo => "ctor",
            MethodInfo => "method",
            PropertyInfo => "property",
            EventInfo => "event",
            FieldInfo => "field",
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
        var detail = member switch
        {
            PropertyInfo property => $"{property} {{ " +
                (property.GetMethod?.IsPublic == true ? "get; " : string.Empty) +
                (property.SetMethod?.IsPublic == true ? "set; " : string.Empty) + "}",
            FieldInfo field when field.IsLiteral =>
                $"{field} = {FormatConstant(field.GetRawConstantValue(), field.FieldType)}",
            _ => member.ToString()!,
        };
        return $"{kind} {FormatType(member.DeclaringType!)} :: {detail}";
    }

    static string FormatType(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            var suffix = type.IsByRef ? "&" : type.IsPointer ? "*" : $"[{new string(',', type.GetArrayRank() - 1)}]";
            return FormatType(type.GetElementType()!) + suffix;
        }
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        var name = StripArity((type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? type.Name)
            .Replace('+', '.');
        return type.IsGenericType
            ? $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>"
            : name;
    }

    static string StripArity(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '`')
            {
                while (index + 1 < value.Length && char.IsDigit(value[index + 1])) index++;
            }
            else
            {
                result.Append(value[index]);
            }
        }
        return result.ToString();
    }

    static string FormatConstant(object? value, Type type) => value switch
    {
        null or DBNull or Missing => type.IsValueType ? "default" : "null",
        string text => $"\"{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        char character => $"'{character}'",
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString()!,
    };

    static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
        MethodInfo method => new[] { method.ReturnType }.Concat(method.GetParameters().Select(parameter => parameter.ParameterType)),
        PropertyInfo property => new[] { property.PropertyType }.Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType)),
        EventInfo @event => [@event.EventHandlerType!],
        FieldInfo field => [field.FieldType],
        _ => [],
    };

    static bool IsNativeType(Type type)
    {
        if (type.IsPointer || type.IsFunctionPointer || type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
        if (type.IsByRef || type.IsArray) return IsNativeType(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericArguments().Any(IsNativeType)) return true;
        var ns = type.Namespace ?? string.Empty;
        return type.Assembly.GetName().Name == "Luau.Interop" || ns == "Luau.Native" ||
            ns.StartsWith("Luau.Internal.Interop", StringComparison.Ordinal) ||
            (typeof(Delegate).IsAssignableFrom(type) && type.IsDefined(typeof(UnmanagedFunctionPointerAttribute)));
    }

    static bool IsExternallyVisible(Type type)
    {
        if (type.IsGenericType && !type.IsGenericTypeDefinition) type = type.GetGenericTypeDefinition();
        return type.IsNested ? type.IsNestedPublic && IsExternallyVisible(type.DeclaringType!) : type.IsPublic;
    }

    static string Diff(string[] expected, string[] actual)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        return string.Join(Environment.NewLine,
            new[] { "Public API differs from PublicApi.approved.txt." }
                .Concat(expected.Where(line => !actualSet.Contains(line)).Select(line => $"- {line}"))
                .Concat(actual.Where(line => !expectedSet.Contains(line)).Select(line => $"+ {line}")));
    }

    static string BaselinePath([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "PublicApi.approved.txt");
}
