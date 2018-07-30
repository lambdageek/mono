// CILSymbolicExecutor.cs
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

namespace Mono.Compiler.BigStep {
	/// <summary>
	///   Emulate CIL execution only in the sense of stack change and delegate further handling for
	///   each operation to a processor.
	/// </summary>
	/// <remarks>
	///   This class partially implements stack-based virtual machine as codified by ECMA-335. It tracks
	///   stack depth change and types associated with each operand, which may come from stack, locals
	///   or arguments. Upon completion of each operation it invokes a processor to perform customized
	///   operation. LLVM bitcode emitter is implemented as a processor.
	/// </remarks>
	public class CILSymbolicExecutor : INameGenerator {
		private IOperationProcessor processor;
		private IRuntimeInformation runtime;
		private MethodInfo methodInfo;
		private MethodBody body;

		private Stack<TempOperand> stack;
		private int tempSeq = 0;

		private List<LocalOperand> locals;
		private List<ArgumentOperand> args;

		// Physical => Logical index mapping for instructions which are jump targets
		private Dictionary<int, int> targetIndices;

		// INameGenerator
		public string NextName ()
		{
			return (tempSeq++).ToString ();
		}

		public CILSymbolicExecutor (IOperationProcessor processor, IRuntimeInformation runtime, MethodInfo methodInfo)
		{ 
			this.processor = processor;
			this.runtime = runtime;
			this.methodInfo = methodInfo;
			this.body = methodInfo.Body;

			this.stack = new Stack<TempOperand>();
			this.targetIndices = new Dictionary<int, int> ();

			// The input is already ordered by index.
			this.locals = new List<LocalOperand> ();
			foreach (LocalVariableInfo lvi in body.LocalVariables) 
                        {
				this.locals.Add (new LocalOperand (lvi.LocalIndex, lvi.LocalType));
			}

			// The input is already ordered by index.
			this.args = new List<ArgumentOperand> ();
			foreach (ParameterInfo pi in methodInfo.Parameters) 
                        {
				this.args.Add (new ArgumentOperand (pi.Position, pi.ParameterType));
			}
		}

		public void Execute ()
		{
			Pass1 ();
			Pass2 ();
		}

		/// In pass 1, collect jump information
		private void Pass1 ()
		{
			// The jump info encoded in CIL is the byte offset within the method. But for the processor
			// we want to represent jump target with the logical index of the first instruction in BB.
			// To collect these info in one pass, use two data structures to cross-reference the logical-physical
			// mapping.

                        // CLR's brancing ops have only one target. The implicit branch is the next op, if the current
                        // evaluates true. However, not all downstream languages support this. For example, branching in
                        // LLVM must explicitly specify targets for both true and false results. To enable this scenario,
                        // we collect both explicit and implicit targets.

			// logical => physical. This contains all instructions.
			List<int> lpIndices = new List<int> ();
			// physical => logical. This contains all instructions.
			Dictionary<int, int> plIndices = new Dictionary<int, int> ();

			var iter = body.GetIterator ();
                        bool isImplicitTarget = false;
			while (iter.MoveNext ()) {
				Opcode opcode = iter.Opcode;
				ExtendedOpcode? extOpCode = null;
				if (opcode == Opcode.ExtendedPrefix) {
					extOpCode = iter.ExtOpcode;
				}
				OpcodeFlags opflags = iter.Flags;

				// Record this instruction in both mappings
				int pindex = iter.Index;
				lpIndices.Add (pindex);
				int lindex = lpIndices.Count - 1;
				plIndices[pindex] = lindex;
                                // Record a jump target
                                if (isImplicitTarget)
                                {
                                        // Case A: an implicit jump target right after a branch op
                                        targetIndices[pindex] = lindex;
                                        isImplicitTarget = false;
                                }
				else if (targetIndices.ContainsKey (pindex)) 
                                {
                                        // Case B: an explicit jump target as specified by a branch op
                                        // First check if there is an entry in targets for this instruction.
					// If so, populate the entry with P index we just learned.
					targetIndices[pindex] = lindex;
				}

				switch (opcode) 
                                {
                                        case Opcode.Br:
                                        case Opcode.BrS:
                                        case Opcode.Brfalse:
                                        case Opcode.BrfalseS:
                                        case Opcode.Brtrue:
                                        case Opcode.BrtrueS:
                                        case Opcode.Beq:
                                        case Opcode.BeqS:
                                        case Opcode.BgeUn:
                                        case Opcode.BgeUnS:
                                        case Opcode.Bgt:
                                        case Opcode.BgtS:
                                        case Opcode.BgtUn:
                                        case Opcode.BgtUnS:
                                        case Opcode.Ble:
                                        case Opcode.BleS:
                                        case Opcode.BleUn:
                                        case Opcode.BleUnS:
                                        case Opcode.Blt:
                                        case Opcode.BltS:
                                        case Opcode.BltUn:
                                        case Opcode.BltUnS:
                                        case Opcode.BneUn:
                                        case Opcode.BneUnS:
                                                int target = DecodeBranchTarget(iter);
                                                if (target <= pindex) 
                                                {
                                                        // CASE I: Jump backward
                                                        // If jumping backward, we already have everything.
                                                        targetIndices[target] = plIndices[target];
                                                } 
                                                else 
                                                {
                                                        // CASE II: Jump forward
                                                        // If jumping forward, we don't know the logic index yet. The value 
                                                        // will be filled later when we reach that instruction.
                                                        targetIndices[target] = -1;
                                                }

                                                // Since this OP is a brancing, the next OP is an implicit target.
                                                isImplicitTarget = true;
                                                break;
				}
			}
		}

		private int DecodeBranchTarget (IlIterator iter)
		{
			int opParam = iter.DecodeParamI ();
			// Use next index to skip the bytes for the current instruction.
			int target = iter.NextIndex + opParam;
			return target;
		}

		/// In pass 2, emulate execution
		private void Pass2 ()
		{
			int index = 0;
			var iter = body.GetIterator ();
			while (iter.MoveNext ()) 
                        {
				Opcode opcode = iter.Opcode;
				ExtendedOpcode? extOpCode = null;
				if (opcode == Opcode.ExtendedPrefix) 
                                {
					extOpCode = iter.ExtOpcode;
				}
				OpcodeFlags opflags = iter.Flags;
				int opParam = 0;
				IOperand output = null;

				// 1) Collect operands
				List<IOperand> operands = new List<IOperand> ();
			        // 1.1) operands not from stack
				switch (opcode) {
					// 1.1.1) operands from Arguments
					case Opcode.Ldarg0:
						operands.Add (output = args[0]);
						break;
					case Opcode.Ldarg1:
						operands.Add (output = args[1]);
						break;
					case Opcode.Ldarg2:
						operands.Add (output = args[2]);
						break;
					case Opcode.Ldarg3:
						operands.Add (output = args[3]);
						break;
					case Opcode.LdargS:
						opParam = iter.DecodeParamI ();
						operands.Add (output = args[opParam]);
						break;
					// 1.1.2) operands from Locals
					case Opcode.Ldloc0:
						operands.Add (output = locals[0]);
						break;
					case Opcode.Ldloc1:
						operands.Add (output = locals[1]);
						break;
					case Opcode.Ldloc2:
						operands.Add (output = locals[2]);
						break;
					case Opcode.Ldloc3:
						operands.Add (output = locals[3]);
						break;
					case Opcode.LdlocS:
						opParam = iter.DecodeParamI();
						operands.Add (output = locals[opParam]);
						break;
					// 1.1.3) operands from constants
					case Opcode.LdcI4_0:
						operands.Add (output = new Int32ConstOperand (0));
						break;
					case Opcode.LdcI4_1:
						operands.Add (output = new Int32ConstOperand (1));
						break;
					case Opcode.LdcI4_2:
						operands.Add (output = new Int32ConstOperand (2));
						break;
					case Opcode.LdcI4_3:
						operands.Add (output = new Int32ConstOperand (3));
						break;
					case Opcode.LdcI4_4:
						operands.Add (output = new Int32ConstOperand (4));
						break;
					case Opcode.LdcI4_5:
						operands.Add (output = new Int32ConstOperand (5));
						break;
					case Opcode.LdcI4_6:
						operands.Add (output = new Int32ConstOperand(6));
						break;
					case Opcode.LdcI4_7:
						operands.Add (output = new Int32ConstOperand(7));
						break;
					case Opcode.LdcI4M1:
						operands.Add (output = new Int32ConstOperand(-1));
						break;
					case Opcode.LdcI4:
					case Opcode.LdcI4S:
						opParam = iter.DecodeParamI();
						operands.Add (output = new Int32ConstOperand(opParam));
						break;
					case Opcode.LdcI8:
					case Opcode.LdcR4:
					case Opcode.LdcR8:
						throw new NotImplementedException($"TODO: Cannot handle {opcode.ToString()} yet.");
                                                // TODO:  ExtendedOpcode.Ldloc
				}

				// 1.2) operands to be popped from stack
				PopBehavior popbhv = iter.PopBehavior;
				int popCount = 0;
			        switch (popbhv) {
					case PopBehavior.Pop0:
						popCount = 0;
						break;
					case PopBehavior.Pop1:
					case PopBehavior.Popi:
					case PopBehavior.Popref:
						popCount = 1;
						break;
					case PopBehavior.Pop1_pop1:
					case PopBehavior.Popi_popi:
					case PopBehavior.Popi_popi8:
					case PopBehavior.Popi_popr4:
					case PopBehavior.Popi_popr8:
					case PopBehavior.Popref_pop1:
					case PopBehavior.Popi_pop1:
					case PopBehavior.Popref_popi:
						popCount = 2;
						break;
					case PopBehavior.Popi_popi_popi:
					case PopBehavior.Popref_popi_popi:
					case PopBehavior.Popref_popi_popi8:
					case PopBehavior.Popref_popi_popr4:
					case PopBehavior.Popref_popi_popr8:
					case PopBehavior.Popref_popi_popref:
						popCount = 3;
						break;
					case PopBehavior.PopAll:
						popCount = stack.Count;
						break;
					case PopBehavior.Varpop:
						if (opcode == Opcode.Ret) {
							if (stack.Count == 0) {
								break;
							} else if (stack.Count == 1) {
								popCount = 1;
								break;
							} else if (stack.Count > 1) {
								// Likely a bug somewhere else in the symbolic engine.
								throw new InvalidOperationException ($"Unexpected. Leaves function with non-empty stack.");
							}
						}

                                                throw new NotImplementedException ($"TODO: Cannot handle PopBehavior.Varpop against { opcode } yet.");
				}

				int count = popCount;
				ClrType[] tempOdTypes = new ClrType[count];
				TempOperand[] tempOds = new TempOperand[count];
				while (count > 0) 
                                {
					TempOperand tmp = stack.Pop ();
					tempOdTypes[count - 1] = tmp.Type;
					tempOds[count - 1] = tmp;
					count--;
				}
                                // Store operands in FIFO order, which is also assumed by CLR operations
                                for (int i = 0; i < popCount; i++) 
                                {
					operands.Add (tempOds[i]);
				}

				// 1.3) results not pushed back to stack, but stored somewhere else (args, locals, etc.)
				switch (opcode) 
                                {
				        case Opcode.Br:
					case Opcode.BrS:
					case Opcode.Brfalse:
					case Opcode.BrfalseS:
					case Opcode.Brtrue:
					case Opcode.BrtrueS:
				        case Opcode.Beq:
				        case Opcode.BeqS:
				        case Opcode.BgeUn:
				        case Opcode.BgeUnS:
				        case Opcode.Bgt:
				        case Opcode.BgtS:
				        case Opcode.BgtUn:
				        case Opcode.BgtUnS:
				        case Opcode.Ble:
				        case Opcode.BleS:
				        case Opcode.BleUn:
				        case Opcode.BleUnS:
				        case Opcode.Blt:
				        case Opcode.BltS:
				        case Opcode.BltUn:
				        case Opcode.BltUnS:
				        case Opcode.BneUn:
				        case Opcode.BneUnS:
						int target = DecodeBranchTarget (iter);
						int logicIndex = targetIndices[target];
						BranchTargetOperand bto = new BranchTargetOperand (logicIndex);
						operands.Add (bto);
						break;
				        case Opcode.Stloc0:
						operands.Add (locals[0]);
						break;
					case Opcode.Stloc1:
						operands.Add (locals[1]);
						break;
					case Opcode.Stloc2:
						operands.Add (locals[2]);
						break;
					case Opcode.Stloc3:
						operands.Add (locals[3]);
						break;
					case Opcode.StlocS:
						opParam = iter.DecodeParamI ();
						operands.Add (locals[opParam]);
						break;
					case Opcode.StargS:
						opParam = iter.DecodeParamI ();
						operands.Add (args[opParam]);
						break; 
				        case Opcode.Ldsfld:
				                int token = iter.DecodeParamI ();
				                FieldInfo fieldInfo = runtime.GetFieldInfoForToken (methodInfo, token);
				                operands.Add (new Int32ConstOperand (token));
				                output = new TempOperand (this, RuntimeInformation.Int32Type); /* FIXME: look up the field info! */
				                break;
				}

                                if (extOpCode.HasValue)
                                {
                                        switch (extOpCode.Value)
                                        {
                                                case ExtendedOpcode.Stloc:
                                                        opParam = iter.DecodeParamI ();
                                                        operands.Add (locals[opParam]);
                                                        break;
                                                case ExtendedOpcode.Starg:
                                                        opParam = iter.DecodeParamI ();
                                                        operands.Add (args[opParam]);
                                                        break;
                                        }
                                }

				// 2) Determine the result type for values to push into stack
				TempOperand tod = null;
				if (output != null) 
                                {
					tod = new TempOperand (this, output.Type);
				} 
                                else 
                                {
					ClrType? ctyp = OpResultTypeLookup.Query (opcode, extOpCode, tempOdTypes);
					if (ctyp.HasValue) 
                                        {
						tod = new TempOperand(this, (ClrType)ctyp);
					}
				}

				// 3) Push result
				PushBehavior pushbhv = iter.PushBehavior;
                                switch (pushbhv) {
					case PushBehavior.Push0:
                                                if (tod != null) 
                                                {
                                                        throw new InvalidOperationException (
                                                                $"Unexpected: a value is generated but should not be pushed to stack. OP: { opcode }");
                                                }
                                                break;
					case PushBehavior.Push1:
					case PushBehavior.Pushi:
					case PushBehavior.Pushi8:
					case PushBehavior.Pushr4:
					case PushBehavior.Pushr8:
						if (tod == null) {
                                                        throw new InvalidOperationException ($"Unexpected: no value to push to stack. OP: { opcode }");
						}
						stack.Push (tod);
						break;
					case PushBehavior.Push1_push1:
						// This only applies to Opcode.Dup
						if (tod == null) {
                                                        throw new InvalidOperationException ($"Unexpected: no value to push to stack. OP: { opcode }");
						}
						stack.Push(tod);
						stack.Push(tod);
						break;
					case PushBehavior.Varpush:
						// This is a huge TODO. Function call (Opcode.Call/Calli/Callvirt) relies on this behavior.
						throw new NotImplementedException ("TODO: Cannot handle PushBehavior.Varpush yet.");
                                }

				// 4) Send the info to operation processor
				bool isJumpTarget = targetIndices.ContainsKey(iter.Index);
				OperationInfo opInfo = new OperationInfo {
					Index = index,
					Operation = opcode,
					ExtOperation = extOpCode,
					Operands = operands.ToArray (),
					Result = tod,
					JumpTarget = isJumpTarget
				};
				processor.Process (opInfo);
				index++;
			}
		}
	}
}
