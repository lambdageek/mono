using System;

using SimpleJit.Metadata;

namespace Mono.Compiler
{
	public interface IRuntimeInformation
	{
		InstalledRuntimeCode InstallCompilationResult (CompilationResult compilationResult, MethodInfo methodInfo, NativeCodeHandle codeHandle);

		object ExecuteInstalledMethod (InstalledRuntimeCode irc, params object[] args);

		ClassInfo GetClassInfoFor (string className);

		MethodInfo GetMethodInfoFor (ClassInfo classInfo, string methodName);

		FieldInfo GetFieldInfoForToken (MethodInfo mi, int token);

		IntPtr ComputeFieldAddress (FieldInfo fi);

                /// For a given array type, get the offset of the vector relative to the base address.
                uint GetArrayBaseOffset(ClrType type);
	}
}
