// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.InteropServices;

namespace System.Runtime.CompilerServices
{
	partial class RuntimeFeature
	{
		public static bool IsDynamicCodeSupported {
			[Intrinsic]  // the JIT/AOT compiler will change this flag to false for FullAOT scenarios, otherwise true
			get => IsDynamicCodeSupported;
		}

		public static bool IsDynamicCodeCompiled {
			[Intrinsic]  // the JIT/AOT compiler will change this flag to false for FullAOT scenarios, otherwise true
			get => IsDynamicCodeCompiled;
		}

		[MethodImplAttribute (MethodImplOptions.InternalCall)]
		private static unsafe extern void LoadMetadataUpdate_internal (Assembly base_assm, byte* dmeta_bytes, int dmeta_length, byte *dil_bytes, int dil_length);

		public static void LoadMetadataUpdate (Assembly assm, byte[] dmeta_data, byte[] dil_data) {
			unsafe {
				fixed (byte* dmeta_bytes = dmeta_data)
				fixed (byte* dil_bytes = dil_data) {
				       LoadMetadataUpdate_internal (assm, dmeta_bytes, dmeta_data.Length, dil_bytes, dil_data.Length);
				}
			}
		}
	}
}
