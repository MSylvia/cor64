﻿using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cor64.Mips.R4300I.CP0;
using cor64.IO;
using cor64.Debugging;

namespace cor64.Mips.R4300I
{
    public class Interpreter : CoreR4300I
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private bool m_WarnInfiniteJump = false;


        public Interpreter(bool debug) :
            base(new Disassembler("o32", debug ? BaseDisassembler.Mode.Debug : BaseDisassembler.Mode.Fast))
        {
        }

        private static long L(ulong v)
        {
            return (long)v;
        }

        private bool IsReserved(DecodedInstruction inst)
        {
            return
                (((inst.Op.Flags & ExecutionFlags.Reserved32) == ExecutionFlags.Reserved32) && !IsOperation64) ||
                (((inst.Op.Flags & ExecutionFlags.Reserved64) == ExecutionFlags.Reserved64) && IsOperation64);
        }

        public override string Description => "Interpreter";

        protected override bool Execute()
        {
            /* Step clock */
            CoreClock.NextTick();

            /* Step coprocessor 0 */
            Cop0.ProcessorTick();

            return base.Execute();
        }

        protected override void Add32(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            /* Operands must be valid 32-bit sign extended in 64-bit mode */
            /* Immediate is always signed-extended */
            /* Unsigned operations don't cause integer overflow exceptions */

            /* We cheat by doing 32-bit sign-extending for immedate in 64-bit mode,
             * the processor will use 64-bit values to add, and check overflow
             * then store the result as a signed 32-bit value, we force everything to
             * use 32-bits so the CLR runtime can throw exceptions for us */

            bool isUnsigned = inst.IsUnsigned();
            bool isImmediate = inst.IsImmediate();
            uint operandA = ReadGPR32(inst.Source);
            uint operandB = isImmediate ? (uint)(short)inst.Immediate : ReadGPR32(inst.Target);
            uint result = 0;
            int dest = isImmediate ? inst.Target : inst.Destination;

            if (isUnsigned)
            {
                unchecked
                {
                    result = operandA + operandB;
                }
            }
            else
            {
                int r = 0;
                int _a = (int)operandA;
                int _b = (int)operandB;

                try
                {
                    checked
                    {
                        r = _a + _b;
                    }
                }
                catch (OverflowException)
                {
                    SetExceptionState(ExceptionType.Overflow);
                    return;
                }

                result = (uint)r;
            }

            Writeback32(dest, result);
        }

        protected override void BitwiseLogic(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isImmediate = inst.IsImmediate();
            int dest = isImmediate ? inst.Target : inst.Destination;
            ulong source = ReadGPR64(inst.Source);
            ulong target = isImmediate ? inst.Immediate : ReadGPR64(inst.Target);

            void Bitwise32(ArithmeticOp compareOp, uint a, uint b)
            {
                uint result = 0;

                switch (compareOp)
                {
                    case ArithmeticOp.AND: result = a & b; break;
                    case ArithmeticOp.OR:  result = a | b; break;
                    case ArithmeticOp.XOR: result = a ^ b; break;
                    case ArithmeticOp.NOR: result = ~(a | b); break;
                    default: throw new InvalidOperationException("Bitwise logic 32");
                }

                Writeback64((isImmediate ? inst.Target : inst.Destination), result);
            }

            void Bitwise64(ArithmeticOp compareOp, ulong a, ulong b)
            {
                ulong result = 0;

                switch (compareOp)
                {
                    case ArithmeticOp.AND: result = a & b; break;
                    case ArithmeticOp.OR: result = a | b; break;
                    case ArithmeticOp.XOR: result = a ^ b; break;
                    case ArithmeticOp.NOR: result = ~(a | b); break;
                    default: throw new InvalidOperationException("Bitwise logic 64");
                }

                Writeback64((isImmediate ? inst.Target : inst.Destination), result);
            }


            if (IsOperation64)
            {
                Bitwise64(inst.Op.ArithmeticType, source, target);
            }
            else
            {
                Bitwise32(inst.Op.ArithmeticType, (uint)source, (uint)target);
            }
        }

        protected override void Add64(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            bool isImmediate = inst.IsImmediate();
            ulong operandA = ReadGPR64(inst.Source);
            ulong operandB = isImmediate ? (ulong)(short)inst.Immediate : ReadGPR64(inst.Target);
            ulong result = 0;
            int dest = isImmediate ? inst.Target : inst.Destination;

            if (isUnsigned)
            {
                unchecked
                {
                    result = operandA + operandB;
                }
            }
            else
            {
                long r = 0;
                long _a = (long)operandA;
                long _b = (long)operandB;

                try
                {
                    checked
                    {
                        r = _a + _b;
                    }
                }
                catch (OverflowException)
                {
                    SetExceptionState(ExceptionType.Overflow);
                    return;
                }

                result = (ulong)r;
            }

            Writeback64(dest, result);
        }

        protected override void Divide32(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            uint operandA = ReadGPR32(inst.Source);
            uint operandB = ReadGPR32(inst.Target);
            uint result = 0;
            uint remainder = 0;

            try
            {

                if (isUnsigned)
                {
                    unchecked
                    {
                        result = operandA / operandB;
                        remainder = operandA % operandB;
                    }
                }
                else
                {
                    int q = 0;
                    int r = 0;
                    int _a = (int)operandA;
                    int _b = (int)operandB;

                    checked
                    {
                        q = _a / _b;
                        r = _a % _b;
                    }

                    result = (uint)q;
                    remainder = (uint)r;
                }

                WritebackHiLo32(remainder, result);
            }
            catch (DivideByZeroException)
            {
                return;
            }
        }

        protected override void Divide64(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            ulong operandA = ReadGPR64(inst.Source);
            ulong operandB = ReadGPR64(inst.Target);
            ulong result = 0;
            ulong remainder = 0;

            try
            {

                if (isUnsigned)
                {
                    unchecked
                    {
                        result = operandA / operandB;
                        remainder = operandA % operandB;
                    }
                }
                else
                {
                    long q = 0;
                    long r = 0;
                    long _a = (long)operandA;
                    long _b = (long)operandB;

                    checked
                    {
                        q = _a / _b;
                        r = _a % _b;
                    }

                    result = (ulong)q;
                    remainder = (ulong)r;
                }

                WritebackHiLo64(remainder, result);
            }
            catch (DivideByZeroException)
            {
                return;
            }
        }

        protected override void Multiply32(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            uint operandA = ReadGPR32(inst.Source);
            uint operandB = ReadGPR32(inst.Target);
            ulong result = 0;
            uint resultLo = 0;
            uint resultHi = 0;

            /* MIPS document says no overflow exceptions, ever... */
            unchecked
            {
                if (isUnsigned)
                    result = (ulong)operandA * (ulong)operandB;
                else
                    result = (ulong)((long)(int)operandA * (long)(int)operandB);

            }

            resultLo = (uint)(result);
            resultHi = (uint)(result >> 32);

            WritebackHiLo32(resultHi, resultLo);
        }

        protected override void Multiply64(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            ulong operandA = ReadGPR64(inst.Source);
            ulong operandB = ReadGPR64(inst.Target);

            /* MIPS document says no overflow exceptions, ever... */
            unchecked
            {
                UInt128 value;

                if (isUnsigned)
                {
                    value = CoreUtils.Multiply64_Unsigned(operandA, operandB);
                }
                else
                {
                    value = CoreUtils.Multiply64_Signed(operandA, operandB);
                }

                WritebackHiLo64(value.hi, value.lo);
            }
        }

        protected override void Shift32(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            int shiftAmount = 0;

            if (inst.IsVariableShift())
            {
                shiftAmount = (int)(ReadGPR32(inst.Source) & 0x3F);
            }
            else
            {
                shiftAmount = inst.ShiftAmount;
            }

            uint value = ReadGPR32(inst.Target);

            if (inst.Op.ArithmeticType == ArithmeticOp.LSHIFT)
            {
                value <<= shiftAmount;
            }
            else
            {
                bool sign = ((value >> 31) == 1);

                value >>= shiftAmount;

                if (sign && !inst.IsUnsigned())
                {
                    /* Sign extend */
                    value |= ~(~0U >> shiftAmount);
                }
            }

            Writeback32(inst.Destination, value);
        }

        protected override void Shift64(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            int shiftAmount = 0;

            if (inst.IsVariableShift())
            {
                shiftAmount = (int)(ReadGPR32(inst.Source) & 0x3F);
            }
            else
            {
                shiftAmount = inst.ShiftAmount;

                if (inst.IsShift32())
                {
                    shiftAmount += 32;
                }
            }

            ulong value = ReadGPR64(inst.Target);

            if (inst.Op.ArithmeticType == ArithmeticOp.LSHIFT)
            {
                value <<= shiftAmount;
            }
            else
            {
                bool sign = ((value >> 63) == 1);

                value >>= shiftAmount;

                if (sign && !inst.IsUnsigned())
                {
                    /* Sign extend */
                    value |= ~(~0UL >> shiftAmount);
                }
            }

            Writeback64(inst.Destination, value);
        }

        protected override void Subtract32(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            uint operandA = ReadGPR32(inst.Source);
            uint operandB = ReadGPR32(inst.Target);
            uint result = 0;

            if (isUnsigned)
            {
                unchecked
                {
                    result = operandA - operandB;
                }
            }
            else
            {
                int r = 0;
                int _a = (int)operandA;
                int _b = (int)operandB;

                try
                {
                    checked
                    {
                        r = _a - _b;
                    }
                }
                catch (OverflowException)
                {
                    SetExceptionState(ExceptionType.Overflow);
                    return;
                }

                result = (uint)r;
            }

            Writeback32(inst.Destination, result);
        }

        protected override void Subtract64(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool isUnsigned = inst.IsUnsigned();
            ulong operandA = ReadGPR64(inst.Source);
            ulong operandB = ReadGPR64(inst.Target);
            ulong result = 0;

            if (isUnsigned)
            {
                unchecked
                {
                    result = operandA - operandB;
                }
            }
            else
            {
                long r = 0;
                long _a = (long)operandA;
                long _b = (long)operandB;

                try
                {
                    checked
                    {
                        r = _a - _b;
                    }
                }
                catch (OverflowException)
                {
                    SetExceptionState(ExceptionType.Overflow);
                    return;
                }

                result = (ulong)r;
            }

            Writeback64(inst.Destination, result);
        }

        protected override void SetOnLessThan(DecodedInstruction inst)
        {
            if (IsReserved(inst))
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            bool unsigned = inst.IsUnsigned();
            bool immediate = inst.IsImmediate();
            ulong mask = IsOperation64 ? 0xFFFFFFFFFFFFFFFF : 0x00000000FFFFFFFF;
            int dest = immediate ? inst.Target : inst.Destination;
            byte result = 0;

            ulong operandA = ReadGPR64(inst.Source) & mask;
            ulong operandB = 0;

            if (immediate)
            {
                operandB = (ulong)(int)inst.Immediate;
            }
            else
            {
                operandB = ReadGPR64(inst.Target);
            }

            operandB &= mask;

            if (unsigned)
            {
                if (operandA < operandB)
                {
                    result = 1;
                }
            }
            else
            {
                if (!IsOperation64)
                {
                    operandA = (ulong)(int)operandA;
                    operandB = (ulong)(int)operandB;
                }

                if ((long)operandA < (long)operandB)
                {
                    result = 1;
                }
            }

            Writeback64(dest, result);
        }

        protected override void TransferReg(DecodedInstruction inst)
        {
            ulong value = 0;

            // TODO: Reexamine if IsOperation64 should be ignored

            /* Source value to copy */
            switch (inst.Op.XferSource)
            {
                case RegBoundType.Hi: value = State.Hi; break;
                case RegBoundType.Lo: value = State.Lo; break;
                case RegBoundType.Gpr: value = ReadGPR64(inst.Source); break;
                case RegBoundType.Cp0:
                    {
                        if (!EnableCp0)
                        {
                            SetExceptionState(ExceptionType.Unusable);
                            return;
                        }

                        value = Cp0Regs.Read(inst.Destination);

                        /* If running 64-bit mode, with the 32-bit version, then sign extend */
                        if (IsOperation64 && inst.IsData32())
                        {
                            value = (ulong)(int)(uint)value;
                        }

                        break;
                    }
                case RegBoundType.Cp1:
                    {
                        if (!EnableCp1)
                        {
                            SetExceptionState(ExceptionType.Unusable);
                            return;
                        }

                        value = State.FPR.S32[inst.FloatSource];

                        /* If running 64-bit mode, with the 32-bit version, then sign extend */
                        if (IsOperation64 && inst.IsData32())
                        {
                            value = (ulong)(int)(uint)value;
                        }

                        break;
                    }

                case RegBoundType.Cp1Ctl:
                    {
                        if (!EnableCp1)
                        {
                            SetExceptionState(ExceptionType.Unusable);
                            return;
                        }

                        value = State.FCR.Value;

                        /* If running 64-bit mode, then sign extend */
                        if (IsOperation64)
                        {
                            value = (ulong)(int)(uint)value;
                        }

                        break;
                    }
            }

            /* Target */
            switch (inst.Op.XferTarget)
            {
                case RegBoundType.Gpr:
                    {
                        Writeback64(inst.Destination, IsOperation64 ? value : (uint)value);
                        break;
                    }

                case RegBoundType.Hi:
                    {
                        State.Hi = IsOperation64 ? value : (uint)value;
                        break;
                    }

                case RegBoundType.Lo:
                    {
                        State.Lo = IsOperation64 ? value : (uint)value;
                        break;
                    }
                case RegBoundType.Cp0:
                    {
                        Cp0Regs.WriteFromGpr(inst.Destination, value);
                        break;
                    }
                case RegBoundType.Cp1:
                    {
                        if (!EnableCp1)
                        {
                            SetExceptionState(ExceptionType.Unusable);
                            return;
                        }

                        if (Cop0.SR.FRMode)
                        {
                            value &= 0xFFFFFFFF;
                            State.FPR.S64[inst.FloatSource] = value;
                        }
                        else
                        {
                            // XXX: Needs testing
                            // Load hi for even
                            if (inst.FloatSource % 2 == 0)
                            {
                                value >>= 16;
                            }
                            else
                            {
                                value &= 0xFFFF;
                            }

                            State.FPR.S32[inst.FloatSource] = (uint)value;
                        }

                        break;
                    }
                case RegBoundType.Cp1Ctl:
                    {
                        if (!EnableCp1)
                        {
                            SetExceptionState(ExceptionType.Unusable);
                            return;
                        }

                        State.FCR.Value = (uint)value;
                        break;
                    }
            }
        }

        protected override void Branch(DecodedInstruction inst)
        {
            bool isLikely = inst.IsLikely();
            bool isLink = inst.IsLink();
            TargetAddress = CoreUtils.ComputeBranchPC(IsOperation64, m_Pc, CoreUtils.ComputeBranchTargetOffset(inst.Immediate));
            ulong source = State.GPR_64[inst.Source];
            ulong target = State.GPR_64[inst.Target];

            TakeBranch = ComputeBranchCondition(source, target, inst.Op.XferTarget, inst.Op.ArithmeticType);

            if (isLink)
            {
                Writeback64(31, m_Pc + 8);
            }

            BranchDelay = !isLikely || (TakeBranch && isLikely);

            /* Clear target if not taken */
            if (!TakeBranch)
            {
                TargetAddress = 0;
            }
        }

        protected override void Jump(DecodedInstruction inst)
        {
            bool isLink = inst.IsLink();
            bool isRegister = inst.IsRegister();
            TargetAddress = CoreUtils.ComputeTargetPC(IsOperation64, isRegister, m_Pc, State.GPR_64[inst.Source], inst.Inst.target);
            UnconditionalJump = true;

            if (isLink)
            {
                Writeback64(31, m_Pc + 8);
            }

            if (!isRegister && (uint)TargetAddress == (uint)m_Pc && !m_WarnInfiniteJump)
            {
                Log.Warn("An unconditional infinite jump was hit");
                m_WarnInfiniteJump = true;
            }
        }

        protected override void Store(DecodedInstruction inst)
        {
            ulong address = 0;
            int size = inst.DataSize();
            ExecutionFlags flags = inst.Op.Flags;

            if (!IsOperation64 && size == 8)
            {
                SetExceptionState(ExceptionType.Reserved);
                return;
            }

            if (!IsOperation64)
            {
                int baseAddress = (int)ReadGPR32(inst.Source);
                int offset = (short)inst.Immediate;
                address = (ulong)(baseAddress + offset);
            }
            else
            {
                long baseAddress = (long)ReadGPR64(inst.Source);
                long offset = (short)inst.Immediate;
                address = (ulong)(baseAddress + offset);
            }

            switch (size)
            {
                default: throw new InvalidOperationException("How did this happen?");
                case 1: m_DataMemory.Data8 = (byte)ReadGPR64(inst.Target); break;
                case 2: m_DataMemory.Data16Swp = (ushort)ReadGPR64(inst.Target); break;
                case 4: m_DataMemory.Data32Swp = (uint)ReadGPR32(inst.Target); break;
                case 8: m_DataMemory.Data64Swp = ReadGPR64(inst.Target); break;
            }

            try
            {
                if (inst.IsLeft())
                {
                    int numOff = (int)(address % (uint)size);
                    int numBytes = size - numOff;
                    long addr = (((long)address - numOff) + (size - 1)) - numOff;
                    int index = size - numOff - 1;
                    byte[] data = m_DataMemory.ReadBuffer();

                    for (int i = 0; i < numBytes; i++)
                    {
                        m_DataMemory.Data8 = data[index - i];
                        m_DataMemory.WriteData(addr - i, 1, true);
                    }
                }
                else if (inst.IsRight())
                {
                    int numOff = (int)(address % (uint)size);
                    int numBytes = 5 - (size - numOff);
                    byte[] data = m_DataMemory.ReadBuffer();

                    for (int i = 0; i < numBytes; i++)
                    {
                        m_DataMemory.Data8 = data[3 - i];
                        m_DataMemory.WriteData((long)address - i, 1, true);
                    }
                }
                else
                {
                    /* Conditional store notes:
                     * The LLBit must be set a Load linked operation before this type of operation
                     * TODO: When ERET instruction is supported, it causes any conditional store to fail afterwards
                     */

                    bool LLMode = (flags & ExecutionFlags.Link) == ExecutionFlags.Link;

                    if (!LLMode || (LLMode && State.LLBit))
                    {
                        m_DataMemory.WriteData((long)address, size, true);

                        //if (m_Debug && inst.Target == 31)
                        //{
                        //    AddInstructionNote(String.Format("Stored return address {0:X8} into memory", m_DataMemory.Data32));
                        //}
                    }

                    /* Store conditional */
                    if (LLMode)
                    {
                        Writeback64(inst.Target, State.LLBit ? 1U : 0U);
                    }
                }
            }
            catch (MipsException e)
            {
                SetExceptionState(e.Exception);
            }
        }

        protected override void Load(DecodedInstruction inst)
        {
            /* Modes: Upper Immediate, Unsigned / Signed (8, 16, 32, 64), Left / Right (32, 64), Load Linked */
            ulong address = 0;
            int size = 0;

            bool upperImm = inst.IsImmediate();
            bool loadLinked = inst.IsLink();
            bool left = inst.IsLeft();
            bool right = inst.IsRight();
            bool unsigned = inst.IsUnsigned();

            try
            {
                if (upperImm)
                {
                    uint imm = inst.Immediate;
                    imm <<= 16;

                    Writeback32(inst.Target, imm);

                    return;
                }
                else
                {
                    size = inst.DataSize();

                    if (!IsOperation64 && size == 8)
                    {
                        SetExceptionState(ExceptionType.Reserved);
                        return;
                    }

                    if (!IsOperation64)
                    {
                        int baseAddress = (int)ReadGPR32(inst.Source);
                        int offset = (short)inst.Immediate;
                        address = (ulong)(baseAddress + offset);
                    }
                    else
                    {
                        long baseAddress = (long)ReadGPR64(inst.Source);
                        long offset = (short)inst.Immediate;
                        address = (ulong)(baseAddress + offset);
                    }

                    // REF: https://www2.cs.duke.edu/courses/cps104/fall02/homework/lwswlr.html

                    if (left)
                    {
                        int numOff = (int)(address % (uint)size);
                        int numBytes = size - numOff;
                        uint val = ReadGPR32(inst.Target);

                        for (int i = 0; i < numBytes; i++)
                        {
                            int leftShift = 8 * (numOff + i);
                            uint mask = 0xFFU << leftShift;
                            m_DataMemory.ReadData((long)address + i, 1, true);
                            val &= ~mask;
                            val |= ((uint)m_DataMemory.Data8 << leftShift);
                        }

                        m_DataMemory.Data32 = val;
                    }
                    else if (right)
                    {
                        int numOff = (int)(address % (uint)size);
                        int numBytes = 5 - (size - numOff);
                        uint val = ReadGPR32(inst.Target);

                        for (int i = 0; i < numBytes; i++)
                        {
                            int shift = 8 * (i + numOff);
                            uint mask = 0xFFU << shift;
                            m_DataMemory.ReadData((long)address - i, 1, true);
                            val &= ~mask;
                            val |= ((uint)m_DataMemory.Data8 << shift);
                        }

                        m_DataMemory.Data32Swp = val;
                    }
                    else
                    {
                        if (loadLinked)
                        {
                            State.LLBit = true;
                        }

                        m_DataMemory.ReadData((long)address, size, true);
                    }

                    if (unsigned)
                    {
                        switch (size)
                        {
                            default: throw new InvalidOperationException("How did this happen (unsigned)?");
                            case 1: Writeback64(inst.Target, m_DataMemory.Data8); break;
                            case 2: Writeback64(inst.Target, m_DataMemory.Data16Swp); break;
                            case 4: Writeback64(inst.Target, m_DataMemory.Data32Swp); break;
                            case 8: Writeback64(inst.Target, m_DataMemory.Data64Swp); break;
                        }
                    }
                    else
                    {
                        switch (size)
                        {
                            default: throw new InvalidOperationException("How did this happen?");
                            case 1: Writeback64(inst.Target, (ulong)(sbyte)m_DataMemory.Data8); break;
                            case 2: Writeback64(inst.Target, (ulong)(short)m_DataMemory.Data16Swp); break;
                            case 4: Writeback64(inst.Target, (ulong)(int)m_DataMemory.Data32Swp); break;
                            case 8: Writeback64(inst.Target, (ulong)(long)m_DataMemory.Data64Swp); break;
                        }
                    }
                }
            }
            catch (MipsException e)
            {
                SetExceptionState(e.Exception);
            }
        }

        protected override void Cache(DecodedInstruction inst)
        {


        }

        protected override void Sync(DecodedInstruction inst)
        {
            
        }

        protected override void FloatLoad(DecodedInstruction inst)
        {
            try
            {
                var size = inst.DataSize();

                long baseAddress = (long)ReadGPR64(inst.Source);
                long offset = (short)inst.Immediate;
                var address = (ulong)(baseAddress + offset);

                m_DataMemory.ReadData((long)address, size, true);

                switch (size)
                {
                    default: throw new InvalidOperationException("Unsupported FPU Load Size: " + size.ToString());
                    case 4: WriteFPR_W(inst.FloatTarget, m_DataMemory.Data32Swp); break;
                    case 8: WriteFPR_L(inst.FloatTarget, m_DataMemory.Data64Swp); break;
                }

                /* If loading a doubleword and FR = 0, we don't care, we bypass 32-bit stuff */
            }
            catch (MipsException e)
            {
                SetExceptionState(e.Exception);
            }

            /* TODO: Simulate odd result registers for 64-bit reads ? */
        }

        protected override void FloatStore(DecodedInstruction inst)
        {
            var size = inst.DataSize();

            long baseAddress = (long)ReadGPR64(inst.Source);
            long offset = (short)inst.Immediate;
            var address = (ulong)(baseAddress + offset);

            switch (size)
            {
                default: throw new InvalidOperationException("Unsupported FPU Store Size: " + size.ToString());
                case 4: m_DataMemory.Data32Swp = ReadFPR_W(inst.FloatTarget); break;
                case 8: m_DataMemory.Data64Swp = ReadFPR_L(inst.FloatTarget); break;
            }

            try
            {
                m_DataMemory.WriteData((long)address, size, true);
            }
            catch (MipsException e)
            {
                SetExceptionState(e.Exception);
            }
        }

        private void ConvertFromSingle(int source, int dest, ExecutionFlags flags)
        {
            float value = ReadFPR_S(source);

            if ((flags & ExecutionFlags.DataS) == ExecutionFlags.DataS)
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("float32 to float32");
            }
            else if ((flags & ExecutionFlags.DataD) == ExecutionFlags.DataD)
            {
                WriteFPR_D(dest, (double)value);
            }
            else if ((flags & ExecutionFlags.Data64) == ExecutionFlags.Data64)
            {
                WriteFPR_L(dest, (ulong)value);
            }
            else
            {
                WriteFPR_W(dest, (uint)value);
            }
        }

        private void ConvertFromDouble(int source, int dest, ExecutionFlags flags)
        {
            double value = ReadFPR_D(source);

            if ((flags & ExecutionFlags.DataS) == ExecutionFlags.DataS)
            {
                WriteFPR_S(dest, (float)value);
            }
            else if ((flags & ExecutionFlags.DataD) == ExecutionFlags.DataD)
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("float64 to float64");
            }
            else if ((flags & ExecutionFlags.Data64) == ExecutionFlags.Data64)
            {
                WriteFPR_L(dest, (ulong)(long)value);
            }
            else
            {
                WriteFPR_W(dest, (uint)(long)value);
            }
        }

        private void ConvertFromUInt32(int source, int dest, ExecutionFlags flags)
        {
            if ((flags & ExecutionFlags.DataS) == ExecutionFlags.DataS)
            {
                WriteFPR_SNR(dest, (int)ReadFPR_W(source));
            }
            else if ((flags & ExecutionFlags.DataD) == ExecutionFlags.DataD)
            {
                WriteFPR_DNR(dest, (int)ReadFPR_W(source));
            }
            else if ((flags & ExecutionFlags.Data64) == ExecutionFlags.Data64)
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("word to word");
            }
            else
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("word to dword");
            }
        }

        private void ConvertFromUInt64(int source, int dest, ExecutionFlags flags)
        {
            if ((flags & ExecutionFlags.DataS) == ExecutionFlags.DataS)
            {
                WriteFPR_SNR(dest, (long)ReadFPR_L(source));
            }
            else if ((flags & ExecutionFlags.DataD) == ExecutionFlags.DataD)
            {
                WriteFPR_DNR(dest, (long)ReadFPR_L(source));
            }
            else if ((flags & ExecutionFlags.Data64) == ExecutionFlags.Data64)
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("word to word");
            }
            else
            {
                // TODO: Throw invalid exception
                throw new InvalidOperationException("word to dword");
            }
        }

        protected override void Add(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, ReadFPR_S(inst.FloatSource) + ReadFPR_S(inst.FloatTarget));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, ReadFPR_D(inst.FloatSource) + ReadFPR_D(inst.FloatTarget));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Subtract(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, ReadFPR_S(inst.FloatSource) - ReadFPR_S(inst.FloatTarget));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, ReadFPR_D(inst.FloatSource) - ReadFPR_D(inst.FloatTarget));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Multiply(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, ReadFPR_S(inst.FloatSource) * ReadFPR_S(inst.FloatTarget));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, ReadFPR_D(inst.FloatSource) * ReadFPR_D(inst.FloatTarget));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Divide(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, ReadFPR_S(inst.FloatSource) / ReadFPR_S(inst.FloatTarget));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, ReadFPR_D(inst.FloatSource) / ReadFPR_D(inst.FloatTarget));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void SqrRoot(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, (float)Math.Sqrt(ReadFPR_S(inst.FloatSource)));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, Math.Sqrt(ReadFPR_D(inst.FloatSource)));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Abs(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, Math.Abs(ReadFPR_S(inst.FloatSource)));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, Math.Abs(ReadFPR_D(inst.FloatSource)));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Mov(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Neg(DecodedInstruction inst)
        {
            if (inst.Format == FpuValueType.FSingle)
            {
                WriteFPR_S(inst.FloatDest, -ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                WriteFPR_D(inst.FloatDest, -ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }

        protected override void Round(DecodedInstruction inst)
        {
            double roundedValue;

            if (inst.Format == FpuValueType.FSingle)
            {
                roundedValue = Math.Round(ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                roundedValue = Math.Round(ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
                return;
            }

            if (inst.IsData32())
            {
                WriteFPR_W(inst.FloatDest, (uint)roundedValue);
            }
            else
            {
                WriteFPR_L(inst.FloatDest, (ulong)roundedValue);
            }
        }

        protected override void Truncate(DecodedInstruction inst)
        {
            double roundedValue;

            if (inst.Format == FpuValueType.FSingle)
            {
                roundedValue = Math.Truncate(ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                roundedValue = Math.Truncate(ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
                return;
            }

            if (inst.IsData32())
            {
                WriteFPR_W(inst.FloatDest, (uint)roundedValue);
            }
            else
            {
                WriteFPR_L(inst.FloatDest, (ulong)roundedValue);
            }
        }

        protected override void Ceil(DecodedInstruction inst)
        {
            double roundedValue;

            if (inst.Format == FpuValueType.FSingle)
            {
                roundedValue = Math.Ceiling(ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                roundedValue = Math.Ceiling(ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
                return;
            }

            if (inst.IsData32())
            {
                WriteFPR_W(inst.FloatDest, (uint)roundedValue);
            }
            else
            {
                WriteFPR_L(inst.FloatDest, (ulong)roundedValue);
            }
        }

        protected override void Floor(DecodedInstruction inst)
        {
            double roundedValue;

            if (inst.Format == FpuValueType.FSingle)
            {
                roundedValue = Math.Floor(ReadFPR_S(inst.FloatSource));
            }
            else if (inst.Format == FpuValueType.FDouble)
            {
                roundedValue = Math.Floor(ReadFPR_D(inst.FloatSource));
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
                return;
            }

            if (inst.IsData32())
            {
                WriteFPR_W(inst.FloatDest, (uint)roundedValue);
            }
            else
            {
                WriteFPR_L(inst.FloatDest, (ulong)roundedValue);
            }
        }

        protected override void Convert(DecodedInstruction inst)
        {
            switch (inst.Format)
            {
                case FpuValueType.FSingle: ConvertFromSingle(inst.FloatSource, inst.FloatDest, inst.Op.Flags); break;
                case FpuValueType.FDouble: ConvertFromDouble(inst.FloatSource, inst.FloatDest, inst.Op.Flags); break;
                case FpuValueType.Word: ConvertFromUInt32(inst.FloatSource, inst.FloatDest, inst.Op.Flags); break;
                case FpuValueType.Doubleword: ConvertFromUInt64(inst.FloatSource, inst.FloatDest, inst.Op.Flags); break;
                default: SetExceptionState(FpuExceptionFlags.Unimplemented); break;
            }
        }

        /* Exceptions: Invalid and Unimplemented */
        protected override void Condition(DecodedInstruction inst)
        {
            bool flag_signaling = inst.Op.ArithmeticType == ArithmeticOp.SIGNALING;
            bool flag_equal = inst.Op.Flags.TestFlag(ExecutionFlags.CondEq);
            bool flag_less_than = inst.Op.Flags.TestFlag(ExecutionFlags.CondLT);
            bool flag_greater_than = inst.Op.Flags.TestFlag(ExecutionFlags.CondGT);
            bool flag_not = inst.Op.Flags.TestFlag(ExecutionFlags.CondNot);
            bool flag_unordered = inst.Op.Flags.TestFlag(ExecutionFlags.CondUn);
            bool flag_forced_ordered = inst.Op.Flags.TestFlag(ExecutionFlags.CondOrd);

            bool result = false;

            double a = 0.0d;
            double b = 0.0d;
            bool hasNaN = false;

            if (inst.Format == FpuValueType.FSingle || inst.Format == FpuValueType.FDouble)
            {
                if (inst.Format == FpuValueType.FSingle)
                {
                    a = ReadFPR_S(inst.FloatSource);
                    b = ReadFPR_S(inst.FloatTarget);
                }
                else
                {
                    a = ReadFPR_D(inst.FloatSource);
                    b = ReadFPR_D(inst.FloatTarget);
                }

                hasNaN = double.IsNaN(a) || double.IsNaN(b);

                if (flag_forced_ordered && hasNaN)
                {
                    SetExceptionState(FpuExceptionFlags.Invalid);
                }
                else
                {
                    if (flag_unordered)
                    {
                        result |= hasNaN;

                        if (flag_signaling)
                        {
                            /* Compare that both are signaling NaNs */
                            // TODO: This needs real testing, and on some website I found that this always results false.
                            result |= (State.FPR.SignalNaNTable[inst.FloatSource] == State.FPR.SignalNaNTable[inst.FloatTarget]);
                        }
                    }

                    if (flag_equal)
                    {
                        result |= (a == b);
                    }

                    if (flag_less_than)
                    {
                        result |= (a < b);
                    }

                    if (flag_greater_than)
                    {
                        result |= (a > b);
                    }
                }

                if (flag_not)
                {
                    result = !result;
                }

                State.FPR.ConditionFlag = result;
                State.FCR.Condition = result;
            }
            else
            {
                SetExceptionState(FpuExceptionFlags.Unimplemented);
            }
        }
    }
}
