﻿using cor64.IO;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace cor64.Mips
{
    public class DataMemory : IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private Stream m_DataStream;
        private byte[] m_DataBuffer;
        private GCHandle m_BufferHandle;
        private IntPtr m_BufferPtr;
        public long LastAddress { get; private set; }

        /* Configured memory accessors */
        private Func<ushort> m_MemRead16;
        private Action<ushort> m_MemWrite16;
        private Func<uint> m_MemRead32;
        private Action<uint> m_MemWrite32;
        private Func<ulong> m_MemRead64;
        private Action<ulong> m_MemWrite64;

        public DataMemory(Stream stream)
        {
            m_DataStream = stream;
            m_DataBuffer = new byte[8];
            m_BufferHandle = GCHandle.Alloc(m_DataBuffer, GCHandleType.Pinned);
            m_BufferPtr = m_BufferHandle.AddrOfPinnedObject();

            if (CoreConfig.Current.ByteSwap) {
                m_MemRead16 = () => m_BufferPtr.AsType_16Swp();
                m_MemRead32 = () => m_BufferPtr.AsType_32Swp();
                m_MemRead64 = () => m_BufferPtr.AsType_64Swp();
                m_MemWrite16 = (x) => m_BufferPtr.AsType_16Swp(x);
                m_MemWrite32 = (x) => m_BufferPtr.AsType_32Swp(x);
                m_MemWrite64 = (x) => m_BufferPtr.AsType_64Swp(x);
            }
            else {
                m_MemRead16 = () => m_BufferPtr.AsType_16();
                m_MemRead32 = () => m_BufferPtr.AsType_32();
                m_MemRead64 = () => m_BufferPtr.AsType_64();
                m_MemWrite16 = (x) => m_BufferPtr.AsType_16(x);
                m_MemWrite32 = (x) => m_BufferPtr.AsType_32(x);
                m_MemWrite64 = (x) => m_BufferPtr.AsType_64(x);
            }
        }

        public Stream BaseStream => m_DataStream;

        public void ReadData(long address, int size)
        {
            // Clear buffer
            Data64 = 0;

            try
            {
                /* 32-bit Constraint */
                address = (uint)address;
                LastAddress = address;

                if (size > m_DataBuffer.Length)
                {
                    throw new InvalidDataException("read size can't be bigger than internal data buffer");
                }

                m_DataStream.Position = address;
                m_DataStream.Read(m_DataBuffer, 0, size);

                //Console.WriteLine("Read {0:X16} from {1:X8}", Data64, address);
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
                throw new EmuException("ReadData: Hit an exception for " + address.ToString("X8"), e);
            }
        }

        public void WriteData(long address, int size)
        {
            try
            {
                address = (uint)address;
                LastAddress = address;

                if (size > m_DataBuffer.Length)
                {
                    throw new InvalidDataException("write size can't be bigger than internal data buffer");
                }

                //Console.WriteLine("Write {0:X16} to {1:X8}", Data64, address);

                m_DataStream.Position = address;
                m_DataStream.Write(m_DataBuffer, 0, size);
            }
            catch (Exception e)
            {
                throw new EmuException("WriteData: Hit an exception for " + address.ToString("X8"), e);
            }
        }

        public byte Data8
        {
            get => m_BufferPtr.AsType_8();
            set => m_BufferPtr.AsType_8(value);
        }

        public ushort Data16 {
            get => m_MemRead16();
            set => m_MemWrite16(value);
        }

        public uint Data32 {
            get => m_MemRead32();
            set => m_MemWrite32(value);
        }

        public ulong Data64 {
            get => m_MemRead64();
            set => m_MemWrite64(value);
        }

        public UInt128 ReadData128(long address) {
            ulong a;
            ulong b;
            
            ReadData(address, 8);
            a = Data64;

            ReadData(address + 8, 8);
            b = Data64;

            if (CoreConfig.Current.ByteSwap) {
                return new UInt128() {
                    lo = b,
                    hi = a
                };
            }
            else {
                return new UInt128() {
                    lo = a,
                    hi = b
                };
            }
        }

        public void WriteData128(long address, UInt128 value) {
            ulong a;
            ulong b;

            if (CoreConfig.Current.ByteSwap) {
                a = value.hi;
                b = value.lo;
            }
            else {
                a = value.lo;
                b = value.hi;
            }

            Data64 = a;
            WriteData(address, 8);

            Data64 = b;
            WriteData(address + 8, 8);
        }

        public byte[] ReadBuffer()
        {
            byte[] buffer = new byte[m_DataBuffer.Length];
            Array.Copy(m_DataBuffer, buffer, buffer.Length);
            return buffer;
        }
        

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    m_DataStream.Dispose();
                }

                m_BufferPtr = IntPtr.Zero;
                m_BufferHandle.Free();

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~Memory() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
