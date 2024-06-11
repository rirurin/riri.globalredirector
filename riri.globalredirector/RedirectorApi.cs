using Iced.Intel;
using Reloaded.Memory.Interfaces;
using riri.commonmodutils;
using riri.globalredirector.Interfaces;
using System.Collections.Concurrent;

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

        public void AddTarget(string name, int length, string sigscan, Func<int, nuint> transformCb)
        {
            if (bIsDecoded)
            {
                _context._utils.Log($"ERROR: Can only register a target during initialization", System.Drawing.Color.Red, LogLevel.Error);
                return;
            }
            /*
            _context._utils.SigScan(sigscan, name, transformCb,
                addr => _context._utils.Log($"found target {name}: {(nint)addr:X}"));
            */
            TargetsToFind.Add(name);
            _context._utils.SigScan(sigscan, name, transformCb,
                addr =>
                {
                    //_context._utils.Log($"{name}: {(nint)addr:X}");
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
                                    //_redirector.program.DecodeProgram();
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
        public unsafe TGlobalType* ReallocateGlobal<TGlobalType>(TGlobalType* old, string name, int newLength) where TGlobalType : unmanaged
        {
            return null;
            //return (TGlobalType*)_allocator.CommitLowestReservedPage((uint)newLength);
            /*
            if (old != null) // handle relocation
            {

            }
            */
            // Look for the first area that contains a suitable area
            /*
            if (_redirector.program.TargetAreas.TryGetValue(name, out var targets))
            {
                foreach (var location in targets.Locations)
                    RedirectPointer(target, location);
            }
            */
            return null;
        }
    }
}
