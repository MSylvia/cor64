using System.IO;
using System;
using System.IO;
using cor64.RCP;
using NLog;

using CMD = cor64.Rdp.RdpCommandTypes;
using static cor64.RCP.DPCInterface;
using cor64.IO;

// TODO: RDP Command interface with DMA engine

namespace cor64.Rdp {
    public abstract class DrawProcessor {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private DPCInterface m_Interface;
        private Stream m_Memory;
        private DisplayListReader m_DpReader;
        private bool m_DebugDL;

        protected readonly RdpCommandTypeFactory.CallTable CallTable = RdpCommandTypeFactory.CreateCallTable();
        
        public abstract String Description { get; }

        protected DrawProcessor() {
            CallTable
            .Map(LoadBlock, CMD.LoadBlock)
            .Map(LoadTile, CMD.LoadTile)
            .Map(LoadTlut, CMD.LoadTLUT)
            .Map(Rectangle, CMD.FillRect, CMD.TexRect, CMD.TexRectFlip)
            .Map(SetImage, CMD.SetColorImage, CMD.SetMaskImage, CMD.SetTexImage)
            .Map(SetColor, CMD.SetBlendColor, CMD.SetEnvColor, CMD.SetPrimColor, CMD.SetFogColor, CMD.SetFillColor)
            .Map(Triangle, CMD.TriNoShade, CMD.TriZNoShade, CMD.TriTex, CMD.TriZTex, CMD.TriShade, CMD.TriZShade, CMD.TriTexShade, CMD.TriZTexShade)
            .Map(SetKey, CMD.SetKeyRed, CMD.SetKeyGB)
            .Map(SetTileSize, CMD.SetTileSize)
            .Map(SetTile, CMD.SetTile)
            .Map(SetCombineMode, CMD.SetCombine)
            .Map(SetOtherModes, CMD.SetOtherModes)
            .Map(SetPrimDepth, CMD.SetPrimDepth)
            .Map(SetSync, CMD.SyncFull, CMD.SyncLoad, CMD.SyncPipe, CMD.SyncTile)
            .Map(SetScissor, CMD.SetScissor)
            .Map(SetConvert, CMD.SetConvert)
            .Finish();
        }

        public void Step() {
            
        }

        public virtual void Init() {
            
        }

        public void AttachInterface(DPCInterface iface) {
            m_Interface = iface;
            m_Interface.DisplayListReady += DisplayListHandler;
        }

        public virtual void AttachMemory(N64MemoryController.N64MemoryStream stream) {
            m_Memory = stream;
            m_DpReader = new DisplayListReader(m_Memory);
        }

        protected virtual void DisplayListHandler(object sender, DPCInterface.DisplayList displayList) {
            m_Interface.RFlags |= ReadStatusFlags.CmdBusy;

            int size = (int)(displayList.End - displayList.Start);

            if (size < 0) {
                throw new InvalidOperationException("Invalid display address range");
            }

            //Log.Debug("Display List: {0:X8}, Size {1}", displayList.Start, size);

            // Print out the display list bytes
            // m_IMemory.Position = displayList.Start;
            // Console.Write("DP: ");
            // for (int i = 0; i < size; i++)  {
            //     Console.Write("{0:X2}", m_IMemory.ReadByte());
            // }
            // Console.WriteLine();

            /* Now read memory of the display list */
            var commands = m_DpReader.ReadDisplayList(displayList.Start, size, m_Interface.UseXBus);

            for (int i = 0; i < commands.Count; i++) {
                var command = commands[i];

                // if (i == 18) {
                //     break;
                // }

                // if (command.ResolveType().Type.Id == RdpCommandTypes.TriTex.Id) {
                //     if (i != 35 /*&& i != 36*/) {
                //         continue;
                //     }
                // }

                // string cmdLabel = command.Type.Name.ToLower();
                // cmdLabel += "_";
                // cmdLabel += i.ToString();
                // command.PrintCommandCArray(cmdLabel);

                if (m_DebugDL) {
                    Log.Debug("DP Command: {0}", command.ResolveType().ToString());
                }

                var handler = CallTable[command];

                if (handler != null)
                    handler?.Invoke(command);
                else
                    throw new InvalidOperationException("RDP command handler is null");
            }

            m_Interface.RFlags &= ~ReadStatusFlags.CmdBusy;
        }

        public void SetDLDebug(bool enable) {
            m_DebugDL = enable;
        }

        protected abstract void LoadBlock(RdpCommand command);

        protected abstract void LoadTile(RdpCommand command);

        protected abstract void LoadTlut(RdpCommand command);

        protected abstract void Rectangle(RdpCommand command);

        protected abstract void SetCombineMode(RdpCommand command);

        protected abstract void SetConvert(RdpCommand command);

        protected abstract void SetKey(RdpCommand command);

        protected abstract void SetOtherModes(RdpCommand command);

        protected abstract void SetColor(RdpCommand command);

        protected abstract void SetPrimDepth(RdpCommand command);

        protected abstract void SetScissor(RdpCommand command);

        protected abstract void SetTile(RdpCommand command);

        protected abstract void SetTileSize(RdpCommand command);

        protected abstract void SetImage(RdpCommand command);

        protected abstract void SetSync(RdpCommand command);

        protected abstract void Triangle(RdpCommand command);
    }
}