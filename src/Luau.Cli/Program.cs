using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZLinq;

if (args.Length == 0)
{
    return await Commands.Repl([]);
}

var command = args[0];
return command switch
{
    "analyze" => await Commands.Analyze(args),
    "ast" => await Commands.Ast(args),
    "compile" => await Commands.Compile(args),
    "dluau" => Commands.Dluau(args.AsSpan(1).ToArray()),
    _ => await Commands.Repl(args),
};

static class Commands
{
    public static async Task<int> Repl(string[] args)
    {
        return await RunProcessAsync("luau", args);
    }

    public static async Task<int> Analyze(string[] args)
    {
        return await RunProcessAsync("luau-analyze", args);
    }

    public static async Task<int> Ast(string[] args)
    {
        return await RunProcessAsync("luau-ast", args);
    }

    public static async Task<int> Compile(string[] args)
    {
        return await RunProcessAsync("luau-compile", args);
    }

    static async Task<int> RunProcessAsync(string toolName, string[] args)
    {
        var currentAssemblyLocation = AppContext.BaseDirectory;
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

        string toolPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "win", toolName + ext);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "linux", toolName + ext);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "osx", toolName + ext);
        }
        else
        {
            Console.WriteLine("Unsupported operating system");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = string.Join(' ', args),
            UseShellExecute = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Analyzes C# files in the project and generates .d.luau file
    /// </summary>
    /// <param name="path">The path of the file or directory</param>
    /// <param name="output">-o|Output path</param>
    /// <returns></returns>
    public static int Dluau(string[] args)
    {
        if (args.Any(x => x is "-h" or "--help"))
        {
            Console.WriteLine("Usage: dotnet luau dluau [path] [options]");
            Console.WriteLine();
            Console.WriteLine("Analyzes C# files in the project and generates .d.luau file");
            Console.WriteLine();
            Console.WriteLine("Available options:");
            Console.WriteLine("  -h, --help: Display this usage message.");
            Console.WriteLine("  -o, --output: output path");
            return 0;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Required argument 'path' was not specified.");
            return 1;
        }

        var path = args[0];

        string? output = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--output")
            {
                if (i == args.Length - 1)
                {
                    Console.WriteLine("Argument 'output' was not specified.");
                    return 1;
                }

                output = args[i + 1];
                i++;
            }
        }

        var outputPath = output ?? Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + ".d.luau");

        if (File.Exists(path))
        {
            var writer = File.CreateText(outputPath);
            writer.Write(CreateLuauDeclarationFileCore(path));
            writer.Flush();
            return 0;
        }
        else if (Directory.Exists(path))
        {
            var writer = File.CreateText(outputPath);

            foreach (var info in new DirectoryInfo(path)
                .DescendantsAndSelf()
                .OfType<FileInfo>()
                .Where(x => x.Extension is ".cs"))
            {
                writer.Write(CreateLuauDeclarationFileCore(info.FullName));
            }

            writer.Flush();
            return 0;
        }
        else
        {
            Console.WriteLine($"File or directory '{path}' not found.");
            return 1;
        }
    }

    static string CreateLuauDeclarationFileCore(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath);

        var builder = new CodeBuilder(0);

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is not TypeDeclarationSyntax declaration) continue;

            var libraryAttribute = GetAttributes(declaration).FirstOrDefault(x => x.Name.ToString() is "LuauLibrary" or "Luau.LuauLibrary" or "global::Luau.LuauLibrary");
            if (libraryAttribute == null) continue;

            var libraryName = libraryAttribute.ArgumentList!.Arguments[0].ToString().Replace("\"", "");

            using (builder.BeginBlock($"declare {libraryName}:"))
            {
                foreach (var member in declaration.Members)
                {
                    var memberAttribute = GetAttributes(member).FirstOrDefault(x => x.Name.ToString() is "LuauMember" or "Luau.LuauMember" or "global::Luau.LuauMember");
                    if (memberAttribute == null) continue;

                    var luauMemberName = memberAttribute.ArgumentList?.Arguments[0].ToString().Replace("\"", "");
                    switch (member)
                    {
                        case FieldDeclarationSyntax field:
                            var fieldName = luauMemberName ?? field.Declaration.Variables.First().Identifier.Text;
                            builder.AppendLine($"{fieldName}: {LuauTypeHelper.GetLuauType(field.Declaration.Type)},");
                            break;
                        case PropertyDeclarationSyntax property:
                            var propertyName = luauMemberName ?? property.Identifier.Text;
                            builder.AppendLine($"{propertyName}: {LuauTypeHelper.GetLuauType(property.Type)},");
                            break;
                        case MethodDeclarationSyntax method:
                            var methodName = luauMemberName ?? method.Identifier.Text;
                            var parameters = method.ParameterList.Parameters
                                .Select(x => (Name: x.Identifier.Text, Type: LuauTypeHelper.GetLuauType(x.Type!)))
                                .Select(x => $"{x.Name}: {x.Type}");
                            builder.AppendLine($"{methodName}: ({string.Join(", ", parameters)}) -> {LuauTypeHelper.GetLuauType(method.ReturnType)},");
                            break;
                    }
                }
            }
        }

        return builder.ToString();
    }

    static AttributeSyntax[] GetAttributes(MemberDeclarationSyntax syntax)
    {
        return syntax.AttributeLists
            .AsValueEnumerable()
            .SelectMany(x => x.Attributes)
            .ToArray();
    }
}