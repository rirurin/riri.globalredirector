using riri.commonmodutils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Iced.Intel;
using Decoder = Iced.Intel.Decoder;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace riri.globalredirector
{
    public class Redirector : ModuleBase<RedirectorContext>
    {
        public ProgramDecoder program { get; private set; }
        //private RedirectorApi _api;
        public unsafe Redirector(RedirectorContext context, Dictionary<string, ModuleBase<RedirectorContext>> modules) : base(context, modules)
        {
            program = new ProgramDecoder(_context._fileName, _context._utils, _context._baseAddress);
        }
        public override void Register()
        {
            //_api = GetModule<RedirectorApi>();
        }
    }
    public class ProgramDecoder
    {
        private static int REQUIRED_ALIGNOF = 0x10;
        public ConcurrentDictionary<string, TargetArea> TargetAreas { get; set; } = new();
        public static nuint BASE_ADDRESS { get; private set; }
        public string ExecutablePath { get; private set; }
        public Dictionary<Slice, uint> VirtualToPhysicalAddress { get; set; } = new();
        public List<(Slice Bounds, string Name)> Sections { get; private set; } = new();
        private Dictionary<uint, (uint rip, uint raw, uint size)> FunctionAddresses { get; set; } = new();
        public HashSet<uint> DecodedSubroutines { get; set; } = new();
        public string LastError { get; private set; }

        private Slice? LastObtainedSection = null;
        private uint LastObtainedRawPosition = 0;
        public int SizeOfCode { get; private set; } = 0;

        private int? SizeOfGlobalNameColumn = null;

        private Utils _utils;
        public ProgramDecoder(string path, Utils utils, long baseAddress)
        {
            ExecutablePath = path;
            _utils = utils;
            BASE_ADDRESS = (nuint)baseAddress;
        }
        public uint GetRealAddress(uint offset)
        {
            if (LastObtainedSection != null && offset - LastObtainedSection.Value.Start < LastObtainedSection.Value.Length)
                return LastObtainedRawPosition + offset - LastObtainedSection.Value.Start;
            for (int i = 0; i < Sections.Count; i++)
            {
                if (offset < Sections[i].Bounds.Start + Sections[i].Bounds.Length
                    && VirtualToPhysicalAddress.TryGetValue(Sections[i].Bounds, out var realStart))
                {
                    LastObtainedSection = Sections[i].Bounds;
                    LastObtainedRawPosition = realStart;
                    return realStart + offset - Sections[i].Bounds.Start;
                }
            }
            return 0;
        }
        public int GetSection(uint offset)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (offset < Sections[i].Bounds.Start + Sections[i].Bounds.Length
                    && VirtualToPhysicalAddress.TryGetValue(Sections[i].Bounds, out var realStart))
                    return i;
            }
            return 0;
        }

        public int GetSizeOfGlobalNameColumn()
        {
            if (SizeOfGlobalNameColumn == null)
            {
                var maxColLen = 0;
                foreach (var targetArea in TargetAreas)
                    if (targetArea.Key.Length > maxColLen) maxColLen = targetArea.Key.Length;
                SizeOfGlobalNameColumn = maxColLen + 2;
            }
            return SizeOfGlobalNameColumn.Value;
        }

        public static VAGetRegisterValue DefaultGetRegister = (register, elementIndex, elementSize) => 0;

        // Represents a particular function (a subroutine called with the CALL instruction)
        public class ChildFunction
        {
            // Branches within the function
            public HashSet<uint> InnerBranches;

            private Decoder _decoder;
            private BinaryReader _stream;
            private ProgramDecoder _parent;

            private bool panicSwitch = false;
            private static int PANIC_THRESHOLD = 8;
            private string FunctionName;

            private Utils _utils;
            public ChildFunction(Decoder decoder, BinaryReader stream, ProgramDecoder parent, uint rip, Utils utils, string? functionName = null)
            {
                _decoder = decoder;
                _stream = stream;
                _parent = parent;
                FunctionName = (functionName != null) ? functionName : $"0x{BASE_ADDRESS + rip:X}";
                InnerBranches = new() { rip };
                _utils = utils;
            }
            public bool IsNextInstructionReturn(out Instruction instr)
            {
                _decoder.Decode(out instr);
                return instr.Mnemonic == Mnemonic.Ret;
            }
            public void Decode()
            {
                var startFn = InnerBranches.First();
                //_context._utils.Log($"[DECODE FUN] RIP: 0x{BASE_ADDRESS + startFn:X}");
                Decode(startFn, true, 0);
            }

            public bool IsLocalBranch(Instruction instr) =>
                instr.Mnemonic == Mnemonic.Jne
                || instr.Mnemonic == Mnemonic.Je
                || instr.Mnemonic == Mnemonic.Jg
                || instr.Mnemonic == Mnemonic.Jge
                || instr.Mnemonic == Mnemonic.Jl
                || instr.Mnemonic == Mnemonic.Jle
                || instr.Mnemonic == Mnemonic.Ja
                || instr.Mnemonic == Mnemonic.Jae
                || instr.Mnemonic == Mnemonic.Jb
                || instr.Mnemonic == Mnemonic.Jbe
            || instr.Mnemonic == Mnemonic.Jmp
            ;

            public bool CanContinueDecode(Instruction instr)
                => instr.Mnemonic != Mnemonic.Ret && !(instr.Mnemonic == Mnemonic.Jmp && instr.Op0Kind != OpKind.NearBranch64);

            public uint DecodeLocalFunction(uint rip, uint ret, int jumpDepth)
            {
                uint childEarliestStart = uint.MaxValue;
                if (jumpDepth + 1 < PANIC_THRESHOLD && rip < _parent.SizeOfCode)
                {
                    // add to decoded branches before we've done decoding to avoid recursion
                    InnerBranches.Add(rip);
                    childEarliestStart = Decode(rip, false, jumpDepth + 1);
                    _stream.BaseStream.Seek(_parent.GetRealAddress(ret), SeekOrigin.Begin);
                    _decoder.IP = ret;
                }
                else // We're likely trapped inside Denuvo hell, get out of there
                    panicSwitch = true;
                return childEarliestStart;
            }

            // SWITCH STATEMENT PASS
            public unsafe class SwitchStatementJumpTableLimiter
            {
                public Register limitReg { get; set; }
                public int limitNum { get; set; }
                public ulong instructionIP { get; set; }

                public SwitchStatementJumpTableLimiter(Register pLimitReg, int pLimitNum, ulong pInstructionIP)
                {
                    limitReg = pLimitReg;
                    limitNum = pLimitNum;
                    instructionIP = pInstructionIP;
                }
            }

            public bool IsCmpSwitchStatementJumpTableLimit(Instruction instr, out SwitchStatementJumpTableLimiter? jumpTableLimiter)
            {
                var limitReg = instr.Op0Register;
                int limitNum;
                jumpTableLimiter = null;
                switch (instr.Op1Kind)
                {
                    case OpKind.Immediate8:
                        limitNum = instr.Immediate8;
                        break;
                    case OpKind.Immediate8to16:
                        limitNum = instr.Immediate8to16;
                        break;
                    case OpKind.Immediate8to32:
                        limitNum = instr.Immediate8to32;
                        break;
                    default:
                        return false;
                }
                jumpTableLimiter = new SwitchStatementJumpTableLimiter(limitReg, limitNum, instr.IP);
                return true;
            }

            public bool IsAbsoluteLeaForSwitchStatementJump(Instruction instr)
                => instr.Mnemonic == Mnemonic.Lea
                    && instr.Op1Kind == OpKind.Memory
                    && instr.MemoryBase == Register.RIP // not relative to other registers
                    && instr.MemoryDisplacement64 == 0; // base address

            // e.g if limitReg is EAX, check EAX first, then RAX
            public bool MovJumpIndexBasedCheckIndexRegister(Register limitReg, Register targetReg)
            {
                var currReg = limitReg;
                while (currReg < Register.EIP)
                {
                    if (currReg == targetReg)
                        return true;
                    currReg += 0x10;
                }
                return false;
            }

            public SortedList<uint, uint> SwitchStatementPrintOffsets(uint offset, int count, int size)
            {
                var streamReturn = _stream.BaseStream.Position;
                _stream.BaseStream.Seek(_parent.GetRealAddress(offset), SeekOrigin.Begin);
                //uint largest = 0;
                SortedList<uint, uint> jumps = new();
                for (int i = 0; i <= count; i++)
                {
                    uint current;
                    switch (size)
                    {
                        case 1:
                            current = _stream.ReadByte();
                            break;
                        case 4:
                            current = _stream.ReadUInt32();
                            break;
                        default:
                            Console.WriteLine($"[SwitchStatementPrintOffsets] Unknown switch statement offset size {size}");
                            return jumps;
                    }
                    //Console.WriteLine($"{i}: 0x{current:X}");
                    jumps.TryAdd<uint, uint>(current, 0);
                }
                _stream.BaseStream.Seek(streamReturn, SeekOrigin.Begin);
                return jumps;
            }
            public uint Decode(uint rip, bool bCanCheckThunk, int jumpDepth)
            {
                //if (!bCanCheckThunk) _context._utils.Log($"[DECODE JMP] RIP: 0x{BASE_ADDRESS + rip:X}");
                _stream.BaseStream.Seek(_parent.GetRealAddress(rip), SeekOrigin.Begin);
                _decoder.IP = rip;
                Instruction? prevInstr = null;
                Instruction? instr = null;
                var count = 0;
                var childEarliestStart = rip;
                bool bIsThunk = false;
                SortedList<uint, uint> jumps = new();

                // switch statement resolving (identify + read jump tables)
                SwitchStatementJumpTableLimiter? potentialSwitchStatementJumpLimit = null;
                Register? switchStatementImageBase = null;
                uint switchStatementOffset = 0;
                do
                {
                    if (instr != null)
                        prevInstr = instr;
                    instr = _decoder.Decode();
                    count++;
                    uint NearBranch64_32 = (uint)instr.Value.NearBranch64;
                    if (instr.Value.IsCallNear && !_parent.DecodedSubroutines.Contains(NearBranch64_32)) // child function call
                        _parent.DecodeChildFunction(_decoder, _stream, NearBranch64_32, instr.Value.NextIP);
                    else if (IsLocalBranch(instr.Value) && !InnerBranches.Contains(NearBranch64_32))
                    {
                        if (instr.Value.Op0Kind != OpKind.NearBranch64)
                            break;
                        // switch statement pass (cmp + ja)
                        // + lea (base address)
                        // + 
                        if (prevInstr != null && instr.Value.Mnemonic == Mnemonic.Ja
                            && prevInstr.Value.Mnemonic == Mnemonic.Cmp
                            && prevInstr.Value.Op0Kind == OpKind.Register
                            //&& instr.Value.IP == 0xea6b82)
                            //&& instr.Value.IP == 0xea6b82
                            && IsCmpSwitchStatementJumpTableLimit(prevInstr.Value, out potentialSwitchStatementJumpLimit)
                            ) { }
                        // first instruction in functino is a jump, this is a thunk
                        // we know that there won't be any instructions after this, so halt checking
                        if (bCanCheckThunk && count == 1)
                            bIsThunk = true;
                        jumps.TryAdd<uint, uint>(NearBranch64_32, 0);
                    }
                    else
                    {
                        if (potentialSwitchStatementJumpLimit != null)
                        {
                            // Swap limit register if needed (can be before OR after LEA)
                            if (instr.Value.Op0Kind == OpKind.Register && instr.Value.Op1Kind == OpKind.Register
                                && instr.Value.Op1Register == potentialSwitchStatementJumpLimit.limitReg)
                                potentialSwitchStatementJumpLimit.limitReg = instr.Value.Op0Register;

                            // LEA, RX [IMAGE_BASE]
                            if (IsAbsoluteLeaForSwitchStatementJump(instr.Value)
                                && instr.Value.IP - potentialSwitchStatementJumpLimit.instructionIP <= 0x20)
                                switchStatementImageBase = instr.Value.Op0Register;

                            // R1 - image base address
                            // C - some constant, within image range
                            // Single level switch statement:
                            // MOVX R3, [R1 + R2 * 4 + C]
                            // Two level switch statement:
                            // MOVX R3, [R1 + R2 + C]
                            // MOVX R4, [R1 + R3 * 4 + C]
                            if (switchStatementImageBase != null
                                && instr.Value.MemoryBase == switchStatementImageBase
                                && instr.Value.MemoryIndex != Register.None
                                //&& instr.Value.MemoryIndexScale == 4
                                )
                            {
                                if (instr.Value.MemoryIndexScale == 1)
                                { // Two level switch statement
                                    if (MovJumpIndexBasedCheckIndexRegister(potentialSwitchStatementJumpLimit.limitReg, instr.Value.MemoryIndex))
                                    {
                                        switchStatementOffset = (uint)instr.Value.MemoryDisplacement64;
                                        //Console.WriteLine($"{BASE_ADDRESS + instr.Value.IP:X} {instr} {instr.Value.MemoryIndexScale} {potentialSwitchStatementJumpLimit.limitReg} {instr.Value.MemoryIndex} ({switchStatementOffset:X}, {potentialSwitchStatementJumpLimit.limitNum})");
                                        potentialSwitchStatementJumpLimit.limitReg = instr.Value.Op0Register;
                                        potentialSwitchStatementJumpLimit.limitNum = (int)SwitchStatementPrintOffsets(switchStatementOffset, potentialSwitchStatementJumpLimit.limitNum, instr.Value.MemoryIndexScale).Last().Key;
                                    }
                                }
                                else if (instr.Value.MemoryIndexScale == 4)
                                { // One level switch statement
                                    if (MovJumpIndexBasedCheckIndexRegister(potentialSwitchStatementJumpLimit.limitReg, instr.Value.MemoryIndex))
                                    {
                                        switchStatementOffset = (uint)instr.Value.MemoryDisplacement64;
                                        //Console.WriteLine($"{BASE_ADDRESS + instr.Value.IP:X} {instr} {instr.Value.MemoryIndexScale} {potentialSwitchStatementJumpLimit.limitReg} {instr.Value.MemoryIndex} ({switchStatementOffset:X}, {potentialSwitchStatementJumpLimit.limitNum})");
                                        var switchJumps = SwitchStatementPrintOffsets(switchStatementOffset, potentialSwitchStatementJumpLimit.limitNum, instr.Value.MemoryIndexScale);
                                        foreach (var switchJump in switchJumps.Keys)
                                            jumps.TryAdd<uint, uint>(switchJump, 0);
                                        //_parent.possibleSwitchStatements++;
                                        potentialSwitchStatementJumpLimit = null;
                                    }
                                }
                            }
                        }
                        for (int i = 0; i < instr.Value.OpCount; i++)
                        {
                            if (instr.Value.GetOpKind(i) == OpKind.Memory)
                            {
                                var va = instr.Value.GetVirtualAddress(i, 0, DefaultGetRegister);
                                foreach (var targetArea in _parent.TargetAreas)
                                {
                                    if (va - targetArea.Value.Range.Start < targetArea.Value.Range.Length)
                                    {
                                        var foundAddr = $"0x{BASE_ADDRESS + instr.Value.IP:X}";
                                        var firstColGap = new string(' ', 0x10 - foundAddr.Length);
                                        var funcNameGap = new string(' ', 0x22 - FunctionName.Length);
                                        var sliceName = targetArea.Key;
                                        var sliceGap = new string(' ', _parent.GetSizeOfGlobalNameColumn() - targetArea.Key.Length);
                                        if (!targetArea.Value.Locations.ContainsKey((uint)instr.Value.IP))
                                        {
                                            var offsets = _decoder.GetConstantOffsets(instr.Value);
                                            _stream.BaseStream.Seek(_parent.GetRealAddress((uint)instr.Value.IP + offsets.DisplacementOffset), SeekOrigin.Begin);
                                            var gRef = new GlobalReference();
                                            gRef.DisplacementOffset = offsets.DisplacementOffset;
                                            gRef.InstructionLength = instr.Value.Length;
                                            gRef.PointerOffset = (int)(va - targetArea.Value.Range.Start);
                                            uint gDisp = _stream.ReadUInt32();
                                            _stream.BaseStream.Seek(_parent.GetRealAddress((uint)instr.Value.NextIP), SeekOrigin.Begin);
                                            gRef.bIsAbsolute = (gDisp - targetArea.Value.Range.Start < targetArea.Value.Range.Length) ? true : false;
                                            gRef.MemoryBase = instr.Value.MemoryBase;
                                            gRef.Displacement = gDisp;
                                            _utils.Log($"{foundAddr}{firstColGap}{FunctionName}{funcNameGap}{sliceName}{sliceGap}{instr.Value}");
                                            targetArea.Value.Locations.Add((uint)instr.Value.IP, gRef);
                                            targetArea.Value.FoundInstances++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                } while (
                instr.Value.Mnemonic != Mnemonic.Ret // normal end of function
                && instr.Value.Mnemonic != Mnemonic.Int3 // function ends with jmp, but padded with the breakpoint interrupt
                && !bIsThunk // dedicated thunk function case (~20% speed increase)
                && !panicSwitch // stop all inner decoding if we get sent into denuvo jail
                );
                // this has covered every branch up until the ret/int3
                childEarliestStart = (uint)instr.Value.NextIP;
                foreach (var jump in jumps.Keys)
                    if (jump > childEarliestStart)
                        childEarliestStart = DecodeLocalFunction(jump, 0, jumpDepth);
                return childEarliestStart;
            }
        }

        public void DecodeChildFunction(Decoder decoder, BinaryReader stream, uint rip, ulong pReturn, string? name = null)
        {
            if (rip % REQUIRED_ALIGNOF == 0)
            {
                DecodedSubroutines.Add(rip);
                var fn = new ChildFunction(decoder, stream, this, rip, _utils, name);
                fn.Decode();
                stream.BaseStream.Seek(GetRealAddress((uint)pReturn), SeekOrigin.Begin);
                decoder.IP = (uint)pReturn;
            }
            // else it's likely a denuvo'd function. it may also be one of the statically
            // linked libraries which use a different alignment for functions, but we don't care about those
        }

        public unsafe struct FlowscriptSectionEntry
        {
            public uint SectionStart;
            public ulong EntryCount;

            public static FlowscriptSectionEntry Read(BinaryReader reader)
            {
                var newEntry = new FlowscriptSectionEntry();
                newEntry.SectionStart = (uint)(reader.ReadUInt64() - BASE_ADDRESS);
                newEntry.EntryCount = reader.ReadUInt64();
                return newEntry;
            }
        }
        public unsafe struct ScriptCommandFunctionTableEntry
        {
            public uint Fn;
            public ulong Parameters;
            public uint Name;

            public static ScriptCommandFunctionTableEntry Read(BinaryReader reader)
            {
                var newEntry = new ScriptCommandFunctionTableEntry();
                newEntry.Fn = (uint)(reader.ReadUInt64() - BASE_ADDRESS);
                newEntry.Parameters = reader.ReadUInt64();
                newEntry.Name = (uint)(reader.ReadUInt64() - BASE_ADDRESS);
                return newEntry;
            }
        }

        public bool DecodeProgram()
        {
            using (var exec = new BinaryReader(File.Open(ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                using (var hasher = SHA256.Create())
                {
                    hasher.ComputeHash(exec.BaseStream);
                    _utils.Log($"Executable is {Path.GetFileName(ExecutablePath)} (TODO: Check hashes)");
                }
                // do this a better way once we get around to this doing for real.
                // read DOS header, get pointer to NT header
                exec.BaseStream.Seek(0x3c, SeekOrigin.Begin);
                var e_lfanew = exec.ReadInt32();
                exec.BaseStream.Seek(e_lfanew, SeekOrigin.Begin);
                // read NT header.
                exec.BaseStream.Seek(6, SeekOrigin.Current);
                var NumberOfSections = exec.ReadInt16();
                exec.BaseStream.Seek(0xc, SeekOrigin.Current);
                var SizeOfOptionalHeader = exec.ReadInt16();
                exec.BaseStream.Seek(0x6, SeekOrigin.Current);
                //exec.BaseStream.Seek(0x6e, SeekOrigin.Current);
                SizeOfCode = exec.ReadInt32();
                exec.BaseStream.Seek(0x64, SeekOrigin.Current);
                var NumberOfRvaAndSizes = exec.ReadInt32();
                if (NumberOfRvaAndSizes < 4)
                {
                    LastError = "Too few RVAs for runtime function entry table to exist";
                    return false;
                }
                // get pointer to unwind data
                exec.BaseStream.Seek(0x8 * 3, SeekOrigin.Current); // go to DataDirectory[3]
                var vpUnwindData = exec.ReadUInt32();
                var unwindDataCount = exec.ReadUInt32() / 0xc;
                // sizeof(DOS Header) + sizeof(NT Header Base) + sizeof(NT Header Options)
                exec.BaseStream.Seek(e_lfanew + 0x18 + SizeOfOptionalHeader, SeekOrigin.Begin);
                // Map virtual addresses to raw addresses, including their size
                for (int i = 0; i < NumberOfSections; i++)
                {
                    byte[] bSectionName = exec.ReadBytes(8);
                    string SectionName = "";
                    unsafe
                    {
                        nint pSectionName = (nint)NativeMemory.Alloc((nuint)bSectionName.Length);
                        Marshal.Copy(bSectionName, 0, pSectionName, 8);
                        SectionName = Marshal.PtrToStringAnsi(pSectionName, 8);
                        NativeMemory.Free((void*)pSectionName);
                    }
                    exec.BaseStream.Seek(0x4, SeekOrigin.Current); // skip Name and Misc
                    Slice newSlice = new Slice(exec.ReadUInt32(), exec.ReadUInt32()); // VirtualAddress, SizeOfRawData
                    Sections.Add((newSlice, SectionName));
                    VirtualToPhysicalAddress.Add(newSlice, exec.ReadUInt32()); // PointerToRawData
                    exec.BaseStream.Seek(0x10, SeekOrigin.Current);
                }
                foreach (var section in Sections)
                {
                    if (VirtualToPhysicalAddress.TryGetValue(section.Bounds, out var real))
                        _utils.Log($"({section.Name}: start 0x{section.Bounds.Start:X}, len 0x{section.Bounds.Length:X}, real 0x{real:X})");
                }
                // Read unwind data
                exec.BaseStream.Seek(GetRealAddress(vpUnwindData), SeekOrigin.Begin);
                for (int i = 0; i < unwindDataCount; i++)
                {
                    var vpFuncStart = exec.ReadUInt32();
                    var fnSize = exec.ReadUInt32() - vpFuncStart;
                    exec.ReadUInt32(); // vpUnwindInfo
                    FunctionAddresses.Add(vpFuncStart, (vpFuncStart, GetRealAddress(vpFuncStart), fnSize));
                }
                // Start decoding instructions (for unwind data, these are usually at the start of functions, but may be elsewhere
                var codeReader = new StreamCodeReader(exec.BaseStream);
                var decoder = Decoder.Create(64, codeReader);
                var time = Stopwatch.StartNew();
                _utils.Log($"Instr Addr{new string(' ', 0x10 - "Instr Addr".Length)}Func Name{new string(' ', GetSizeOfGlobalNameColumn() - "Func Addr".Length)}Target{new string(' ', "gDatUnit".Length - "Target".Length + 2)}Decoded Instruction");
                // Flowscript Pass
                exec.BaseStream.Seek(GetRealAddress(0x1a3d5c0), SeekOrigin.Begin); // we'd get this address by sigscanning in reality
                List<FlowscriptSectionEntry> flowSections = new();
                for (int i = 0; i < 6; i++)
                    flowSections.Add(FlowscriptSectionEntry.Read(exec));
                foreach (var flowSection in flowSections)
                {
                    exec.BaseStream.Seek(GetRealAddress(flowSection.SectionStart), SeekOrigin.Begin);
                    for (uint i = 0; i < (uint)flowSection.EntryCount; i++)
                    {
                        var flowEntry = ScriptCommandFunctionTableEntry.Read(exec);
                        uint returnPos = flowSection.SectionStart;
                        unsafe { returnPos += (i + 1) * (uint)sizeof(ScriptCommandFunctionTableEntry); }
                        exec.BaseStream.Seek(GetRealAddress(flowEntry.Name), SeekOrigin.Begin);
                        byte[] bNameArea = exec.ReadBytes(32);
                        string? funcName = null;
                        unsafe
                        {
                            nint pNameArea = (nint)NativeMemory.Alloc((nuint)bNameArea.Length);
                            Marshal.Copy(bNameArea, 0, pNameArea, bNameArea.Length);
                            funcName = Marshal.PtrToStringAnsi(pNameArea);
                            NativeMemory.Free((void*)pNameArea);
                            if (!DecodedSubroutines.Contains(flowEntry.Fn))
                                DecodeChildFunction(decoder, exec, flowEntry.Fn, returnPos, funcName);
                        }
                    }
                }
                // Unwind instruction pass
                foreach (var fn in FunctionAddresses)
                    if (!DecodedSubroutines.Contains(fn.Value.rip))
                        DecodeChildFunction(decoder, exec, fn.Value.rip, 0);
                time.Stop();
                foreach (var targetArea in TargetAreas)
                    _utils.Log($"{targetArea.Key}: {targetArea.Value.FoundInstances} instances found");
                _utils.Log($"Completed in {time.ElapsedMilliseconds} ms");
            }
            return true;
        }
    }
}
