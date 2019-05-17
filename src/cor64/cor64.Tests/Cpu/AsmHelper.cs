﻿using cor64.Mips;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace cor64.Tests.Cpu
{
    public static class AsmHelper
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static Stream Assemble(params String[] asm)
        {
            N64Assembler assembler = new N64Assembler();

            /* Assembly Source */
            var source = new AssemblyTextSource("main");
            source += "arch n64.cpu";
            source += "endian msb";

            StringBuilder line = new StringBuilder();

            for (int i = 0; i < asm.Length; i++)
            {
                line.Append(asm[i]);
                if (i + 1 < asm.Length) line.Append(" ");
            }

            source += line.ToString();

            /* Assemble into bytes */
            assembler.AddAssemblySource(source);
            assembler.AssembleCode(true);

            /* Get the output */
            var streamOut = assembler.Output;
            streamOut.Position = 0;

            return streamOut;
        }

        private static String FpuAssemble(TestCase test)
        {
            StringBuilder sb = new StringBuilder();

            if (test.SourceB.Key >= 0)
                sb.AppendFormat("{0} {1},{2},{3}\n", test.Opcode, test.Result.Key, test.SourceA.Key, test.SourceB.Key);
            else
                sb.AppendFormat("{0} {1},{2}\n", test.Opcode, test.Result.Key, test.SourceA.Key);

            return sb.ToString();
        }

        public static void AssembleMipsTestCase(TestCase testCase, bool toggleBreakpoint)
        {
            Assert.False(!testCase.Test32 && !testCase.Test64);

            Stream rom = null;

            String layout = "{0} {1}{2}{3}";

            String asm = "";

            if (testCase.IsFpuTest)
            {
                asm = FpuAssemble(testCase);
            }
            else if (testCase.IsJump)
            {
                asm = testCase.Opcode;
                asm += " ";

                if (testCase.JumpOffset != null)
                {
                    asm += testCase.JumpOffset.Value;
                }
                else
                {
                    asm += testCase.SourceA.Key;
                }
            }
            else if (testCase.IsBranch)
            {
                /* All branch tests are simple, they always target address (word-aligned) */

                /* The assembler auto divides the offset operand by 4 */

                asm = String.Format(
                    layout,
                    testCase.Opcode,
                    testCase.SourceA.Key + ",",
                    testCase.SourceB.Key >= 0 ? (testCase.SourceB.Key + ",") : "",
                    "4");
            }
            else if (testCase.IsLoadStore)
            {
                layout = "{0} {1},{3}({2})";

                asm = String.Format(
                    layout,
                    testCase.Opcode,
                    testCase.SourceA.Key,
                    testCase.SourceB.Key,
                    (ushort)testCase.SourceC.Value
                    );
            }
            else if (!testCase.IsXfer)
            {
                /* Used by most instructions */

                StringBuilder asmBuilder = new StringBuilder();
                bool chainStart = false;

                asmBuilder.Append(testCase.Opcode);
                asmBuilder.Append(" ");

                if (testCase.Result.Key >= 0 && !testCase.NoDestinationReg)
                {
                    asmBuilder.Append(testCase.Result.Key.ToString());
                    chainStart = true;
                }

                if (testCase.SourceA.Key >= 0)
                {
                    if (chainStart)
                    {
                        asmBuilder.Append(",");
                    }

                    if (testCase.IsImmediate && testCase.SourceB.Key < 0)
                    {
                        asmBuilder.Append((ushort)testCase.SourceA.Value);
                    }
                    else
                    {
                        asmBuilder.Append(testCase.SourceA.Key);
                    }

                    chainStart = true;
                }

                if (testCase.SourceB.Key >= 0)
                {
                    if (chainStart)
                    {
                        asmBuilder.Append(",");
                    }

                    if (testCase.IsImmediate || testCase.IsShift)
                    {
                        asmBuilder.Append((ushort)testCase.SourceB.Value);
                    }
                    else
                    {
                        asmBuilder.Append(testCase.SourceB.Key);
                    }
                }

                asm = asmBuilder.ToString();
            }
            else
            {
                String operandA = "";
                String sep = "";
                String operandB = "";

                switch (testCase.XferSource)
                {
                    case RegBoundType.Gpr: operandA = testCase.SourceA.Key.ToString(); break;
                    case RegBoundType.Cp0: operandA = testCase.SourceCp0.Key.ToString(); break;
                    default: break;
                }

                switch (testCase.XferTarget)
                {
                    case RegBoundType.Gpr: operandB = testCase.Result.Key.ToString(); break;
                    case RegBoundType.Cp0: operandB = testCase.SourceCp0.Key.ToString(); break;
                    default: break;
                }

                if (operandA.Length > 0 && operandB.Length > 0)
                {
                    sep = ",";
                }

                asm = testCase.Opcode + " " + operandA + sep + operandB;
            }


            Log.Info("Test Asm: " + asm);

            rom = Assemble(asm);

            testCase.SetProgram(rom);
        }
    }
}
