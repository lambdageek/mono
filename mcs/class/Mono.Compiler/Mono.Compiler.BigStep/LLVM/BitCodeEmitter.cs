using System;
using System.Collections.Generic;
using System.Threading;

using Mono.Compiler;
using SimpleJit.Metadata;
using SimpleJit.CIL;

using LLVMSharp;

/// <summary>
///   Emit LLVM bitcode via LLVMSharp.
/// </summary>
namespace Mono.Compiler.BigStep.LLVMBackend
{
    public class BitCodeEmitter : IOperationProcessor
    {
        private static readonly LLVMBool Success = new LLVMBool(0);
        private static readonly LLVMBool True = new LLVMBool(1);
        private static readonly LLVMMCJITCompilerOptions s_options = new LLVMMCJITCompilerOptions { NoFramePointerElim = 0 };

        private static int s_moduleSeq;
        private static bool s_uninitialized;

        private LLVMModuleRef module;
        private LLVMBuilderRef builder;
        private LLVMValueRef function;

        private LLVMValueRef[] argAddrs;
        private LLVMValueRef[] localAddrs;
        private Dictionary<string, LLVMValueRef> temps;
        private Dictionary<string, LLVMBasicBlockRef> bbs;

        public bool PrintDebugInfo { get; set; }

        public BitCodeEmitter(IRuntimeInformation runtimeInfo, MethodInfo method)
        {
            int seq = Interlocked.Increment(ref s_moduleSeq);
            string modName = "llvmmodule_" + seq;
            module = LLVM.ModuleCreateWithName(modName);
            builder = LLVM.CreateBuilder();
            temps = new Dictionary<string, LLVMValueRef>();
            bbs = new Dictionary<string, LLVMBasicBlockRef>();

            IReadOnlyCollection<ParameterInfo> prms = method.Parameters;
            LLVMTypeRef[] largs = new LLVMTypeRef[prms.Count];
            int i = 0;
            foreach (ParameterInfo pinfo in prms)
            {
                largs[i] = TranslateType(pinfo.ParameterType);
            }
            LLVMTypeRef rtyp = TranslateType(method.ReturnType);

            var funTy = LLVM.FunctionType(rtyp, largs, false);
            string funcName = modName + "_" + method.Name;
            function = LLVM.AddFunction(module, funcName, funTy);
            CreateFirstBasicBlock();

            IList<LocalVariableInfo> locals = method.Body.LocalVariables;
            AllocateArgsAndLocals(largs, locals);
        }

        #region Basic Block operations
        private void CreateFirstBasicBlock()
        {
            LLVMBasicBlockRef bb = LLVM.AppendBasicBlock(function, "entry");
            LLVM.PositionBuilderAtEnd(builder, bb);
        }

        private LLVMBasicBlockRef GetOrAddBasicBlock(int opIndex, bool moveToEnd)
        {
            string name = "BB_" + opIndex;
            LLVMBasicBlockRef bbr;
            if (!bbs.TryGetValue(name, out bbr))
            {
                bbs[name] = bbr = LLVM.AppendBasicBlock(function, name);
            }

            if (moveToEnd)
            {
                LLVM.PositionBuilderAtEnd(builder, bbr);
            }

            return bbr;
        }
        #endregion

        private void AllocateArgsAndLocals(LLVMTypeRef[] args, IList<LocalVariableInfo> locals)
        {
            this.argAddrs = new LLVMValueRef[args.Length];
            uint i = 0;
            for (; i < args.Length; i++)
            {
                LLVMValueRef vref = LLVM.GetParam(function, i);
                LLVMValueRef vaddr = LLVM.BuildAlloca(builder, args[i], "A" + i);
                LLVM.BuildStore(builder, vref, vaddr);
                this.argAddrs[i] = vaddr;
            }

            i = 0;
            localAddrs = new LLVMValueRef[locals.Count];
            foreach (LocalVariableInfo lvi in locals)
            {
                LLVMTypeRef ltyp = TranslateType(lvi.LocalType);
                LLVMValueRef lref = LLVM.BuildAlloca(builder, ltyp, "L" + i);
                this.localAddrs[i] = lref;
            }
        }

        // Emit LLVM instruction per CIL operation
        public void Process(OperationInfo opInfo)
        {
            if (opInfo.JumpTarget)
            {
                // If this op is a jump target, replace BB now
                this.GetOrAddBasicBlock(opInfo.Index, true);
            }

            Opcode op = opInfo.Operation;
            ExtendedOpcode? exop = opInfo.ExtOperation;
            IOperand[] operands = opInfo.Operands;
            TempOperand result = opInfo.Result;

            switch (op)
            {
                // Notation for comments:
                // op1, op2, ... => result pushed into expr-stack
                case Opcode.Nop:
                    break;
                case Opcode.Ret:
                    // tmp
                    InvokeOperation(
                        op, exop, operands,
                        vm =>
                        {
                            LLVM.BuildRet(builder, vm.Temp0);
                        });
                    break;
                case Opcode.Ldarg0:
                case Opcode.Ldarg1:
                case Opcode.Ldarg2:
                case Opcode.Ldarg3:
                case Opcode.LdargS:
                    // arg => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildLoad(builder, vm.Address0, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Stloc0:
                case Opcode.Stloc1:
                case Opcode.Stloc2:
                case Opcode.Stloc3:
                    // tmp, local
                    InvokeOperation(
                        op, exop, operands,
                        vm =>
                        {
                            LLVM.BuildStore(builder, vm.Temp0, vm.Address1);
                        });
                    break;
                case Opcode.LdcI4:
                case Opcode.LdcI4_0:
                case Opcode.LdcI4_1:
                case Opcode.LdcI4_2:
                case Opcode.LdcI4_3:
                case Opcode.LdcI4_4:
                case Opcode.LdcI4_5:
                case Opcode.LdcI4_6:
                case Opcode.LdcI4_7:
                case Opcode.LdcI4_8:
                case Opcode.LdcI4M1:
                case Opcode.LdcI4S:
                    // const => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildLoad(builder, vm.Const0, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Ldloc0:
                case Opcode.Ldloc1:
                case Opcode.Ldloc2:
                case Opcode.Ldloc3:
                case Opcode.LdlocS:
                    // local => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildLoad(builder, vm.Address0, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Add:
                case Opcode.AddOvf: // TODO - Handle overflow
                case Opcode.AddOvfUn: // TODO - Handle overflow, unsigned
                                      // tmp, tmp => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildAdd(builder, vm.Temp0, vm.Temp1, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Sub:
                case Opcode.SubOvf: // TODO - Handle overflow
                case Opcode.SubOvfUn: // TODO - Handle overflow, unsigned
                                      // tmp, tmp => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildSub(builder, vm.Temp0, vm.Temp1, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Mul:
                case Opcode.MulOvf: // TODO - Handle overflow
                case Opcode.MulOvfUn: // TODO - Handle overflow, unsigned
                                      // tmp, tmp => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildMul(builder, vm.Temp0, vm.Temp1, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Div:
                    // tmp, tmp => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildFDiv(builder, vm.Temp0, vm.Temp1, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
                case Opcode.Br:
                case Opcode.BrS:
                    // Special - jump to another BB
                    LLVMBasicBlockRef bb = this.GetBranchTarget(operands[0]);
                    LLVM.BuildBr(builder, bb);
                    break;
                case Opcode.DivUn:
                    // tmp, tmp => tmp
                    InvokeOperation(
                        op, exop, operands, result,
                        vm =>
                        {
                            LLVMValueRef tmp = LLVM.BuildUDiv(builder, vm.Temp0, vm.Temp1, result.Name);
                            return new NamedTempValue(tmp, result.Name);
                        });
                    break;
            }
        }

        /// Produce a native handle for the generated native code. Call this after Process()
        public NativeCodeHandle Yield()
        {
            if (PrintDebugInfo)
            {
                LLVM.DumpModule(module);
            }

            if (!BitCodeEmitter.s_uninitialized)
            {
                lock (typeof(BitCodeEmitter))
                {
                    if (!BitCodeEmitter.s_uninitialized)
                    {
                        LLVM.LinkInMCJIT();
                        LLVM.InitializeX86TargetMC();
                        LLVM.InitializeX86Target();
                        LLVM.InitializeX86TargetInfo();
                        LLVM.InitializeX86AsmParser();
                        LLVM.InitializeX86AsmPrinter();
                        LLVM.InitializeMCJITCompilerOptions(s_options);

                        BitCodeEmitter.s_uninitialized = true;
                    }
                }
            }

            try
            {
                if (LLVM.CreateMCJITCompilerForModule(out LLVMExecutionEngineRef engine, module, s_options, out var error) != Success)
                {
                    throw new Exception($"Compilation by LLVM failed: { error }");
                }
                IntPtr fnptr = LLVM.GetPointerToGlobal(engine, function);
                LLVM.DisposeExecutionEngine(engine); // Safe to dispose of this?
                unsafe
                {
                    return new NativeCodeHandle((byte*)fnptr, -1);
                }
            }
            finally
            {
                LLVM.DisposeBuilder(builder);
            }
        }

        internal class ValueMappings
        {
            internal LLVMValueRef Const0
            {
                get
                {
                    return Values[0].Value;
                }
            }

            internal LLVMValueRef Address0
            {
                get
                {
                    return Values[0].Value;
                }
            }

            internal LLVMValueRef Address1
            {
                get
                {
                    return Values[0].Value;
                }
            }

            internal LLVMValueRef Temp0
            {
                get
                {
                    return Values[0].Value;
                }
            }

            internal LLVMValueRef Temp1
            {
                get
                {
                    return Values[1].Value;
                }
            }

            internal StorageTypedValue[] Values { get; private set; }

            internal ValueMappings(int length)
            {
                Values = new StorageTypedValue[length];
            }
        }

        internal enum StorageType
        {
            Address,
            Temp,
            Const
        }

        internal class StorageTypedValue
        {
            internal StorageType Type { get; set; }
            internal LLVMValueRef Value { get; set; }
        }

        internal class NamedTempValue
        {
            internal string Name { get; set; }
            internal LLVMValueRef Value { get; set; }

            internal NamedTempValue(LLVMValueRef value, string name = null)
            {
                this.Value = value;
                this.Name = name;
            }
        }

        /// Invoke an LLVM operation with given value mappings. 
        /// The operation is supposed to return a temp value to be stored.
        private void InvokeOperation(
            Opcode op,
            ExtendedOpcode? exop,
            IOperand[] operands,
            TempOperand result,
            Func<ValueMappings, NamedTempValue> emitFunc)
        {
            ValueMappings mappings = new ValueMappings(operands.Length + (result == null ? 0 : 1));
            StorageTypedValue[] stvalues = mappings.Values;
            int i = 0;
            for (; i < operands.Length; i++)
            {
                IOperand operand = operands[i];
                stvalues[i] = MakeStorageTypedValue(operand);
            }

            if (result != null)
            {
                stvalues[i] = MakeStorageTypedValue(result);
            }

            NamedTempValue ntv = emitFunc(mappings);
            if (ntv != null)
            {
                string name = ntv.Name;
                if (name == null)
                {
                    name = LLVM.GetValueName(ntv.Value);
                }
                temps[name] = ntv.Value;
            }
        }

        /// Invoke an LLVM operation with given value mappings. 
        /// The operation doesn't produce new temp values.
        private void InvokeOperation(
            Opcode op,
            ExtendedOpcode? exop,
            IOperand[] operands,
            Action<ValueMappings> emitFunc)
        {
            InvokeOperation(
                op,
                exop,
                operands,
                null,
                (vm) =>
                {
                    emitFunc(vm);
                    return null;
                });
        }

        private StorageTypedValue MakeStorageTypedValue(IOperand operand)
        {
            LLVMValueRef value = default(LLVMValueRef);
            StorageType stype = default(StorageType);
            switch (operand.OperandType)
            {
                case OperandType.Temp:
                    stype = StorageType.Temp;
                    value = GetTempValue(operand);
                    break;
                case OperandType.Local:
                    stype = StorageType.Address;
                    value = GetLocalAddr(operand);
                    break;
                case OperandType.Argument:
                    stype = StorageType.Address;
                    value = GetArgAddr(operand);
                    break;
                case OperandType.Const:
                    stype = StorageType.Const;
                    value = GetConstValue(operand);
                    break;
            }
            return new StorageTypedValue
            {
                Type = stype,
                Value = value
            };
        }

        private LLVMBasicBlockRef GetBranchTarget(IOperand operand)
        {
            BranchTargetOperand bto = (BranchTargetOperand)operand;
            int target = bto.PC;
            return GetOrAddBasicBlock(target, false);
        }

        private LLVMValueRef GetArgAddr(IOperand operand)
        {
            ArgumentOperand aod = (ArgumentOperand)operand;
            return this.argAddrs[aod.Index];
        }

        private LLVMValueRef GetLocalAddr(IOperand operand)
        {
            LocalOperand lod = (LocalOperand)operand;
            return this.localAddrs[lod.Index];
        }

        private LLVMValueRef GetTempValue(IOperand operand)
        {
            TempOperand tod = (TempOperand)operand;
            return temps[tod.Name];
        }

        private LLVMValueRef GetConstValue(IOperand operand)
        {
            ConstOperand cod = (ConstOperand)operand;
            if (cod is Int32ConstOperand)
            {
                return LLVM.ConstInt(LLVM.Int32Type(), (ulong)((Int32ConstOperand)cod).Value, true);
            }
            if (cod is Int64ConstOperand)
            {
                return LLVM.ConstInt(LLVM.Int64Type(), (ulong)((Int64ConstOperand)cod).Value, true);
            }
            if (cod is Float32ConstOperand)
            {
                return LLVM.ConstReal(LLVM.FloatType(), ((Float32ConstOperand)cod).Value);
            }
            if (cod is Float64ConstOperand)
            {
                return LLVM.ConstReal(LLVM.FloatType(), ((Float64ConstOperand)cod).Value);
            }

            throw new Exception("Unexpected. The const operand is tno recognized.");
        }

        private static LLVMTypeRef TranslateType(ClrType ctyp)
        {
            if (ctyp == RuntimeInformation.BoolType)
            {
                return LLVM.Int1Type();
            }
            if (ctyp == RuntimeInformation.Int8Type)
            {
                return LLVM.Int8Type();
            }
            if (ctyp == RuntimeInformation.Int16Type || ctyp == RuntimeInformation.Int8Type)
            {
                return LLVM.Int16Type();
            }
            if (ctyp == RuntimeInformation.Int32Type || ctyp == RuntimeInformation.UInt16Type)
            {
                return LLVM.Int32Type();
            }
            if (ctyp == RuntimeInformation.Int64Type || ctyp == RuntimeInformation.UInt32Type)
            {
                return LLVM.Int64Type();
            }
            if (ctyp == RuntimeInformation.CharType)
            {
                return LLVM.Int16Type(); // Unicode
            }
            if (ctyp == RuntimeInformation.Float32Type || ctyp == RuntimeInformation.Float64Type)
            {
                return LLVM.FloatType();
            }
            if (ctyp == RuntimeInformation.NativeIntType || ctyp == RuntimeInformation.NativeUnsignedIntType)
            {
                return LLVM.Int64Type();
            }
            if (ctyp == RuntimeInformation.StringType)
            {
                return LLVM.PointerType(LLVM.Int16Type(), 0); // 0 = default address sapce 
            }
            if (ctyp == RuntimeInformation.VoidTypeInstance)
            {
                return LLVM.VoidType();
            }

            Type typ = ctyp.AsSystemType;
            if (typ.IsClass)
            {
                return LLVM.PointerType(LLVM.Int64Type(), 0); // 0 = default address sapce 
            }

            throw new Exception($"TODO: Cannot handle type { typ.Name } yet.");
        }
    }
}