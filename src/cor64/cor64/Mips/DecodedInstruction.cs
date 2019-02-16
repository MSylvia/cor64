﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cor64.Mips
{
    public struct DecodedInstruction
    {
        /* Decoded Instruction data */
        private readonly BinaryInstruction m_Inst;
        private readonly Opcode m_Opcode;
        private readonly ulong m_Address;
        private readonly bool m_NoOp;
        private readonly bool m_LastInst;
  
        internal DecodedInstruction(ulong address, Opcode opcode, BinaryInstruction inst, bool emuNoOps, bool lastOne)
        {
            m_Opcode = opcode;
            m_Inst = inst;
            m_Address = address;
            m_NoOp = emuNoOps;
            m_LastInst = lastOne;
        }

        public BinaryInstruction Inst => m_Inst;

        public Opcode Op => m_Opcode;

        public bool IsBranch => Op.Family == OperationFamily.Branch || Op.Family == OperationFamily.BranchFpu;

        public ulong Address => m_Address;

        public String Opcode => m_Opcode.Op;

        /// <summary>
        /// Indicates an emulator level nop (not MIPS) which flag meaning the instruction was fetched outside the MIPS environment and cannot be used
        /// </summary>
        public bool EmulatorNop => m_NoOp;

        public bool LastOne => m_LastInst;

        public bool IsBranchConditional
        {
            get
            {
                if (!IsBranch) return false;

                switch (Op.ArithmeticType)
                {
                    default: return false;
                    case ArithmeticOp.EQUAL: 
                    case ArithmeticOp.NOT_EQUAL:
                    case ArithmeticOp.GREATER_THAN:
                    case ArithmeticOp.LESS_THAN:
                    case ArithmeticOp.GREATER_THAN_OR_EQUAL:
                    case ArithmeticOp.LESS_THAN_OR_EQUAL:
                    case ArithmeticOp.FALSE:
                    case ArithmeticOp.TRUE: return true;
                }
            }
        }

        public ulong GetStaticJumpTarget()
        {
            uint target = m_Inst.target << 2;       // (32-bit) Word-align target
            uint jumpAddress = (uint)m_Address + 4; // (32-bit) next instuction address
            jumpAddress &= 0xF0000000;            // Leave only the 4 most signficant bits
            jumpAddress |= (target & 0x0FFFFFFF); // Concat the target to PC address
            return jumpAddress;
        }

        public ulong GetStaticBranchTarget()
        {
            int offset = (short)m_Inst.imm;        // Sign extend to 32-bit
            offset <<= 2;                        // Word-align offset
            int target = (int)m_Address + 4; // Get the 32-bit PC address
            target += offset;                    // Add offset to PC
            return (uint)target;
        }

        public int Source => m_Inst.rs;
        public int Destination => m_Inst.rd;
        public int Target => m_Inst.rt;
        public uint Immediate => m_Inst.imm;
        public int ShiftAmount => m_Inst.sa;

        public bool IsNull => m_Opcode.Family == OperationFamily.Null;
    }
}
