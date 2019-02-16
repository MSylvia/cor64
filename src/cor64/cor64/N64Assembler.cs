﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cor64.BassSharp;
using cor64.BassSharp.Table;
using cor64.IO;
using NLog;

/* Notes:
 * Setting little-endian or big-endian mode will not affect
 * the actual byte ordering, this is due to the MIPS tables
 * enforcing their own order of bytes reguardless of the 
 * endianess mode set in the assembler
 */

namespace cor64
{
    public class N64Assembler : BassTable
    {
        private StreamTarget m_Target = new StreamTarget();
        private Dictionary<String, AssemblyTextSource> m_Sources = new Dictionary<string, AssemblyTextSource>();
        private static Logger Log = LogManager.GetCurrentClassLogger();

        public IDictionary<String, AssemblyTextSource> AssemblySources => m_Sources;
        public Stream AssembledStream => m_Target.Output;

        public void AddAssemblySource(AssemblyTextSource source)
        {
            m_Sources.Add(source.Name, source);
            Log.Debug("Added source: " + source.Name);
        }

        private sealed class StreamTarget : ITarget
        {
            private MemoryStream m_OutputStream = new MemoryStream();

            public Stream Output => m_OutputStream;

            public void Close()
            {
                m_OutputStream.Close();
            }

            public void Seek(long offset)
            {
                m_OutputStream.Position = offset;
            }

            public void WriteBE(ulong data, int len)
            {
                var shift = 8 * len;

                while (len > 0)
                {
                    len--;
                    shift -= 8;
                    m_OutputStream.WriteByte((byte)(data >> shift));
                }

            }

            public void WriteLE(ulong data, int len)
            {
                while (len > 0)
                {
                    len--;
                    m_OutputStream.WriteByte((byte)data);
                    data >>= 8;
                }
            }

            public Stream GetStream()
            {
                return m_OutputStream;
            }
        }

        public N64Assembler() : base() {
            SetTarget(m_Target);
            PreappendSources();
        }

        protected override ISource RequestStreamSource(string name)
        {
            return m_Sources[name];
        }

        protected override void Assemble(bool strict = false)
        {
            var sources = m_Sources.Values.ToList();

            foreach (var src in sources) {
                if (!base.Source(src))
                    throw new InvalidOperationException("Failed to add source: " + src.Name);
            }

            base.Assemble(strict);
        }

        public void AssembleCode(bool strict = false)
        {
            Assemble(strict);
        }

        protected virtual void PreappendSources()
        {

        }

        protected AssemblyTextSource LoadSourceFromManifest(String name, Type type)
        {
            var r = ReadStringResource(type, name);

            if (r != null) {
                var src = new AssemblyTextSource(name);
                src += r;
                return src;
            }
            else {
                throw new IOException("Cannot find source " + name);
            }
        }

        protected override ISource RequestBinarySource(string name)
        {
            throw new NotImplementedException();
        }
    }
}
