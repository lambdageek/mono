using System;

namespace Mono.Compiler
{
	public class FieldInfo
	{
		internal System.Reflection.FieldInfo srFieldInfo;
		public ClassInfo Parent { get; }

		internal FieldInfo (System.Reflection.FieldInfo srfi) {
			this.srFieldInfo = srfi;
			this.Parent = ClassInfo.FromType (srfi.DeclaringType);
		}

		public bool IsStatic {
			get {
				return srFieldInfo.IsStatic;
			}
		}

	}
}
