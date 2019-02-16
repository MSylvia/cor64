﻿using System;
using cor64.IO;

namespace cor64.PIF
{
    public class PIFMemory : PerpherialDevice
    {
        private MemMappedBuffer m_Rom = new MemMappedBuffer(0x7C0, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private MemMappedBuffer m_Ram = new MemMappedBuffer(0x40, MemMappedBuffer.MemModel.SINGLE_READ_WRITE);

        public PIFMemory(N64MemoryController n64MemoryController) : base(n64MemoryController, 0x100000)
        {
            AppendDevice(m_Rom, m_Ram);
        }
    }
}
