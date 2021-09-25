using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.CodeDom.Compiler;
using System.Threading.Tasks;
using System.Collections.Generic;

using CommandLine;

namespace RuntimeCompiler
{
#pragma warning disable CA1416
    public class Program
    {
        public static string[] Input = new string[] { $"{Directory.GetCurrentDirectory()}/Source.txt" };
        public static string DefaultOutputPath = $"{Directory.GetCurrentDirectory()}/Output.dll";

        public static bool Debug = true;
        public static bool ProduceAssembly = false;

        public static Assembly Loaded;

        public static async Task Main(string[] args)
        {
            await Log($"Loading RuntimeCompiler ...");

            if (args != null && args.Length > 0)
            {
                Arguments arg = null;

                try
                {
                    arg = Parser.Default.ParseArguments<Arguments>(args).Value;
                }
                catch (Exception e)
                {
                    await Log($"An error occured while parsing startup arguments ..", 2);
                    await Log(e, 2);
                }

                if (arg == null)
                {
                    await Log("Failed to parse startup arguments.", 1);
                }
                else
                {
                    Input = arg.Input;
                    DefaultOutputPath = arg.Output;
                    ProduceAssembly = arg.GenerateOutput;
                    Debug = arg.Debug;
                }
            }

            if (ProduceAssembly)
                await Log($"An assembly WILL be generated.", 0);

            await Log($"Loading source files .. ({Input.Length}) ..", 0);

            var validInput = new HashSet<string>(Input.Length);

            foreach (var inp in Input)
            {
                if (!File.Exists(inp))
                {
                    await Log($"One of the files in the input ({inp}) is invalid, removing ..", 1);

                    continue;
                }

                validInput.Add(inp);
            }

            HashSet<Assembly> assemblies = new HashSet<Assembly>();

            foreach (string file in Directory.GetFiles(Directory.GetCurrentDirectory()))
            {
                if (!file.EndsWith(".dll"))
                    continue;

                try
                {
                    assemblies.Add(Assembly.Load(await File.ReadAllBytesAsync(file)));
                }
                catch { continue; }
            }

            await Log($"Referencing {assemblies.Count} assemblies ..");

            try
            {
                await Log("Creating a compiler ..", 3);

                var compiler = CodeDomProvider.CreateProvider(CodeDomProvider.GetLanguageFromExtension(".cs"));
                var param = new CompilerParameters(assemblies.Select(x => x.GetName().Name + ".dll").ToArray(), DefaultOutputPath);

                param.GenerateExecutable = ProduceAssembly;
                param.GenerateInMemory = !ProduceAssembly;

                await Log("Compiling ...", 0);

                var res = compiler.CompileAssemblyFromFile(param, validInput.ToArray());

                if (res.Errors.Count != 0)
                {
                    await Log($"The compiler has generated {res.Errors.Count} error(s).", 2);

                    await LogCompilerAsync(res.Errors);
                }
                else
                {
                    if (ProduceAssembly)
                    {
                        await Log($"The compiled assembly is located at {res.PathToAssembly}");

                        await Task.Delay(1000);

                        await ExitAsync("Compilation was succesfull.");
                    }
                    else
                    {
                        await Log($"The compiled assembly has been loaded into the memory. You can use commands to interact with it. To list" +
                            $"all commands use CMDS.");

                        Loaded = res.CompiledAssembly;
                    }
                }
            }
            catch (Exception e)
            { 
                await ExitAsync(e.ToString());
            }
        }

        public static async Task HandleAsync(string line)
        {
            string[] args = line.Split(' ');

            if (args.Length < 1)
                return;

            switch (args[0].ToUpper())
            {
                case "CMDS":
                    {
                        await Log("CMDS", 4);
                        await Log("LIST", 4);
                        await Log("EXECUTE", 4);

                        return;
                    }
                case "LIST":
                    {
                        if (args.Length < 2)
                        {
                            await Log("You need to supply more arguments for this command (FIELD, PROPERTY, METHOD, TYPE)");

                            return;
                        }    

                        switch (args[1].ToUpper())
                        {
                            case "FIELD":
                                {
                                    foreach (Type type in Loaded.GetTypes())
                                    {
                                        foreach (var field in type.GetFields())
                                        {
                                            await Log($"{field.FieldType.FullName}::{field.DeclaringType.FullName}.{field.Name}", 4);
                                        }
                                    }

                                    return;
                                }

                            case "PROPERTY":
                                {
                                    foreach (Type type in Loaded.GetTypes())
                                    {
                                        foreach (var prop in type.GetProperties())
                                        {
                                            var mod = "";

                                            if (prop.CanRead)
                                                mod += "get/";

                                            if (prop.CanWrite)
                                                mod += "set";

                                            await Log($"{mod} {prop.PropertyType.FullName}::{prop.DeclaringType.FullName}.{prop.Name}", 4);
                                        }
                                    }

                                    return;
                                }

                            case "METHOD":
                                {
                                    foreach (Type type in Loaded.GetTypes())
                                    {
                                        foreach (var method in type.GetMethods())
                                        {
                                            await Log($"{method.ReturnType.FullName}::{method.DeclaringType.FullName}.{method.Name}", 4);
                                        }
                                    }

                                    return;
                                }
                        
                            case "TYPE":
                                {
                                    foreach (Type type in Loaded.GetTypes())
                                    {
                                        var modifiers = "";
                                        var types = "";

                                        if (type.IsPublic)
                                            modifiers += "public ";
                                        else if (type.IsNotPublic)
                                            modifiers += "private ";

                                        if (type.IsSealed && type.IsAbstract)
                                            modifiers += "static ";

                                        if (type.IsSealed && !type.IsAbstract)
                                            modifiers += "sealed ";

                                        if (!type.IsSealed && type.IsAbstract)
                                            modifiers += "abstract ";

                                        if (type.IsEnum)
                                            types += "enum ";

                                        if (type.IsCollectible)
                                            types += "collectible ";

                                        if (type.IsGenericType)
                                            types += "generic ";

                                        if (type.IsAnsiClass)
                                            types += "ansi ";

                                        if (type.IsValueType)
                                            types += "valuetype ";

                                        if (type.IsArray)
                                            types += "array ";

                                        await Log($"{modifiers} {type.FullName} ({types})", 4);
                                    }

                                    return;
                                }
                        }

                        return;
                    }

                case "EXECUTE":
                    {
                        var un = line.Replace("EXECUTE ", "").Split("::");

                        if (un.Length < 2)
                        {
                            await Log("You have to supply a type and a method in this format: typeName::methodName", 4);

                            return;
                        }

                        string typeName = un[0];
                        string methodName = un[1];

                        var type = Loaded.GetType(typeName);

                        if (type == null)
                        {
                            await Log($"Type {typeName} not found.", 4);

                            return;
                        }

                        var method = type.GetMethod(methodName);

                        if (method == null)
                        {
                            await Log($"Method {type.FullName}::{methodName} not found.", 4);

                            return;
                        }

                        if (!method.IsStatic)
                        {
                            await Log($"This method is not static which makes it complicated to invoke.", 4);

                            return;
                        }

                        if (method.GetParameters().Length > 0)
                        {
                            await Log($"This method has overloads, unfortunately there is no way to easily execute a method with unknown parameters.", 4);

                            return;
                        }

                        var returnedObject = method.Invoke(null, null);

                        if (returnedObject != null)
                        {
                            await Log($"Method executed, it has returned an object of type {returnedObject.GetType().FullName} with value: {returnedObject}");
                        }
                        else
                        {
                            await Log($"Method executed, it has returned a null object.", 4);
                        }

                        return;
                    }
            }
        }

        public static async Task CheckForCommandsAsync()
        {
            while (true)
            {
                await Task.Delay(100);

                var line = Console.ReadLine();

                if (line != null)
                    await HandleAsync(line);
            }
        }

        public static async Task ExitAsync(string code)
        {
            await Log(code, 2);

            await Task.Delay(2000);

            await Log("Exiting ...", 0);

            await Task.Delay(1000);

            Environment.Exit(0);
        }

        public static async Task LogCompilerAsync(CompilerErrorCollection errors)
        {
            foreach (CompilerError e in errors)
            {
                Console.ForegroundColor = e.IsWarning ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.Write($"[{(e.IsWarning ? "WARNING" : "ERROR")}] ");
                Console.Write($"[{e.ErrorNumber}] {e.ErrorText} (File: {e.FileName}, line: {e.Line}, column: {e.Column})");
                Console.Write(Environment.NewLine);
                Console.ResetColor();

                await Task.Delay(600);
            }
        }

        public static async Task Log(object message, int type = 0)
        {
            var stamp = DateTime.Now.ToLocalTime().ToString("T");
            var tagColor = ConsoleColor.Yellow;

            switch (type)
            {
                case 0:
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"[{stamp}] ");
                        Console.ForegroundColor = tagColor;
                        Console.Write($"[INFO] ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write(message);
                        Console.Write(Environment.NewLine + Environment.NewLine);
                        Console.ResetColor();
                        break;
                    }
                case 1:
                    tagColor = ConsoleColor.DarkYellow;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{stamp}] ");
                    Console.ForegroundColor = tagColor;
                    Console.Write($"[WARN] ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(message);
                    Console.Write(Environment.NewLine + Environment.NewLine);
                    Console.ResetColor();
                    break;
                case 2:
                    tagColor = ConsoleColor.Red;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{stamp}] ");
                    Console.ForegroundColor = tagColor;
                    Console.Write($"[ERROR] ");
                    Console.ForegroundColor = tagColor;
                    Console.Write(message);
                    Console.Write(Environment.NewLine + Environment.NewLine);
                    Console.ResetColor();
                    break;
                case 3:
                    if (!Debug) return;
                    tagColor = ConsoleColor.White;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{stamp}] ");
                    Console.ForegroundColor = tagColor;
                    Console.Write($"[DEBUG] ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(message);
                    Console.Write(Environment.NewLine + Environment.NewLine);
                    Console.ResetColor();
                    break;
                case 4:
                    tagColor = ConsoleColor.Green;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{stamp}] ");
                    Console.ForegroundColor = tagColor;
                    Console.Write($"[COMMAND OUTPUT] ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(message);
                    Console.Write(Environment.NewLine + Environment.NewLine);
                    Console.ResetColor();
                    break;
                default:
                    break;
            }

            await Task.Delay(600);
        }
    }

    public class Arguments
    {
        [Option('i', "input", Required = false, HelpText = "The input source file.")]
        public string[] Input { get; set; } = new string[] { $"{Directory.GetCurrentDirectory()}/Source.txt" };

        [Option('o', "output", Required = false, HelpText = "The output DLL file.")]
        public string Output { get; set; } = $"{Directory.GetCurrentDirectory()}/Output.dll";

        [Option('g', "generate", Default = false)]
        public bool GenerateOutput { get; set; } = false;

        [Option('d', "debug", Default = true)]
        public bool Debug { get; set; } = true;
    }
}