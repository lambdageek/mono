using System;
using System.Collections.Generic;

using Mono.Compiler;
using SimpleJit.Metadata;
using SimpleJit.CIL;

/// <summary>
///   Operands used by CIL execution emulator.
/// </summary>
/// <remarks>
///   Operand is a data wrapper used to convey information between CLR and processor.
///   In the case of LLVM code emitter, the name of these operands play a crucial role
///   in linking between a CLR value source and a LLVM value.
///   
///   The name convention used by these operands:
///     Argument: A + [parameter index starting from 0]
///     Local:    L + [variable index starting from 0]
///     Temp:     T + [a contiguous incrementing value starting from 0]
///     Constant: C + [The value itself in form of string]
///     PC:       PC (name for this operand type is not important)
/// </remarks>
namespace Mono.Compiler.BigStep {
	internal enum OperandType {
		/// <summary> The operand is a method argument. </summary>
		Argument,
		/// <summary> The operand is a user-defined local variable. </summary>
		Local,
		/// <summary> The operand is a machine-defined local variable for temporary use. </summary>
		Temp,
		/// <summary> The operand is a constant value stored as part of the instruction. </summary>
		Const,
		/// <summary> The operand is a value for the logical program counter. </summary>
		PC
	}

	internal interface IOperand {
		string Name { get; }
		ClrType Type { get; }
		OperandType OperandType { get; }
	}

	abstract internal class Operand : IOperand {
		public virtual string Name { get; private set; }
		public ClrType Type { get; private set; }

		internal Operand (string name, ClrType type)
		{
			Name = name;
			Type = type;
		}

		public virtual OperandType OperandType { get; }
	}

	internal class BranchTargetOperand : Operand {
		public int PC { get; private set; }

		internal BranchTargetOperand (int pcvalue)
			: base ("PC", RuntimeInformation.VoidTypeInstance)
		{
			PC = pcvalue;
		}

		public override OperandType OperandType => OperandType.PC;
	}

	internal class ArgumentOperand : Operand {
		public int Index { get; private set; }

		internal ArgumentOperand (int index, ClrType type)
			: base ("A" + index, type)
		{
			Index = index;
		}

		public override OperandType OperandType => OperandType.Argument;
	}

	internal class LocalOperand : Operand {
		public int Index { get; private set; }

		internal LocalOperand (int index, ClrType type)
			: base ("L" + index, type)
		{
			Index = index;
		}

		public override OperandType OperandType => OperandType.Local;
	}

	internal abstract class ConstOperand : Operand {
		protected ConstOperand (ClrType type)
			: base ("C", type) // "C" is just a prefix. See overridden name getters in subclasses
		{
		}

		public override OperandType OperandType => OperandType.Const;
	}

	internal class Int32ConstOperand : ConstOperand {
		public int Value { get; private set; }

		internal Int32ConstOperand (int value)
			: base (RuntimeInformation.Int32TypeInstance)
		{
			Value = value;
		}

		public override string Name {
			get { return base.Name + Value.ToString (); }
		}
	}

	internal class Int64ConstOperand : ConstOperand {

		public long Value { get; private set; }

		internal Int64ConstOperand (long value)
			: base (RuntimeInformation.Int64Type)
		{
			Value = value;
		}

		public override string Name {
			get { return base.Name + Value.ToString (); }
		}
	}

	internal class Float32ConstOperand : ConstOperand {
		public float Value { get; private set; }

		internal Float32ConstOperand (float value)
			: base (RuntimeInformation.Float32Type)
		{
			Value = value;
		}

		public override string Name {
			get { return base.Name + Value.ToString (); }
		}
	}

	internal class Float64ConstOperand : ConstOperand {

		public double Value { get; private set; }

		internal Float64ConstOperand (double value)
			: base (RuntimeInformation.Float64Type)
		{
			Value = value;
		}

		public override string Name {
			get { return base.Name + Value.ToString (); }
		}
	}

	internal class TempOperand : Operand {
		internal TempOperand (INameGenerator nameGen, ClrType type)
			: base ("T" + nameGen.NextName(), type)
		{
		}

		public override OperandType OperandType => OperandType.Temp;
	}

	public interface INameGenerator {
		string NextName ();
	}
}

