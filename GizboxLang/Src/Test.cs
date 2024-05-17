using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

using LLVMSharp;
using System.Runtime;
using System.Threading;
//using LLVMSharp.Interop;

namespace Gizbox
{
    public unsafe class Test
    {
        public static sbyte* StringToSbyte(string input)
        {
            IntPtr llvmIRPointer = Marshal.StringToHGlobalAnsi(input);
            return (sbyte*)llvmIRPointer;
        }


//LLVMSharp5.0.0  
        public static int LLVM_Sqr(int num)
        {
            // 初始化 LLVM
            //LLVM.InitializeAllTargetInfos();
            //LLVM.InitializeAllTargets();
            //LLVM.InitializeAllTargetMCs();
            //LLVM.InitializeAllAsmParsers();
            //LLVM.InitializeAllAsmPrinters();

            Console.WriteLine("Start：LLVM SQR");


            LLVM.LinkInMCJIT();
            LLVM.InitializeX86Target();
            LLVM.InitializeX86TargetMC();
            LLVM.InitializeX86TargetInfo();
            LLVM.InitializeX86AsmParser();
            LLVM.InitializeX86AsmPrinter();

            // 定义或读取LLVM IR文本
            string llvmIR = @"define i32 @square(i32 %x) {
entry:
  %result = mul i32 %x, %x
  ret i32 %result
}
";

            Console.WriteLine("Context Create!");

            // 创建LLVM上下文
            LLVMContextRef context = LLVM.ContextCreate();
            
            Console.WriteLine("Parse!");
            // 创建模块并将IR文本解析到模块中

            LLVMMemoryBufferRef buffer = LLVM.CreateMemoryBufferWithMemoryRange(Marshal.StringToHGlobalAnsi(llvmIR), llvmIR.Length, "simple_module", true);
            LLVMModuleRef module;
            IntPtr msg;
            bool err = context.ParseIRInContext(buffer, out module, out msg);
            if (err)
            {
                throw new Exception("IR解析错误 -- (" + Marshal.PtrToStringAnsi(msg) + ")");
            }
            Console.WriteLine("Parse 输出：(" + Marshal.PtrToStringAnsi(msg) + ")");
            LLVM.DisposeMessage(msg);

            Console.WriteLine("Verify!");
            // 验证模块
            string verMsg;
            LLVM.VerifyModule(module, LLVMVerifierFailureAction.LLVMPrintMessageAction, out verMsg);
            Console.WriteLine("Verify 输出：" + verMsg);

            // 打印LLVM IR
            Console.WriteLine("模块打印：");
            Console.WriteLine(Marshal.PtrToStringAnsi(LLVM.PrintModuleToString(module)));

            Console.WriteLine("GetFunc!");
            // 查找main函数
            LLVMValueRef func = LLVM.GetNamedFunction(module, "square");
            Console.WriteLine("找到函数：" + func.GetValueName());
            Console.WriteLine("");

            Console.WriteLine("CreateEngine!");
            // 初始化执行引擎
            LLVMMCJITCompilerOptions options = new LLVMMCJITCompilerOptions();
            //LLVMOpaqueExecutionEngine* enginePtr;
            LLVMExecutionEngineRef engineRef ;
            string createMCJITmsg;
            if (LLVM.CreateMCJITCompilerForModule(out engineRef, module, options, out createMCJITmsg))
            {
                Console.WriteLine("JIT ERROR : " + createMCJITmsg);
                return -1;
            }



            Console.WriteLine("Execute!");
            // 执行main函数
            LLVMGenericValueRef[] args = new LLVMGenericValueRef[1];
            LLVMTypeRef i32Type = LLVM.Int32TypeInContext(context);
            args[0] = LLVM.CreateGenericValueOfInt(i32Type, (ulong)num, true);
            LLVMGenericValueRef result = LLVM.RunFunction(engineRef, func, args);


            // 获取结果
            Int32 resultValue = (int)LLVM.GenericValueToInt(result, true);
            Console.WriteLine("结果打印：");
            Console.WriteLine(resultValue.ToString());

            Console.WriteLine("Dispose!");
            //释放参数和返回值  
            LLVM.DisposeGenericValue(args[0]);
            LLVM.DisposeGenericValue(result);

            //// 释放资源
            //LLVM.DisposeMemoryBuffer(buffer);
            //LLVM.DisposeModule(module);
            LLVM.DisposeExecutionEngine(engineRef);
            LLVM.ContextDispose(context);

            return resultValue;
        }

        //LLVMSharp 16.0.0测试成功    
        //        public static void Foo()
        //        {
        //            // 初始化 LLVM
        //            LLVM.InitializeAllTargetInfos();
        //            LLVM.InitializeAllTargets();
        //            LLVM.InitializeAllTargetMCs();
        //            LLVM.InitializeAllAsmParsers();
        //            LLVM.InitializeAllAsmPrinters();


        //            // 定义或读取LLVM IR文本
        //            /*
        //int a = 1;
        //int b = a * a;
        //             */
        //            //string llvmIR = @"
        //            //define i32 @main() {
        //            //entry:
        //            //  %a = alloca i32
        //            //  store i32 1, i32* %a
        //            //  %a_load = load i32, i32* %a
        //            //  %b = mul i32 %a_load, %a_load
        //            //  ret i32 %b
        //            //}";
        //            string llvmIR = @"
        //define i32 @main() {
        //entry:
        //  ret i32 1
        //}";

        //            // 创建LLVM上下文
        //            LLVMContextRef context = LLVMContextRef.Create();
        //            // 创建模块并将IR文本解析到模块中
        //            LLVMMemoryBufferRef buffer = LLVM.CreateMemoryBufferWithMemoryRange(StringToSbyte(llvmIR), (nuint)llvmIR.Length, StringToSbyte("simple_module"), 0);
        //            LLVMModuleRef module = context.ParseIR(buffer);

        //            // 验证模块
        //            module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);

        //            // 打印LLVM IR
        //            Console.WriteLine("模块打印：");
        //            Console.WriteLine(module.PrintToString());

        //            // 初始化执行引擎
        //            LLVMMCJITCompilerOptions options = new LLVMMCJITCompilerOptions();
        //            LLVMOpaqueExecutionEngine* enginePtr;
        //            sbyte* msg;
        //            if (LLVM.CreateMCJITCompilerForModule(&enginePtr, module, &options, 0, &msg) != 0)
        //            {
        //                Console.WriteLine("Failed to create MCJIT compiler.");
        //                return;
        //            }
        //            LLVMExecutionEngineRef engineRef = enginePtr;

        //            // 查找main函数
        //            LLVMValueRef mainFunction = module.GetNamedFunction("main");


        //            Console.WriteLine("函数打印：");
        //            Console.WriteLine(mainFunction.PrintToString());

        //            // 执行main函数
        //            LLVMGenericValueRef[] args = new LLVMGenericValueRef[0];
        //            LLVMGenericValueRef result = engineRef.RunFunction(mainFunction, args);

        //            Console.WriteLine("结果打印：");
        //            Console.WriteLine(result.ToString());

        //            // 获取结果
        //            Int32 resultValue = (int)LLVM.GenericValueToInt(result, 0);
        //            Console.WriteLine($"Result: {resultValue}");

        //            //// 释放资源
        //            engineRef.Dispose();
        //            context.Dispose();
        //        }
    }
}
