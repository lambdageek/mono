using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using SimpleJit.Metadata;

namespace Mono.Compiler {
	public class RuntimeInformation : IRuntimeInformation {
		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		extern static InstalledRuntimeCode mono_install_compilation_result (int compilationResult, RuntimeMethodHandle handle, NativeCodeHandle codeHandle);

		public InstalledRuntimeCode InstallCompilationResult (CompilationResult compilationResult, MethodInfo methodInfo, NativeCodeHandle codeHandle) {
			return mono_install_compilation_result ((int) compilationResult, methodInfo.RuntimeMethodHandle, codeHandle);
		}

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		extern static object mono_execute_installed_method (InstalledRuntimeCode irc, params object[] args);

		public object ExecuteInstalledMethod (InstalledRuntimeCode irc, params object[] args) {
			return mono_execute_installed_method (irc, args);
		}

		public ClassInfo GetClassInfoFor (string className)
		{
			var t = Type.GetType (className, true); /* FIXME: get assembly first, then type */
			return ClassInfo.FromType (t);
		}

		public MethodInfo GetMethodInfoFor (ClassInfo classInfo, string methodName) {
			/* FIXME: methodName doesn't uniquely determine a method */
			return classInfo.GetMethodInfoFor (methodName);
		}

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		static extern System.Reflection.FieldInfo GetSRFieldInfoForToken (RuntimeMethodHandle handle, int token);

		public FieldInfo GetFieldInfoForToken (MethodInfo mi, int token) {
			System.Reflection.FieldInfo srfi = GetSRFieldInfoForToken (mi.RuntimeMethodHandle, token);
			return new FieldInfo (srfi);
		}

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		static extern IntPtr ComputeStaticFieldAddress (RuntimeFieldHandle handle);

		public IntPtr ComputeFieldAddress (FieldInfo fi) {
			if (!fi.IsStatic)
				throw new InvalidOperationException ("field isn't static");
			return ComputeStaticFieldAddress (fi.srFieldInfo.FieldHandle);
		}

		public static ClrType VoidType => commonTypes[(typeof (void)).TypeHandle];

		public static ClrType ObjectType => commonTypes[(typeof (object)).TypeHandle];

		public static ClrType StringType => commonTypes[(typeof (string)).TypeHandle];

		public static ClrType TypedRefType => commonTypes[(typeof (System.TypedReference)).TypeHandle];

		public static ClrType BoolType => commonTypes[ (typeof (bool)).TypeHandle];

		public static ClrType CharType => commonTypes[(typeof (char)).TypeHandle];

		public static ClrType Int8Type => commonTypes[(typeof (System.SByte)).TypeHandle];
		public static ClrType UInt8Type => commonTypes[ (typeof (System.Byte)).TypeHandle];

		public static ClrType Int16Type => commonTypes[(typeof (System.Int16)).TypeHandle];
		public static ClrType UInt16Type => commonTypes[(typeof (System.UInt16)).TypeHandle];

		public static ClrType Int32Type => commonTypes[(typeof (System.Int32)).TypeHandle];
		public static ClrType UInt32Type => commonTypes[(typeof (System.UInt32)).TypeHandle];

		public static ClrType Int64Type => commonTypes[(typeof (System.Int64)).TypeHandle];
		public static ClrType UInt64Type => commonTypes[(typeof (System.UInt64)).TypeHandle];

		public static ClrType NativeIntType => commonTypes[(typeof (System.IntPtr)).TypeHandle];
		public static ClrType NativeUnsignedIntType => commonTypes[(typeof (System.UIntPtr)).TypeHandle];

		public static ClrType Float32Type => commonTypes[(typeof (System.Single)).TypeHandle];
		public static ClrType Float64Type => commonTypes[(typeof (System.Double)).TypeHandle];

                private static IDictionary<RuntimeTypeHandle, ClrType> commonTypes;

                static RuntimeInformation ()
                {
                        commonTypes = new Dictionary<RuntimeTypeHandle, ClrType>();
                        commonTypes[(typeof (void)).TypeHandle] = new ClrType ((typeof (void)).TypeHandle);
                        commonTypes[(typeof (object)).TypeHandle] = new ClrType ((typeof (object)).TypeHandle);
                        commonTypes[(typeof (string)).TypeHandle] = new ClrType ((typeof (string)).TypeHandle);
                        commonTypes[(typeof (System.TypedReference)).TypeHandle] = new ClrType ((typeof (System.TypedReference)).TypeHandle);
                        commonTypes[(typeof (bool)).TypeHandle] = new ClrType ((typeof (bool)).TypeHandle, NumericCatgoery.Int, 1, false);
                        commonTypes[(typeof (char)).TypeHandle] = new ClrType ((typeof (char)).TypeHandle, NumericCatgoery.Int, 2, false);
                        commonTypes[(typeof (System.SByte)).TypeHandle] = new ClrType ((typeof (System.SByte)).TypeHandle, NumericCatgoery.Int, 1, true);
                        commonTypes[(typeof (System.Byte)).TypeHandle] = new ClrType ((typeof (System.Byte)).TypeHandle, NumericCatgoery.Int, 1, false);
                        commonTypes[(typeof (System.Int16)).TypeHandle] = new ClrType ((typeof (System.Int16)).TypeHandle, NumericCatgoery.Int, 2, true);
                        commonTypes[(typeof (System.UInt16)).TypeHandle] = new ClrType ((typeof (System.UInt16)).TypeHandle, NumericCatgoery.Int, 2, false);
                        commonTypes[(typeof (System.Int32)).TypeHandle] = new ClrType ((typeof (System.Int32)).TypeHandle, NumericCatgoery.Int, 4, true);
                        commonTypes[(typeof (System.UInt32)).TypeHandle] = new ClrType ((typeof (System.UInt32)).TypeHandle, NumericCatgoery.Int, 4, false);
                        commonTypes[(typeof (System.Int64)).TypeHandle] = new ClrType ((typeof (System.Int64)).TypeHandle, NumericCatgoery.Int, 8, true);
                        commonTypes[(typeof (System.UInt64)).TypeHandle] = new ClrType ((typeof (System.UInt64)).TypeHandle, NumericCatgoery.Int, 8, false);
                        commonTypes[(typeof (System.IntPtr)).TypeHandle] = new ClrType ((typeof (System.IntPtr)).TypeHandle, NumericCatgoery.NativeInt, 8, true); // The precesion shouldn't be used.
                        commonTypes[(typeof (System.UIntPtr)).TypeHandle] = new ClrType ((typeof (System.UIntPtr)).TypeHandle, NumericCatgoery.NativeInt, 8, false); // The precesion shouldn't be used.
                        commonTypes[(typeof (System.Single)).TypeHandle] = new ClrType ((typeof (System.Single)).TypeHandle, NumericCatgoery.Float, 4, true);
                        commonTypes[(typeof (System.Double)).TypeHandle] = new ClrType ((typeof (System.Double)).TypeHandle, NumericCatgoery.Float, 8, true);
                }

                internal static ClrType ClrTypeFromType(Type type)
                {
                        RuntimeTypeHandle handle = type.TypeHandle;
                        if (!commonTypes.TryGetValue(handle, out ClrType typ)) 
                        {
                                typ = new ClrType(handle);
                        }

                        return typ;
                }

	}
}
