﻿using cor64.IO;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cor64.RCP
{
    /* Parallel Interface for Cartridge and DiskDrive */
    public class PIMemory : PerpherialDevice
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private MemMappedBuffer m_DramAddress = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READ_WRITE);
        private MemMappedBuffer m_CartAddress = new MemMappedBuffer(4);
        private MemMappedBuffer m_ReadLen = new MemMappedBuffer(4);
        private MemMappedBuffer m_WriteLen = new MemMappedBuffer(4);
        private MemMappedBuffer m_Status = new MemMappedBuffer(4, MemMappedBuffer.MemModel.DUAL_READ_WRITE);
        private MemMappedBuffer m_Dom1Lat = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom1Pwd = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom1Pgs = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom1Rls = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom2Lat = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom2Pwd = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom2Pgs = new MemMappedBuffer(4);
        private MemMappedBuffer m_Dom2Rls = new MemMappedBuffer(4);

        public PIMemory(N64MemoryController controller) : base(controller, 0x100000)
        {
            m_ReadLen.CPUWrite += ReadLengthWrite;
            m_WriteLen.CPUWrite += WriteLengthWrite;

            AppendDevice(
                m_DramAddress, m_CartAddress, m_ReadLen, m_WriteLen, 
                m_Status, 
                m_Dom1Lat, m_Dom1Pwd, m_Dom1Pgs, m_Dom1Rls,
                m_Dom2Lat, m_Dom2Pwd, m_Dom2Pgs, m_Dom2Rls);
        }

        private void WriteLengthWrite()
        {
            SourceAddress = m_CartAddress.ReadPtr.AsType_32Swp();
            DestAddress = m_DramAddress.ReadPtr.AsType_32Swp();
            int size = (int)m_WriteLen.ReadPtr.AsType_32Swp() + 1;

            Log.Debug("PI DMA Start Write: {0:X8} to {1:X8} size={2:X8}", SourceAddress, DestAddress, size);

            TransferBytesAsync(size);

            Log.Debug("PI DMA Finish");
        }

        private void ReadLengthWrite()
        {
            SourceAddress = m_DramAddress.ReadPtr.AsType_32Swp();
            DestAddress = m_CartAddress.ReadPtr.AsType_32Swp();
            int size = (int)m_WriteLen.ReadPtr.AsType_32Swp() + 1;

            Log.Debug("PI DMA Start Read: {0:X8} to {1:X8} size={2:X8}", SourceAddress, DestAddress, size);

            TransferBytesAsync(size);

            Log.Debug("PI DMA Finish");
        }
    }
}
