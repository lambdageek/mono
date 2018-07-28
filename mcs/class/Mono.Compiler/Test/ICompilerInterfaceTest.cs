using System;
using NUnit.Framework;
using Mono.Compiler;

using SimpleJit.CIL;

namespace MonoTests.Mono.CompilerInterface
{
	[TestFixture]
	public class ICompilerTests
	{
		IRuntimeInformation runtimeInfo = null;
		ICompiler compiler = null;

		[TestFixtureSetUp]
		public void Init () {
			runtimeInfo = new RuntimeInformation ();
			compiler = new ManagedJIT ();
		}

		public static void EmptyMethod () {
			return;
		}

		public static int ConstIntMethod () {
			return 42;
		}

		public static int IdentityMethod (int x) {
			return x;
		}

		public static int AddMethod (int a, int b) {
			return a + b;
		}

		public static int AddMethod3 (int a, int b, int c) {
			return a + b + c;
		}

		public static int staticField = 0x1337;
		public static int StaticFieldReadMethod () {
			return staticField;
		}

                public static int IfElse1 (int a, int b) {
                        if (a > 10) {
                                b = a * b;
                        } else{
                                b = a + b;
                        }
                        
                        return b;
		}

		[Test]
		public void TestIfElse1 () {
                        InstalledRuntimeCode irc = CompileCode("IfElse1");
                        int addition = (int) runtimeInfo.ExecuteInstalledMethod (irc, 11, 2);
			Assert.AreEqual (22, addition);
                        addition = (int) runtimeInfo.ExecuteInstalledMethod (irc, 5, 2);
			Assert.AreEqual (7, addition);
                }

                private InstalledRuntimeCode CompileCode(string methodName) {
                        ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, methodName);
			NativeCodeHandle nativeCode;

			CompilationResult result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
                        return irc;
                }
                
		[Test]
		public void TestAddMethod () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "AddMethod");
			NativeCodeHandle nativeCode;

			CompilationResult result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
			
			int addition = (int) runtimeInfo.ExecuteInstalledMethod (irc, 1, 2);
			Assert.AreEqual (addition, 3);
		}

		[Test]
		public unsafe void TestInstallCompilationResultAndExecuteAddMethod () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "AddMethod");

			CompilationResult result = CompilationResult.Ok;
			byte[] amd64addblob = { 0x48, 0x8d, 0x04, 0x37, /* lea rax, [rdi + rsi * 1] */
			                        0xc3};                  /* ret */

			fixed (byte *b = amd64addblob) {
				NativeCodeHandle nativeCode = new NativeCodeHandle (b, amd64addblob.Length);

				InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);

				int sum = (int) runtimeInfo.ExecuteInstalledMethod (irc, 1, 2);
				Assert.AreEqual (3, sum);

				/* test result against host execution engine */
				sum = (int) runtimeInfo.ExecuteInstalledMethod (irc, 1337, 666);
				Assert.AreEqual (AddMethod (1337, 666), sum);
			}
		}

		[Test]
		public unsafe void TestInstallCompilationResultAndExecuteAddMethod3 () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "AddMethod3");

			CompilationResult result = CompilationResult.Ok;
			byte[] amd64addblob = { 0x48, 0x01, 0xf7,       /* add rdi, rsi */
			                        0x48, 0x8d, 0x04, 0x17, /* lea rax, [rdi + rdx * 1] */
			                        0xc3};                  /* ret */

			fixed (byte *b = amd64addblob) {
				NativeCodeHandle nativeCode = new NativeCodeHandle (b, amd64addblob.Length);

				InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);

				int sum = (int) runtimeInfo.ExecuteInstalledMethod (irc, 1, 2, 3);
				Assert.AreEqual (6, sum);

				/* test result against host execution engine */
				sum = (int) runtimeInfo.ExecuteInstalledMethod (irc, 1337, 666, 0xbeef);
				Assert.AreEqual (AddMethod3 (1337, 666, 0xbeef), sum);
			}
		}

		[Test]
		public void TestRetrieveBytecodes () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "AddMethod");

			Assert.AreEqual (4, mi.Body.Body.Length);

			var it = mi.Body.GetIterator ();

			var move1 = it.MoveNext ();
			Assert.IsTrue (move1);
			Assert.AreEqual (Opcode.Ldarg0, it.Opcode, "instr 1");
			Assert.IsTrue (it.HasNext);

			var move2 = it.MoveNext ();
			Assert.IsTrue (move2);
			Assert.AreEqual (Opcode.Ldarg1, it.Opcode, "instr 2");
			Assert.IsTrue (it.HasNext);

			var move3 = it.MoveNext ();
			Assert.IsTrue (move3);
			Assert.AreEqual (Opcode.Add, it.Opcode, "instr 3");
			Assert.IsTrue (it.HasNext);

			var move4 = it.MoveNext ();
			Assert.IsTrue (move4);
			Assert.AreEqual (Opcode.Ret, it.Opcode, "instr 4");
			Assert.IsFalse (it.HasNext);

			var move5 = it.MoveNext ();
			Assert.IsFalse (move5);
		}

		[Test]
		public unsafe void TestSimpleRet () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);

			byte[] input = { 0x2a /* OpCodes.Ret*/ };
			var body = new SimpleJit.Metadata.MethodBody (input, 0, false, 0, Array.Empty<SimpleJit.Metadata.LocalVariableInfo>());
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "EmptyMethod");
			NativeCodeHandle nativeCode;

			var result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
			runtimeInfo.ExecuteInstalledMethod (irc);

			/* 0xc3 is `RET` in AMD64 assembly */
			Assert.AreEqual ((byte) 0xc3, *nativeCode.Blob);
		}

		[Test]
		public unsafe void TestConstInt () {
			// Goal: Ldc and ret of int32
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);

			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "ConstIntMethod");
			Assert.IsNotNull (mi);

			NativeCodeHandle nativeCode;

			var result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			Assert.AreEqual (CompilationResult.Ok, result);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
			Assert.IsNotNull (irc);
			var o = runtimeInfo.ExecuteInstalledMethod (irc, 1);
			Assert.IsNotNull (o);
			Assert.AreEqual (typeof(int), o.GetType ());
			Assert.AreEqual (42, (int)o);
		}

		[Test]
		public unsafe void TestIdentity () {
			// goal: minimum test of fn arg and stack manipulation in BigStep
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);

			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "IdentityMethod");

			NativeCodeHandle nativeCode;

			var result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
			var o = runtimeInfo.ExecuteInstalledMethod (irc, 42);
			Assert.IsNotNull (o);
			Assert.AreEqual (typeof(int), o.GetType ());
			Assert.AreEqual (42, (int)o);
		}

		[Test]
		public unsafe void TestStaticFieldRead () {
			ClassInfo ci = runtimeInfo.GetClassInfoFor (typeof (ICompilerTests).AssemblyQualifiedName);
			MethodInfo mi = runtimeInfo.GetMethodInfoFor (ci, "StaticFieldReadMethod");

			NativeCodeHandle nativeCode;

			var result = compiler.CompileMethod (runtimeInfo, mi, CompilationFlags.None, out nativeCode);
			InstalledRuntimeCode irc = runtimeInfo.InstallCompilationResult (result, mi, nativeCode);
			var o = runtimeInfo.ExecuteInstalledMethod (irc);
			Assert.IsNotNull (o);
			Assert.AreEqual (typeof(int), o.GetType ());
			Assert.AreEqual (0x1337, (int)o);
		}
	}
}
