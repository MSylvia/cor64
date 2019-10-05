﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cor64.Mips
{
    /// <summary>
    /// Decode BinaryInstructions into DecodedInstructions
    /// </summary>
    public abstract class BaseDisassembler
    {
        private Stream m_BinaryStream;
        private ulong m_BaseAddress;
        private String m_Abi;
        private Mode m_Mode;
        private ISymbolProvider m_SymbolProvider;

        public enum Mode
        {
            Fast,
            Debug,
            DebugFast
        }

        private static readonly String[] Cop1ConditionalTable = {
            "f", "un", "eq", "olt", "ult", "ole", "ule", "sf",
            "ngle", "sqe", "ngl", "lt", "nge", "le", "ngt"
        };

        protected BaseDisassembler(String abi, Mode mode)
        {
            m_Abi = abi;
            m_Mode = mode;
        }

        public virtual void SetStreamSource(Stream stream)
        {
            m_BinaryStream = stream;
        }

        public ulong CurrentAddress => m_BaseAddress;

        public String ABI => m_Abi;

        protected abstract Opcode DecodeOpcode(BinaryInstruction inst);

        public abstract String GetFullDisassembly(DecodedInstruction inst);

        public String GetSymbol(ulong address)
        {
            /* We must clamp the virtual address to what ELF expects */
            if ((address & 0xF0000000UL) == 0xA0000000UL)
            {
                address <<= 8;
                address >>= 8;
                address |= 0x80000000;
            }

            if (m_SymbolProvider != null)
            {
                var sym = m_SymbolProvider.GetSymbol(address);

                if (sym != null)
                {
                    return sym;
                }
            }

            return "";
        }

        public void AttachSymbolProvider(ISymbolProvider provider)
        {
            m_SymbolProvider = provider;
        }

        private DecodedInstruction _Disassemble()
        {
            DecodedInstruction decoded;

            /* Important note: When changing PC it becomes out of sync with the stream's position
             * So we need a better a way to handle so that PC jumps only update the Stream's position
             * else in sequencial IO, let the stream handle it basically */
            m_BinaryStream.Position = (long)m_BaseAddress;

            BinaryInstruction inst = new BinaryInstruction(ReadNext32());

            decoded = new DecodedInstruction(m_BaseAddress, DecodeOpcode(inst), inst, false, m_BinaryStream.Position + 4 >= m_BinaryStream.Length);

            return decoded;
        }

        public DecodedInstruction Disassemble(ulong address)
        {
            if (address < 0 || address >= (ulong)m_BinaryStream.Length)
                return new DecodedInstruction(0, new Opcode(), new BinaryInstruction(), true, false);

            m_BaseAddress = address;
            return _Disassemble();
        }

        public DecodedInstruction[] Disassemble(ulong address, int count)
        {
            DecodedInstruction[] disassembly = new DecodedInstruction[count];
            m_BaseAddress = address;

            for (int i = 0; i < count; i++) {
                disassembly[i] = _Disassemble();
            }

            return disassembly;
        }

        private uint ReadNext32()
        {
            var bytes = new Byte[4];
            m_BinaryStream.Read(bytes, 0, bytes.Length);

            /* Byteswaps big to little */
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }

        protected ulong ComputeJumpTarget(DecodedInstruction inst)
        {
            return CoreUtils.ComputeTargetPC(false, inst.Address, 0, inst.Inst.target);
        }

        protected ulong ComputeBranchTarget(DecodedInstruction inst)
        {
            return CoreUtils.ComputeBranchPC(false, inst.Address, CoreUtils.ComputeBranchTargetOffset(inst.Inst.imm));
        }

        protected static String DecodeCop1Format(BinaryInstruction inst)
        {
            switch (inst.fmtType)
            {
                case FpuValueType.FDouble: return "d";
                case FpuValueType.Doubleword: return "l";
                case FpuValueType.Word: return "w";
                case FpuValueType.FSingle: return "s";
                default: return "f";
            }
        }

        protected static String DecodeCop1Conditional(BinaryInstruction inst)
        {
            if (inst.fc >= 0 && inst.fc <= 15 && inst.fmtType != FpuValueType.Reserved)
                return Cop1ConditionalTable[inst.fc];

            else
                return "cond";
        }
    }
}