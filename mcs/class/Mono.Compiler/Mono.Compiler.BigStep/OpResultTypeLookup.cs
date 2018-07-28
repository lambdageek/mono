// OpResultTypeLookup.cs
//
// Author:
//   Ming Zhou  <zhoux738@umn.edu>
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;

using Mono.Compiler;
using SimpleJit.Metadata;
using SimpleJit.CIL;

/// <summary>
///   Implemented "III.1.5 Operand type table"
/// </summary>
namespace Mono.Compiler.BigStep {
	// III.1.5 Operand type table
	internal class OpResultTypeLookup {
		internal static ClrType? Query (Opcode op, ExtendedOpcode? exop, params ClrType[] types)
		{
			if (exop.HasValue) {
				ExtendedOpcode exopcode = (ExtendedOpcode)exop;
				switch (exop) {
					// Table III.4: Binary Comparison or Branch Operations
					case ExtendedOpcode.Ceq:
					case ExtendedOpcode.Cgt:
					case ExtendedOpcode.CgtUn:
					case ExtendedOpcode.Clt:
					case ExtendedOpcode.CltUn:
						return RuntimeInformation.BoolType;
				}
			}

                        CilStackType st0, st1;
			switch (op) {
				// Table III.2: Binary Numeric Operations
				// Table III.7: Overflow Arithmetic Operations
				case Opcode.Add:
				case Opcode.AddOvf:
				case Opcode.AddOvfUn:
				case Opcode.Sub:
				case Opcode.SubOvf:
				case Opcode.SubOvfUn:
				case Opcode.Mul:
				case Opcode.MulOvf:
				case Opcode.MulOvfUn:
				case Opcode.Div:
				case Opcode.Rem:
					return QueryBinaryOp (types[0], types[1]);
				// Table III.3: Unary Numeric Operations
				case Opcode.Neg:
                                        st0 = ToStackType (types[0]);
                                        st1 = ToStackType (types[1]);
					if (st0 == CilStackType.Int32 && st1 == CilStackType.Int32 ||
                                                st0 == CilStackType.Int64 && st1 == CilStackType.Int64 || 
                                                st0 == CilStackType.TypePointer && st1 == CilStackType.TypePointer ||
                                                st0 == CilStackType.Float && st1 == CilStackType.Float) 
                                        {
						return RuntimeInformation.BoolType;
					}
					// This should never happen unless there are bugs in CSC.
					throw new Exception ($"Unexpected. Operation { op.ToString () } cannot perform on operands of type { types[0].AsSystemType.Name } and { types[1].AsSystemType.Name }");
				// Table III.5: Table III.5: Integer Operations
				case Opcode.And:
				case Opcode.Not:
				case Opcode.Or:
				case Opcode.Xor:
				case Opcode.RemUn:
				case Opcode.DivUn:
					// Reuse matrix defined for bianry ops since the valid set is a subset of the latter 
					// and we always assume the validaity of input operands.
					return QueryBinaryOp (types[0], types[1]);
				// Table III.6: Shift Operations
				case Opcode.Shl:
				case Opcode.Shr:
				case Opcode.ShrUn:
					// operand 0: To Be Shifted
					// operand 1: Shift-By
                                        st0 = ToStackType (types[0]);
                                        st1 = ToStackType (types[1]);
					if ((st0 == CilStackType.Int32 || st0 == CilStackType.Int64 || st0 == CilStackType.NativeInt) && 
                                                (st1 == CilStackType.Int32 || st1 == CilStackType.NativeInt)) 
                                        {
						return RuntimeInformation.Int32Type;
					}
					// This should never happen unless there are bugs in CSC.
					throw new Exception ($"Unexpected. Operation { op.ToString () } cannot perform on operands of type { types[0].AsSystemType.Name } and { types[1].AsSystemType.Name }");
					// Table III.8: Conversion Operations
				case Opcode.ConvI1:
				case Opcode.ConvU1:
				case Opcode.ConvI2:
				case Opcode.ConvU2:
				case Opcode.ConvI4:
				case Opcode.ConvU4:
				case Opcode.ConvOvfI1:
				case Opcode.ConvOvfI1Un:
				case Opcode.ConvOvfI2:
				case Opcode.ConvOvfI2Un:
				case Opcode.ConvOvfI4:
				case Opcode.ConvOvfI4Un:
				case Opcode.ConvI:
				case Opcode.ConvOvfI:
				case Opcode.ConvU:
				case Opcode.ConvOvfU:
					// For short integers, the stack value is truncated but remains the same type.
					return types[0];
				case Opcode.ConvI8:
				case Opcode.ConvU8:
					if (types[0] == RuntimeInformation.Float32Type ||
					    (types[0] == RuntimeInformation.Int32Type)) {
						return RuntimeInformation.Int64Type;
					} else {
						return types[0];
					}
				case Opcode.ConvR4:
				case Opcode.ConvR8:
				case Opcode.ConvRUn:
					return RuntimeInformation.Float64Type;
				default:
					return null;
			}
		}

                // Types categorized on stack. The stack allows operends of types as specified
                // by Table I.6: Data Types Directly Supported by the CLI, but they will be categorized
                // into 6 groups referred to as Stack Types, which are used to determine the result
                // type of operation.
                internal enum CilStackType 
                {
                        Int32,
                        Int64,
                        NativeInt,
                        Float,
                        TypePointer,
                        Object
                }

		internal class CilTypePair 
                {
			private string notation;

			private CilTypePair (ClrType ta, ClrType tb)
			{
				CilStackType sta = ToStackType (ta);
				CilStackType stb = ToStackType (tb);
				notation = sta.ToString() + "-" + stb.ToString();
			}

			public override int GetHashCode ()
			{
				return notation.GetHashCode ();
			}

			public override bool Equals (object obj)
			{
				if (obj != null && obj is CilTypePair) {
					return notation == ((CilTypePair)obj).notation;
				}

				return false;
			}

			internal static CilTypePair Create (ClrType ta, ClrType tb)
			{
				return new CilTypePair (ta, tb);
			}
		}

		// Convert the type to one of five types tracked by CLI for operator result verification
		private static CilStackType ToStackType (ClrType type)
		{
                        switch(type.NumCat)
                        {
                                case NumericCatgoery.Int:
                                        return type.Precision <= 4 ? CilStackType.Int32 : CilStackType.Int64;
                                case NumericCatgoery.NativeInt:
                                        return CilStackType.NativeInt;
                                case NumericCatgoery.Float:
                                        return CilStackType.Float;
                        }
			
                        if (type == RuntimeInformation.TypedRefType) {
				return CilStackType.TypePointer;
			} else {
				return CilStackType.Object;
			}
		}

		internal static ClrType QueryBinaryOp (ClrType ta, ClrType tb)
		{
			return binaryOpResults[CilTypePair.Create (ta, tb)];
		}

		private static Dictionary<CilTypePair, ClrType> binaryOpResults;

		static OpResultTypeLookup ()
		{
			binaryOpResults = new Dictionary<CilTypePair, ClrType> ();
			binaryOpResults[CilTypePair.Create(RuntimeInformation.Int32Type, RuntimeInformation.Int32Type)] = RuntimeInformation.Int32Type;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.Int64Type, RuntimeInformation.Int64Type)] = RuntimeInformation.Int64Type;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.Int32Type, RuntimeInformation.NativeIntType)] = RuntimeInformation.NativeIntType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.NativeIntType, RuntimeInformation.Int32Type)] = RuntimeInformation.NativeIntType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.NativeIntType, RuntimeInformation.NativeIntType)] = RuntimeInformation.NativeIntType;

			// For F types, always result in long type for now. This needs re-visiting
			binaryOpResults[CilTypePair.Create(RuntimeInformation.Float32Type, RuntimeInformation.Float32Type)] = RuntimeInformation.Float64Type;
			// The following are not needed since they are converted to 'F' type on stack
                        //binaryOpResults[CilTypePair.Create(RuntimeInformation.Float32Type, RuntimeInformation.Float64Type)] = RuntimeInformation.Float64Type;
			//binaryOpResults[CilTypePair.Create(RuntimeInformation.Float64Type, RuntimeInformation.Float32Type)] = RuntimeInformation.Float64Type;
			//binaryOpResults[CilTypePair.Create(RuntimeInformation.Float64Type, RuntimeInformation.Float64Type)] = RuntimeInformation.Float64Type;

			// For & types, they are only applicable to certain OPs. 
			// But since we assume the validity of the input, do not perform such checks.
			binaryOpResults[CilTypePair.Create(RuntimeInformation.Int32Type, RuntimeInformation.TypedRefType)] = RuntimeInformation.TypedRefType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.NativeIntType, RuntimeInformation.TypedRefType)] = RuntimeInformation.TypedRefType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.TypedRefType, RuntimeInformation.Int32Type)] = RuntimeInformation.TypedRefType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.TypedRefType, RuntimeInformation.NativeIntType)] = RuntimeInformation.TypedRefType;
			binaryOpResults[CilTypePair.Create(RuntimeInformation.TypedRefType, RuntimeInformation.TypedRefType)] = RuntimeInformation.TypedRefType;
		}
	}
}
