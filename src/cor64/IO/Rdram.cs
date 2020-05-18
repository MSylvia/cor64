﻿using cor64.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace cor64.IO
{
    public class Rdram : BlockDevice
    {
        const int MAXSIZE = 8 * 1024 * 1024;

        private PinnedBuffer m_Ram = new PinnedBuffer(MAXSIZE);
        private PinnedBuffer m_DummyRead = new PinnedBuffer(4);
        private PinnedBuffer m_DummyWrite = new PinnedBuffer(4);

        public override long Size => 0x03EFFFFF + 1;

        public IntPtr GetRamPointer(int offset)
        {
            return m_Ram.GetPointer().Offset(offset);
        }

        public sealed override void Read(long position, byte[] buffer, int offset, int count)
        {
            int memOffset = (int)position;
            var ptr = m_Ram.GetPointer().Offset(memOffset);

            // XXX: This check ensures RDRAM boundary correctly functions
            if (position >= MAXSIZE)
            {
                return;
            }

            Marshal.Copy(ptr, buffer, offset, count);
        }

        public sealed override void Write(long position, byte[] buffer, int offset, int count)
        {
            int memOffset = (int)position;
            var ptr = m_Ram.GetPointer().Offset(memOffset);

            // XXX: This check ensures RDRAM boundary correctly functions
            if (position >= MAXSIZE)
            {
                return;
            }

            Marshal.Copy(buffer, offset, ptr, count);
        }

        public override IntPtr[] GetReadPointerMap()
        {
            var map = new IntPtr[Size / 4];

            for (int i = 0; i < map.Length; i++)
            {
                int pos = i * 4;

                if (pos < MAXSIZE)
                {
                    map[i] = IntPtr.Add(m_Ram.GetPointer(), pos);
                }
                else
                {
                    map[i] = m_DummyRead.GetPointer();
                }
            }

            return map;
        }

        public override IntPtr[] GetWritePointerMap()
        {
            var map = new IntPtr[Size / 4];

            for (int i = 0; i < map.Length; i++)
            {
                int pos = i * 4;

                if (pos < MAXSIZE)
                {
                    map[i] = IntPtr.Add(m_Ram.GetPointer(), pos);
                }
                else
                {
                    map[i] = m_DummyWrite.GetPointer();
                }
            }

            return map;
        }
    }
}
