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
				BitCodeEmitter processor = new BitCodeEmitter(methodInfo){
					// PrintDebugInfo = true,
					VerifyGeneratedCode = true
				};
				CILSymbolicExecutor exec = new CILSymbolicExecutor(processor, RuntimeInfo, methodInfo);
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

		private CompilationResult CompileMethodOld (MethodInfo methodInfo, out NativeCodeHandle result)
		{
			var builder = new Builder ();
			var env = new Env (RuntimeInfo, methodInfo);

			Preamble (env, builder);

			result = NativeCodeHandle.Invalid;
			var r = TranslateBody (env, builder, methodInfo.Body);
			if (r != Ok)
				return r;
			r = builder.Finish (out result);
			return r;
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

		// translation environment for a single function
		class Env {
			private ArgStack currentStack;
			private IRuntimeInformation RuntimeInfo { get; }
			internal ArgStack ArgStack { get => currentStack ; }
			public Env (IRuntimeInformation runtimeInfo, MethodInfo methodInfo)
			{
				this.RuntimeInfo = runtimeInfo;
				this.MethodName = methodInfo.ClassInfo.Name + "::" + methodInfo.Name;
				this.BSTypes = new BSTypes (runtimeInfo);
				this.ReturnType = methodInfo.ReturnType;

				var parameters = methodInfo.Parameters;
				this.ArgumentTypes = new ClrType [parameters.Count];
				int i = 0;
				foreach (ParameterInfo pi in parameters)
					this.ArgumentTypes [i++] = pi.ParameterType;

				currentStack = new ArgStack ();
			}

			public ClrType ReturnType { get; }
			public ClrType[] ArgumentTypes { get; }

			public readonly BSTypes BSTypes;
			public readonly string MethodName;
		}

		// encapsulate the LLVM module and builder here.
		class Builder {
			static readonly LLVMBool Success = new LLVMBool (0);

			LLVMModuleRef module;
			LLVMBuilderRef builder;
			LLVMValueRef function;
			LLVMBasicBlockRef entry;
			LLVMBasicBlockRef currentBB;
			LLVMValueRef[] arguments;

			public LLVMModuleRef Module { get => module; }
			public LLVMValueRef Function { get => function; }

			public Builder () {
				module = LLVM.ModuleCreateWithName ("BigStepCompile");
				builder = LLVM.CreateBuilder ();
			}

			public void BeginFunction (string name, BSType returnType, BSType[] args) {
				var llvm_arguments = new LLVMTypeRef [args.Length];
				for (int i = 0; i < args.Length; i++)
					llvm_arguments [i] = args [i].Lowered;

				var funTy = LLVM.FunctionType (returnType.Lowered, llvm_arguments, false);
				function = LLVM.AddFunction (module, name, funTy);
				entry = LLVM.AppendBasicBlock (function, "entry");
				LLVM.PositionBuilderAtEnd (builder, entry);
				currentBB = entry;

				arguments = new LLVMValueRef [args.Length];
				for (int i = 0; i < args.Length; i++) {
					arguments [i] = LLVM.GetParam (function, (uint) i);
					LLVM.SetValueName (arguments [i], "arg" + i);
				}
			}

			internal unsafe void PrintDisassembly (NativeCodeHandle nch) {
				IntPtr fnptr = new IntPtr (nch.Blob);

				// FIXME: do this once
				LLVMDisasmContextRef disasm = LLVM.CreateDisasm (TargetTriple, IntPtr.Zero, 0, null, null);
				LLVM.SetDisasmOptions (disasm, 2 /* print imm as hex */);

				// FIXME: use codebuf length
				const long maxlength = 0x100;
				long pc = 0;

				Console.WriteLine ("disasm:");
				while (pc < maxlength) {
					const int stringBufSize = 0x40;
					IntPtr outString = Marshal.AllocHGlobal (stringBufSize);

					long oldPc = pc;
					pc += LLVM.DisasmInstruction (disasm, IntPtr.Add (fnptr, (int) pc), 0x100, 0, outString, stringBufSize);

					string s = Marshal.PtrToStringAnsi (outString);

					/* HACK because we don't know codbuf length; this is the disassembled string of 0x00 0x00 */
					if (s.Contains ("addb") && s.Contains ("%al, (%rax)")) {
						break;
					}
					Console.WriteLine ($"{oldPc:x4}: {s}");
				}
			}

			internal CompilationResult Finish (out NativeCodeHandle result) {

				// FIXME: get rid of this printf
				LLVM.DumpModule (Module);

				LLVMMCJITCompilerOptions options = new LLVMMCJITCompilerOptions { NoFramePointerElim = 0 };

				BigStep.InitializeLLVM_OSX_AMD64 (options);

				if (LLVM.CreateMCJITCompilerForModule(out var engine, Module, options, out var error) != Success)
				{
					/* FIXME: If I make completely bogus LLVM IR, I would expect to
					 * fail here and get some kind of error, but I don't.
					 */
					Console.Error.WriteLine($"Error: {error}");
					result = NativeCodeHandle.Invalid;
					return CompilationResult.BadCode;
				}
				IntPtr fnptr = LLVM.GetPointerToGlobal (engine, Function);
				if (fnptr == IntPtr.Zero) {
					result = NativeCodeHandle.Invalid;
					Console.Error.WriteLine ("LLVM.GetPointerToGlobal returned null");
					return CompilationResult.InternalError;
				} else {
					Console.Error.WriteLine ("saw {0}", fnptr);
				}
				unsafe {
					result = new NativeCodeHandle ((byte*)fnptr, -1);
				}

				// FIXME: guard behind debug flag
				PrintDisassembly (result);

				//FIXME: cleanup in a Dispose method?

				LLVM.DisposeBuilder (builder);

				// FIXME: can I really dispose of the EE while code is installed in Mono :-(

				// LLVM.DisposeExecutionEngine (engine);

				return Ok;
			}

			public LLVMValueRef ConstInt (BSType t, ulong value, bool signextend)
			{
				return LLVM.ConstInt (t.Lowered, value, signextend);
			}

			public void EmitRetVoid () {
				LLVM.BuildRetVoid (builder);
			}

			public void EmitRet (LLVMValueRef v)
			{
				LLVM.BuildRet (builder, v);
			}

			public LLVMValueRef EmitAlloca (BSType t, string nameHint)
			{
				return LLVM.BuildAlloca (builder, t.Lowered, nameHint);
			}

			public LLVMValueRef EmitLoad (LLVMValueRef ptr, string nameHint)
			{
				return LLVM.BuildLoad (builder, ptr, nameHint);
			}

			public LLVMValueRef EmitArgumentLoad (uint position)
			{
				return arguments [position];
			}

			public void EmitStore (LLVMValueRef value, LLVMValueRef ptr)
			{
				LLVM.BuildStore (builder, value, ptr);
			}

			public LLVMValueRef EmitAdd (LLVMValueRef left, LLVMValueRef right)
			{
				return LLVM.BuildBinOp (builder, LLVMOpcode.LLVMAdd, left, right, "add");
			}
		}

		void Preamble (Env env, Builder builder)
		{
			var rt = LowerType (env, env.ReturnType);

			BSType[] args = new BSType [env.ArgumentTypes.Length];
			for (int i = 0; i < env.ArgumentTypes.Length; i++)
				args [i] = LowerType (env, env.ArgumentTypes [i]);

			builder.BeginFunction (env.MethodName, rt, args);
		}

		CompilationResult TranslateBody (Env env, Builder builder, MethodBody body)
		{
			var iter = body.GetIterator ();
			// TODO: alloca for locals and stack; store in env

			var r = Ok;

			while (iter.MoveNext ()) {
				var opcode = iter.Opcode;
				var opflags = iter.Flags;
				switch (opcode) {
					case Opcode.LdcI4_0:
					case Opcode.LdcI4_1:
					case Opcode.LdcI4_2:
						r = TranslateLdcI4 (env, builder, opcode - Opcode.LdcI4_0);
						break;
					case Opcode.LdcI4S:
						r = TranslateLdcI4 (env, builder, iter.DecodeParamI ());
						break;
					case Opcode.Ldarg0:
					case Opcode.Ldarg1:
						r = TranslateLdarg (env, builder, opcode - Opcode.Ldarg0);
						break;
					case Opcode.Add:
						// TODO: pass op
						r = TranslateBinaryOp (env, builder);
						break;
					case Opcode.Ret:
						r = TranslateRet (env, builder);
						break;
					default:
						throw NIE ($"BigStep.Translate {opcode}");
				}
				if (r != Ok)
					break;
			}
			return r;
		}

		CompilationResult TranslateRet (Env env, Builder builder)
		{
			if (env.ReturnType == RuntimeInfo.VoidType) {
				builder.EmitRetVoid ();
				return Ok;
			} else {
				var a = Pop (env, builder);
				var v = builder.EmitLoad (a.Ptr, "ret-value");
				builder.EmitRet (v);
				return Ok;
			}
				
		}

		CompilationResult TranslateLdcI4 (Env env, Builder builder, System.Int32 c)
		{
			BSType t = env.BSTypes.Int32Type;
			var a = Push (env, builder, t);
			
			var v = builder.ConstInt (t, (ulong)c, false);
			builder.EmitStore (v, a.Ptr);
			return Ok;
		}

		CompilationResult TranslateLdarg (Env env, Builder builder, uint position)
		{
			var t = LowerType (env, env.ArgumentTypes [position]);
			var a = Push (env, builder, t);
			var v = builder.EmitArgumentLoad (position);
			builder.EmitStore (v, a.Ptr);
			return Ok;
		}

		CompilationResult TranslateBinaryOp (Env env, Builder builder)
		{
			var a0 = Pop (env, builder);
			var a1 = Pop (env, builder);
			if (a0.StoredType != a1.StoredType) {
				Console.Error.WriteLine ("BinOp: Types of operands do not match");
				return CompilationResult.InternalError;
			}

			var v0 = builder.EmitLoad (a0.Ptr, "summand0");
			var v1 = builder.EmitLoad (a1.Ptr, "summand1");

			var vr = builder.EmitAdd (v0, v1);

			var ar = Push (env, builder, a0.StoredType);
			builder.EmitStore (vr, ar.Ptr);

			return Ok;
		}

		ArgStackValue Push (Env env, Builder builder, BSType t)
		{
			// FIXME: create stack slots up front and just bump a
			// stack height in the env and pick out the
			// pre-allocated slot.
			var v = builder.EmitAlloca (t, "stack-slot");
			var a = new ArgStackValue ();
			a.Ptr = v;
			a.StoredType = t;
			env.ArgStack.Push (a);
			return a;
		}

		ArgStackValue Pop (Env env, Builder builder)
		{
			var a = env.ArgStack.Pop ();
			return a;
		}

		BSType LowerType (Env env, ClrType t)
		{
			if (t == RuntimeInfo.VoidType)
				return env.BSTypes.VoidType;
			else if (t == RuntimeInfo.Int32Type)
				return env.BSTypes.Int32Type;
			else
				throw NIE ($"don't know how to lower type {t}");
		}

		private static Exception NIE (string msg)
		{
			return new NotImplementedException (msg);
		}

	}
		
}
