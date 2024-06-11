using Iced.Intel;
using Reloaded.Memory.Interfaces;
using riri.commonmodutils;
using riri.globalredirector.Interfaces;
using System.Runtime.InteropServices;

namespace riri.globalredirector
{
    public struct Slice
    {
        public uint Start { get; private set; }
        public uint Length { get; private set; }
        public Slice(uint start, uint length)
        {
            Start = start;
            Length = length;
        }

        public bool IsNull() => Start == 0;
    }

    public class TargetArea
    {
        public Slice Range { get; private set; }
        public int FoundInstances { get; set; }
        public SortedDictionary<uint, GlobalReference> Locations { get; private set; } = new();

        public TargetArea(Slice range)
        {
            Range = range;
            FoundInstances = 0;
        }
        public TargetArea(uint start, uint length)
        {
            Range = new Slice(start, length);
            FoundInstances = 0;
        }
    }
    public struct GlobalReference
    {
        public byte DisplacementOffset;
        public int InstructionLength;
        public bool bIsAbsolute;
        public int PointerOffset;
        public Register MemoryBase;
        public uint Displacement;
    }
    public class TargetAreaQueued
    {
        public int length { get; private set; }
        public string sigscan { get; private set; }
        public Func<int, nuint> transformCb { get; private set; }

        public TargetAreaQueued(int pLength, string pDigscan, Func<int, nuint> pTransformCb)
        {
            length = pLength;
            sigscan = pDigscan;
            transformCb = pTransformCb;
        }
    }

    public class RedirectorApi : ModuleBase<RedirectorContext>, IRedirectorApi
    {
        private object _targetFindLock = new object();
        public HashSet<string> TargetsToFind = new();
        private bool bIsDecoded = false;

        private Redirector _redirector;
        private AllocatorWin32 _allocator;
        public unsafe RedirectorApi(RedirectorContext context, Dictionary<string, ModuleBase<RedirectorContext>> modules) : base(context, modules)
        {

        }
        public override void Register()
        {
            _redirector = GetModule<Redirector>();
            _allocator = GetModule<AllocatorWin32>();
        }

        public void AddTargetRaw(string name, int length, string sigscan, Func<int, nuint> transformCb)
        {
            if (bIsDecoded)
            {
                _context._utils.Log($"ERROR: Can only register a target during initialization", System.Drawing.Color.Red, LogLevel.Error);
                return;
            }
            TargetsToFind.Add(name);
            _context._utils.SigScan(sigscan, name, transformCb,
                addr =>
                {
                    _context._utils.Log($"found {name}: {(nint)addr:X}");
                    if (_redirector.program.TargetAreas.TryAdd(name, new TargetArea((uint)(addr - _context._baseAddress), (uint)length)))
                    {
                        lock (_targetFindLock)
                        {
                            TargetsToFind.Remove(name);
                            if (TargetsToFind.Count == 0)
                            {
                                bIsDecoded = true;
                                if (_allocator._minimumPossibleAddress != 0)
                                {
                                    _redirector.program.DecodeProgram();
                                }
                            }
                        }
                    } else
                    {
                        _context._utils.Log($"ERROR: {name} was already added as a target", System.Drawing.Color.Red, LogLevel.Error);
                    }
                }
            );
        }

        public unsafe void AddTarget<TGlobalType>(string name, int lengthEntries, string sigscan, Func<int, nuint> transformCb) where TGlobalType : unmanaged
            => AddTargetRaw(name, lengthEntries * sizeof(TGlobalType), sigscan, transformCb);

        private void RedirectPointer(nuint target, KeyValuePair<uint, GlobalReference> location)
        {
            uint newOffset = (location.Value.bIsAbsolute) ?
                                (uint)(target - (nuint)_context._baseAddress + (nuint)location.Value.PointerOffset)
                                : (uint)(target + (nuint)location.Value.PointerOffset - (nuint)(_context._baseAddress + location.Key + location.Value.InstructionLength));
            {
                using (var protection = _context._memory.ChangeProtectionDisposable((nuint)(_context._baseAddress + location.Key), 0x10, Reloaded.Memory.Enums.MemoryProtection.ReadWriteExecute))
                {
                    _context._utils.Log($"address 0x{_context._baseAddress + location.Key:X} + 0x{location.Value.DisplacementOffset:X}, absolute? {location.Value.bIsAbsolute}, offset {location.Value.PointerOffset:X}, new value 0x{newOffset:X}");
                    _context._memory.Write((nuint)_context._baseAddress + location.Key + location.Value.DisplacementOffset, newOffset);
                }
            }
        }
        public unsafe nuint MoveGlobalRaw(nuint old, string name, int lengthBytes)
        {
            if (!_redirector.program.TargetAreas.TryGetValue(name, out var targetArea))
            {
                _context._utils.Log($"Error: Couldn't find the target \"{name}\". Make sure you added this target using the AddTarget() or AddTargetRaw() methods.", System.Drawing.Color.Red, LogLevel.Error);
                return 0;
            }
            Slice dataToCopy;
            if (old != 0) // we're grabbing from the original global
            {
                _context._utils.Log($"TODO: Implement moving already moved globals");
                return 0;
            }
            dataToCopy = targetArea.Range;
            // Make allocation
            var alloc = _allocator.Allocate(lengthBytes, name);
            if (alloc == 0)
            {
                _context._utils.Log($"Error: Allocation failed, passed maximum allowed address.", System.Drawing.Color.Red, LogLevel.Error);
                return 0;
            }
            // Move contents
            _context._utils.Log($"{name} ALLOC: {(nint)alloc:X}, copy from {_context._baseAddress + dataToCopy.Start:X}, length {dataToCopy.Length:X}");
            NativeMemory.Copy((void*)(_context._baseAddress + dataToCopy.Start), (void*)alloc, dataToCopy.Length);
            // Redirect pointers
            foreach (var location in targetArea.Locations)
                RedirectPointer(alloc, location);
            return alloc;
        }
        public unsafe TGlobalType* MoveGlobal<TGlobalType>(TGlobalType* old, string name, int newLength) where TGlobalType : unmanaged
            => (TGlobalType*)MoveGlobalRaw((nuint)old, name, newLength * sizeof(TGlobalType));
    }
}
