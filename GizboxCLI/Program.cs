using System;
using System.Collections.Generic;
using Gizbox;

namespace GizboxCLI
{
    internal class Program
    {
        /// <summary>
        /// 命令行入口。
        /// </summary>
        private static int Main(string[] args)
        {
            try
            {
                if(args.Length == 0)
                {
                    PrintHelp();
                    return 1;
                }

                foreach(var arg in args)
                {
                    if(arg == "--help" || arg == "-h")
                    {
                        PrintHelp();
                        return 0;
                    }
                    if(arg == "--version" || arg == "-v")
                    {
                        PrintVersion();
                        return 0;
                    }
                }

                var cliOptions = ParseArgs(args);
                if(cliOptions == null)
                    throw new Exception("解析命令行参数失败");

                string outputPath = InvokeCompile(cliOptions.InputFile, cliOptions);
                Console.WriteLine(outputPath);
                return 0;
            }
            catch(ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("使用 --help 查看帮助。");
                return 1;
            }
            catch(GizboxException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 3;
            }
        }


        private static string InvokeCompile(string inputFile, CliOptions options)
        {
            Compiler compiler = new Compiler();

            //读取源代码
            string src = System.IO.File.ReadAllText(inputFile);


            //输出目录  
            string outputPath = (options.OutputPath ?? string.Empty);
            if(string.IsNullOrWhiteSpace(outputPath))
            {
                string defaultExt = options.OutputKind switch
                {
                    CompileOutputKind.GixLib => ".gixlib",
                    CompileOutputKind.Dll => ".dll",
                    _ => ".exe",
                };
                string inputDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(inputFile)) ?? Environment.CurrentDirectory;
                string inputBaseName = System.IO.Path.GetFileNameWithoutExtension(inputFile);
                outputPath = System.IO.Path.Combine(inputDir, inputBaseName + defaultExt);
            }
            outputPath = System.IO.Path.GetFullPath(outputPath);
            string outputDir = System.IO.Path.GetDirectoryName(outputPath);
            if(string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Environment.CurrentDirectory;
            }
            string outputName = System.IO.Path.GetFileNameWithoutExtension(outputPath);
            System.IO.Directory.CreateDirectory(outputDir);


            //添加库搜索路径（Gizbox库和原生库通用）
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            compiler.AddLibPath(Environment.CurrentDirectory);
            foreach(var path in options.LibPaths)
                compiler.AddLibPath(path);
            foreach(var path in options.NativeLibPaths)
                compiler.AddLibPath(path);


            //使用硬编码的语法分析器  
            compiler.ConfigParserDataSource(true);



            //输出类型
            switch(options.OutputKind)
            {
                case CompileOutputKind.GixLib:
                    {
                        compiler.CompileToLib(src, outputName, outputPath);
                        return outputPath;
                    }
                case CompileOutputKind.Exe:
                    {
                        //编译配置  
                        CompileOptions compileOptions = new CompileOptions()
                        {
                            platform = Platform.Windows_X64,
                            buildMode = BuildMode.Release,
                            libs = new List<string>(options.NativeLibs),
                            dlls = new List<string>(options.RuntimeDlls),
                        };


                        var ir = compiler.CompileToIR(src, isMainUnit: true, outputName);
                        compiler.ComileIRToBin(ir, outputDir, compileOptions, shared: false);

                        return outputPath;
                    }
                case CompileOutputKind.Dll:
                    {
                        //编译配置  
                        CompileOptions compileOptions = new CompileOptions()
                        {
                            platform = Platform.Windows_X64,
                            buildMode = BuildMode.Release,
                            libs = new List<string>(options.NativeLibs),
                            dlls = new List<string>(options.RuntimeDlls),
                        };


                        var ir = compiler.CompileToIR(src, isMainUnit: true, outputName);
                        compiler.ComileIRToBin(ir, outputDir, compileOptions, shared: true);

                        return outputPath;
                    }
            }

            throw new ArgumentOutOfRangeException(nameof(options.OutputKind), options.OutputKind, "未知输出类型");
        }

        /// <summary>
        /// 解析命令行参数。
        /// </summary>
        private static CliOptions ParseArgs(string[] args)
        {
            CliOptions options = new CliOptions();

            for(int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if(arg == "-shared" || arg == "--shared")
                {
                    SetOutputKind(options, CompileOutputKind.Dll, arg);
                    continue;
                }
                if(arg == "--gixlib")
                {
                    SetOutputKind(options, CompileOutputKind.GixLib, arg);
                    continue;
                }
                if(arg == "-o")
                {
                    options.OutputPath = ReadValue(args, ref i, arg);
                    continue;
                }
                if(arg.StartsWith("-o", StringComparison.Ordinal) && arg.Length > 2)
                {
                    options.OutputPath = arg.Substring(2);
                    continue;
                }
                if(arg == "-I")
                {
                    options.LibPaths.Add(ReadValue(args, ref i, arg));
                    continue;
                }
                if(arg.StartsWith("-I", StringComparison.Ordinal) && arg.Length > 2)
                {
                    options.LibPaths.Add(arg.Substring(2));
                    continue;
                }
                if(arg == "-L")
                {
                    options.NativeLibPaths.Add(ReadValue(args, ref i, arg));
                    continue;
                }
                if(arg.StartsWith("-L", StringComparison.Ordinal) && arg.Length > 2)
                {
                    options.NativeLibPaths.Add(arg.Substring(2));
                    continue;
                }
                if(arg == "-l")
                {
                    options.NativeLibs.Add(ReadValue(args, ref i, arg));
                    continue;
                }
                if(arg.StartsWith("-l", StringComparison.Ordinal) && arg.Length > 2)
                {
                    options.NativeLibs.Add(arg.Substring(2));
                    continue;
                }
                if(arg == "--dll")
                {
                    options.RuntimeDlls.Add(ReadValue(args, ref i, arg));
                    continue;
                }
                if(arg.StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"未知参数: {arg}");
                }
                if(string.IsNullOrWhiteSpace(options.InputFile) == false)
                {
                    throw new ArgumentException("暂只支持一个输入文件。");
                }

                options.InputFile = arg;
            }

            if(string.IsNullOrWhiteSpace(options.InputFile))
                throw new ArgumentException("缺少输入文件。 ");

            return options;
        }

        /// <summary>
        /// 读取当前参数的值  
        /// </summary>
        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            if(index + 1 >= args.Length)
                throw new ArgumentException($"参数 {optionName} 缺少值。");

            index++;
            return args[index];
        }

        /// <summary>
        /// 设置输出类型并检查冲突  
        /// </summary>
        private static void SetOutputKind(CliOptions options, CompileOutputKind outputKind, string optionName)
        {
            if(options.OutputKind != CompileOutputKind.Exe && options.OutputKind != outputKind)
            {
                throw new ArgumentException($"参数 {optionName} 与当前输出类型冲突。");
            }

            options.OutputKind = outputKind;
        }

        /// <summary>
        /// 打印帮助信息  
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("gizboxc <input.gix> [options]");
            Console.WriteLine();
            Console.WriteLine("  -o <path>        指定输出文件");
            Console.WriteLine("  -I <dir>         指定 Gizbox 库搜索路径");
            Console.WriteLine("  -L <dir>         指定原生库搜索路径");
            Console.WriteLine("  -l <file>        链接原生静态库或导入库");
            Console.WriteLine("  --dll <file>     复制并参与动态链接的 dll");
            Console.WriteLine("  -shared          输出 dll");
            Console.WriteLine("  --gixlib         输出 gixlib");
            Console.WriteLine("  --help, -h       显示帮助");
            Console.WriteLine("  --version, -v    显示版本");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  gizboxc main.gix -o app.exe");
            Console.WriteLine("  gizboxc main.gix --gixlib -o core.gixlib");
            Console.WriteLine("  gizboxc main.gix -shared -o demo.dll -L native -l demo.dll.a --dll demo.dll");
        }

        /// <summary>
        /// 打印版本信息
        /// </summary>
        private static void PrintVersion()
        {
            var version = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"gizboxc {version}");
        }
    }

    public enum CompileOutputKind
    {
        Exe,
        Dll,
        GixLib,
    }

    public sealed class CliOptions
    {
        public string InputFile { get; set; } = string.Empty;

        public CompileOutputKind OutputKind { get; set; }
        public string? OutputPath { get; set; }
        public List<string> LibPaths { get; } = new List<string>();
        public List<string> NativeLibPaths { get; } = new List<string>();
        public List<string> NativeLibs { get; } = new List<string>();
        public List<string> RuntimeDlls { get; } = new List<string>();
    }


}
