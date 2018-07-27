using System;
using System.Runtime.InteropServices;

using Mono.Compiler;
using Mono.Compiler.BigStep.LLVMBackend;
using SimpleJit.Metadata;
using SimpleJit.CIL;

using LLVMSharp;

/// <summary>
///   Compile from CIL to LLVM IR (and then to native code) in one big step
///   (without using our own intermediate representation).
///
///   Basically mimic the Kaleidoscope tutorial.
/// </summary>
namespace Mono.Compiler.BigStep
{
	public class BigStep
	{
		// FIXME
		const string TargetTriple = "x86_64-apple-macosx10.12.3";

		const CompilationResult Ok = CompilationResult.Ok;
		CompilationFlags Flags { get; }
		IRuntimeInformation RuntimeInfo { get; }

		public BigStep (IRuntimeInformation runtimeInfo, CompilationFlags flags)
		{
			this.Flags = flags;
			this.RuntimeInfo = runtimeInfo;
		}

		public CompilationResult CompileMethod (MethodInfo methodInfo, out NativeCodeHandle result)
		{
			// return CompileMethodOld(methodInfo, result);
			
			result = NativeCodeHandle.Invalid;
			try
			{
				BitCodeEmitter processor = new BitCodeEmitter (RuntimeInfo, methodInfo) {
					// PrintDebugInfo = true,
					VerifyGeneratedCode = true
				};
				CILSymbolicExecutor exec = new CILSymbolicExecutor (processor, RuntimeInfo, methodInfo);
				exec.Execute();
				result = processor.Yield();
				return CompilationResult.Ok;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				return CompilationResult.InternalError;
			}
		}

		internal static void InitializeLLVM_OSX_AMD64 (LLVMMCJITCompilerOptions mcjitCompilerOptions)
		{
			LLVM.LinkInMCJIT ();
			LLVM.InitializeX86TargetMC ();
			LLVM.InitializeX86Target ();
			LLVM.InitializeX86TargetInfo ();
			LLVM.InitializeX86AsmParser ();
			LLVM.InitializeX86AsmPrinter ();
			LLVM.InitializeX86Disassembler ();

			/* this looks like unused code, but it initializes the target configuration */
			LLVMTargetRef target = LLVM.GetTargetFromName("x86-64");
			LLVMTargetMachineRef tmachine = LLVM.CreateTargetMachine(
						target,
						TargetTriple,
						"x86-64",  // processor
						"",  // feature
						LLVMCodeGenOptLevel.LLVMCodeGenLevelNone,
						LLVMRelocMode.LLVMRelocDefault,
						LLVMCodeModel.LLVMCodeModelDefault);
			/* </side effect code> */

			LLVM.InitializeMCJITCompilerOptions(mcjitCompilerOptions);
		}

	}
		
}
