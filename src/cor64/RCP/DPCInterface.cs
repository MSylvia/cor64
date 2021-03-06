using cor64.IO;
using cor64.Debugging;
using System;
using cor64.Rdp;

namespace cor64.RCP {
/*
 0x0410 0000 to 0x041F FFFF  DP command registers:
-------------------------------------------------
    DPC_BASE_REG - 0x04100000

    0x0410 0000 to 0x0410 0003  DPC_START_REG
             DP CMD DMA start
       (RW): [23:0] DMEM/RDRAM start address
    0x0410 0004 to 0x0410 0007  DPC_END_REG
             DP CMD DMA end
       (RW): [23:0] DMEM/RDRAM end address
    0x0410 0008 to 0x0410 000B  DPC_CURRENT_REG
             DP CMD DMA end
        (R): [23:0] DMEM/RDRAM current address
    0x0410 000C to 0x0410 000F  DPC_STATUS_REG
             DP CMD status
        (W): [0] clear xbus_dmem_dma  (R): [0]  xbus_dmem_dma
             [1] set xbus_dmem_dma         [1]  freeze
             [2] clear freeze              [2]  flush
             [3] set freeze                [3]  start gclk
             [4] clear flush               [4]  tmem busy
             [5] set flush                 [5]  pipe busy
             [6] clear tmem ctr            [6]  cmd busy
             [7] clear pipe ctr            [7]  cbuf ready
             [8] clear cmd ctr             [8]  dma busy
             [9] clear clock ctr           [9]  end valid
                                           [10] start valid
    0x0410 0010 to 0x0410 0013  DPC_CLOCK_REG
             DP clock counter
        (R): [23:0] clock counter
    0x0410 0014 to 0x0410 0017  DPC_BUFBUSY_REG
             DP buffer busy counter
        (R): [23:0] clock counter
    0x0410 0018 to 0x0410 001B  DPC_PIPEBUSY_REG
             DP pipe busy counter
        (R): [23:0] clock counter
    0x0410 001C to 0x0410 001F  DPC_TMEM_REG
             DP TMEM load counter
        (R): [23:0] clock counter
    0x0410 0020 to 0x041F FFFF  Unused
*/
    public class DPCInterface : PerpherialDevice {
        [Flags]
        public enum WriteStatusFlags : uint {
            ClearXbusDmemDma = 1,
            SetXbusDmemDma = 0b10,
            ClearFreeze = 0b100,
            SetFreeze   = 0b1000,
            ClearFlush  = 0b10000,
            SetFlush    = 0b100000,
            ClearTmemCtr= 0b1000000,
            ClearPipeCtr= 0b10000000,
            ClearCmdCtr = 0b100000000,
            ClearClockCtr=0b1000000000
        }

        [Flags]
        public enum ReadStatusFlags : uint {
            XbusDmemDma = 1,
            Freeze = 0b10,
            Flush =   0b100,
            StartGClk=0b1000,
            TmemBusy= 0b10000,
            PipeBusy= 0b100000,
            CmdBusy = 0b1000000,
            ColorBufferReady = 0b10000000,
            DmaBusy = 0b100000000,
            EndValid=  0b1000000000,
            Startvalid=0b10000000000
        }

        private readonly MemMappedBuffer m_Start = new MemMappedBuffer();
        private readonly MemMappedBuffer m_End = new MemMappedBuffer();
        private readonly MemMappedBuffer m_Current = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private readonly MemMappedBuffer m_Status = new MemMappedBuffer(4,  MemMappedBuffer.MemModel.DUAL_READ_WRITE);
        private readonly MemMappedBuffer m_Clock = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private readonly MemMappedBuffer m_BufferBusyCounter = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private readonly MemMappedBuffer m_PipeBusyCounter = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private readonly MemMappedBuffer m_TmemLoadCounter = new MemMappedBuffer(4, MemMappedBuffer.MemModel.SINGLE_READONLY);
        private readonly DrawProcessor m_Rdp;

        private readonly MemMappedBuffer[] m_RegSelects;

        public event EventHandler<DisplayList> DisplayListReady;

        public struct DisplayList {
            internal DisplayList(uint start, uint end) {
                Start = start;
                End = end;
            }

            public uint Start { get; }
            public uint End { get; }
        }

        public DPCInterface(N64MemoryController controller) : base (controller, 0x100000) {
            Map(m_Start, m_End, m_Current);
            Map(m_Status);
            Map(m_Clock, m_BufferBusyCounter, m_PipeBusyCounter, m_TmemLoadCounter);

            // Init some status flags
            RFlags |= ReadStatusFlags.ColorBufferReady;

            m_Start.Write += DisplayListStart;
            m_End.Write += DisplayListEnd;
            m_Start.Write += StatusWrite;

            m_RegSelects = new MemMappedBuffer[] {
                m_Start,
                m_End,
                m_Current,
                m_Status,
                m_Clock,
                m_BufferBusyCounter,
                m_PipeBusyCounter,
                m_TmemLoadCounter
            };
        }

        public void RegWriteFromRsp(int select, uint value) {
            m_RegSelects[select].RegisterValue = value;
            m_RegSelects[select].WriteNotify();
        }

        private void DisplayListStart() {
            //Console.WriteLine("Display List: {0:X8}", m_Start.ReadonlyRegisterValue);
        }

        private void DisplayListEnd() {
            //Console.WriteLine("Display List: {0:X8}", m_End.ReadonlyRegisterValue);

            var start = m_Start.RegisterValue & 0x0FFFFFFFU;
            var end = m_End.RegisterValue & 0x0FFFFFFFU;

            DisplayListReady?.Invoke(this, new DisplayList(start, end));

            CmdReadFinish();
        }

        public void DirectDLSetup(uint address, int size) {
            m_Start.RegisterValue = address;
            m_End.RegisterValue = (uint)(address + size);
        }

        public void DirectDLExecute() {
            DisplayListEnd();
        }

        private void StatusWrite() {
            var status = m_Status.RegisterValue;
            var flags = (WriteStatusFlags)status;

            if ((flags & WriteStatusFlags.SetXbusDmemDma) == WriteStatusFlags.SetXbusDmemDma) {
                RFlags |= ReadStatusFlags.XbusDmemDma;
            }

            if ((flags & WriteStatusFlags.ClearXbusDmemDma) == WriteStatusFlags.ClearXbusDmemDma) {
                RFlags &= ~ReadStatusFlags.XbusDmemDma;
            }


            m_Status.RegisterValue = 0;
        }

        private void CmdReadFinish() {
            m_End.RegisterValue &= 0x0FFFFFFFU;
            m_Start.RegisterValue &= 0x0FFFFFFFU;
            m_Current.RegisterValue = m_End.RegisterValue;
        }

        public WriteStatusFlags WFlags => (WriteStatusFlags)m_Status.RegisterValue;

        public ReadStatusFlags RFlags {
            get => (ReadStatusFlags)m_Status.ReadonlyRegisterValue;
            set => m_Status.ReadonlyRegisterValue = (uint)value;
        }

        public bool UseXBus => (RFlags & ReadStatusFlags.XbusDmemDma) == ReadStatusFlags.XbusDmemDma;
    }

}