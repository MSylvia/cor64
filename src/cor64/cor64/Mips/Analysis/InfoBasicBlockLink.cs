﻿using System;
namespace cor64.Mips.Analysis
{
    public class InfoBasicBlockLink
    {
        private InfoBasicBlock m_LinkedBlock;
        private int m_BlockOffset;

        public InfoBasicBlockLink(InfoBasicBlock block, int offset)
        {
            m_LinkedBlock = block;
            m_BlockOffset = offset;
        }

        public InfoBasicBlock LinkedBlock => m_LinkedBlock;

        public int BlockOffset => m_BlockOffset;

        public ulong TargetAddress => m_LinkedBlock.Address + (ulong)m_BlockOffset;

        public void Modify(InfoBasicBlock block, int offset)
        {
            m_BlockOffset = offset;
            m_LinkedBlock = block;
        }

        public override string ToString()
        {
            return String.Format("Linked to block {0:X8} + {1}", m_LinkedBlock.Address, BlockOffset);
        }
    }
}
