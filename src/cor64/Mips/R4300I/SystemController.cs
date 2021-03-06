using System.Runtime.ConstrainedExecution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace cor64.Mips.R4300I
{
    /// <summary>
    /// This class simulates functions of the coprocessor 0 in high level emulation
    /// </summary>
    public class SystemController
    {
        private readonly static Logger Log = LogManager.GetCurrentClassLogger();
        private ExecutionState m_State;
        private bool m_TLBRefillException;
        private bool m_XTLBRefillExcepetion;
        private bool m_CacheErr;
        private bool m_NMI;
        private MipsInterface m_Interface;
        private readonly Clock m_Clock;
        private bool m_SoftReset;
        private bool m_RcpHandlerTaken = false;
        private bool m_Direct;

        const int INTERRUPT_SW0 = 0;  // An interrupt set by user
        const int INTERRUPT_SW1 = 1;  // An interrupt set by user
        const int INTERRUPT_RCP = 2;  // RCP Hardware Events
        const int INTERRUPT_CART = 3; // Cartidge Events
        const int INTERRUPT_RESET = 4; // Reset Button
        const int INTERRUPT_RDB_READ = 5; // Debugger Read
        const int INTERRUPT_RDB_WRITE = 6; // Debugger Write
        const int INTERRUPT_TIMER = 7; // MIPS Timer


        protected CauseRegister CR => m_State.Cp0.Cause;
        protected StatusRegister SR => m_State.Cp0.Status;
        protected ControlRegisters REGS => m_State.Cp0;

        public ulong ExceptionHandlerAddress { get; private set; }

        public SystemController(ExecutionState coreState, Clock clock)
        {
            m_State = coreState;
            m_Clock = clock;
        }

        public void AttachInterface(MipsInterface mipsInterface)
        {
            m_Interface = mipsInterface;
        }

        public void SetTLBRefillException()
        {
            m_TLBRefillException = true;
        }

        public void SetXTLBRefillException()
        {
            m_TLBRefillException = true;
        }

        public void SetCacheError()
        {
            m_CacheErr = true;
        }

        public void SetSoftReset()
        {
            m_SoftReset = true;
        }

        public void UseDirectAddresses() {
            m_Direct = true;
        }

        public void SetNonMaskableInterrupt()
        {
            m_NMI = true;
        }

        /// <summary>
        /// Called to simulate a processor tick for coprocessor 0
        /// </summary>
        public void ProcessorTick(ulong pc, bool isDelaySlot)
        {
            /* Increment count register */
            // TODO: Allow some clock emulator to suggest the increment amount instead
            MipsTimerTick(1);

            /* Process all system events */
            CheckInterrupts(pc, isDelaySlot);
        }

        public void MipsTimerTick(ulong count)
        {
            #if HACK_FASTER_TIMER
            count += 1;
            #endif

            REGS.Write(CTS.CP0_REG_COUNT, REGS.Read(CTS.CP0_REG_COUNT) + count);
        }

        public uint TimerCount => (uint)REGS.Read(CTS.CP0_REG_COUNT);

        public uint TimerMax => (uint)REGS.Read(CTS.CP0_REG_COMPARE);

        private void CheckInterrupts(ulong pc, bool isDelaySlot)
        {
            // XXX: It seems we should always check for pending RCP updates
            //      even when interrupts have been disabled

            /* RCP Hardware Interrupt */
            if (m_Interface != null) {
                if ((m_Interface.Interrupt & m_Interface.Mask) != 0)
                {
                    #if !FILTER_RCP_INTERRUPTS
                    CR.SetInterruptPending(INTERRUPT_RCP);
                    #endif
                }
                else {
                    #if !FILTER_RCP_INTERRUPTS
                    CR.ClearPendingInterrupt(INTERRUPT_RCP);
                    #endif
                }
            }

            bool timerPending = false;

            /* Timer Interrupt */
            if ( REGS.RegRead(CTS.CP0_REG_COMPARE) != 0) {
                if (REGS.RegRead(CTS.CP0_REG_COUNT) >= REGS.RegRead(CTS.CP0_REG_COMPARE))
                {
                    REGS.RegWrite(CTS.CP0_REG_COUNT, 0);
                    timerPending = true;
                }
            }


            if (!SR.InterruptsEnabled) {
                return;
            }

            if (SR.ErrorLevel) {
                return;
            }

            if (SR.ExceptionLevel) {
                return;
            }

            uint target;
            uint cacheTarget;
            bool executeHandler;
            bool error = false;

            if (SR.TestFlags(StatusRegister.StatusFlags.UseBootstrapVectors))
            {
                cacheTarget = 0xBFC00200;
                target = 0xBFC00200;
            }
            else
            {
                cacheTarget = 0xA0000000;
                target = 0x80000000;
            }

            /* Non-maskable MIPS interrupt, this cannot be ignored */
            /* This forces the MIPS to do a reset */
            if (m_NMI)
            {
                target = 0xBFC00000;
                m_NMI = false;
                error = true;
            }

            // If this has been set manually, don't use any base address
            if (m_Direct) {
                target = 0;
                cacheTarget = 0;
            }

            /* 32-bit TLB Exception */
            if (m_TLBRefillException)
            {
                m_TLBRefillException = false;
                executeHandler = true;
            }

            /* 64-bit TLB Exception */
            else if (m_XTLBRefillExcepetion)
            {
                target += 0x080;
                m_XTLBRefillExcepetion = false;
                executeHandler = true;
            }

            /* CPU Cache Exception */
            else if (m_CacheErr)
            {
                target += 0x100;
                m_CacheErr = false;
                error = true;
                executeHandler = true;
            }

            /* CPU Instruction Exception */
            else if (CR.ExceptionThrown)
            {
                CR.ClearThrownException();
                target += 0x180;
                executeHandler = true;
                //error = true;
            }

            /* Interrupts */
            else
            {
                target += 0x180;
                error = false;

                /* MIPS timer has generated an event, set its interrupt */
                if (timerPending) {
                    CR.SetInterruptPending(INTERRUPT_TIMER);

                    timerPending = false;

                    #if DEBUG_MIPS_TIMER
                        Log.Debug("Mips Timer interrupt trigger");
                    #endif
                }

                /* Reset button has been pushed, set its interrupt */
                if (SR.InterruptMask != 0)
                {
                    /* SoftReset */
                    if (m_SoftReset)
                    {
                        m_SoftReset = false;
                        CR.SetInterruptPending(INTERRUPT_RESET);
                        error = true;
                    }
                }

                // This should handle interrupts SW0 and SW1 set by the user
                executeHandler = (CR.InterruptPending & SR.InterruptMask) != 0;
            }

            /* If true, then prepare to jump to the dedicated exception handler */
            if (executeHandler)
            {
                #if DEBUG_INTERRUPTS
                Log.Debug("Execute Interrupt Handler");
                #endif

                InterpreterPendingInterrupts = true;

                if (!error)
                {
                    SR.ExceptionLevel = true;

                    if (isDelaySlot)
                    {
                        REGS.Write(CTS.CP0_REG_EPC, pc - 4);
                        CR.SetBranchDelayBit();
                    }
                    else
                    {
                        REGS.Write(CTS.CP0_REG_EPC, pc);
                        CR.ClearBranchDelayBit();
                    }
                }
                else
                {
                    SR.ErrorLevel = true;

                    if (isDelaySlot)
                    {
                        REGS.Write(CTS.CP0_REG_ERROR_EPC, pc - 4);
                        CR.SetBranchDelayBit();
                    }
                    else
                    {
                        REGS.Write(CTS.CP0_REG_ERROR_EPC, pc);
                        CR.ClearBranchDelayBit();
                    }
                }

                #if DEBUG_INTERRUPTS
                Log.Debug("PC to return {0:X8} after servicing interrupt", REGS.RegRead(error ? CTS.CP0_REG_ERROR_EPC : CTS.CP0_REG_EPC));
                #endif

                ExceptionHandlerAddress = target;
            }
        }

        public bool IsKernelMode => SR.ModeBits == 0;

        public bool IsSupervisorMode => SR.ModeBits == 1;

        public bool IsUserMode => SR.ModeBits == 2;

        /// <summary>
        /// This should be only set when the system controller detects interrupts waiting to be serviced
        /// </summary>
        public bool InterpreterPendingInterrupts;
    }
}
