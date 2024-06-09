using riri.commonmodutils;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace riri.globalredirector
{
    [SupportedOSPlatform("windows")]
    public class AllocatorWin32 : ModuleBase<RedirectorContext>
    {
        //private Dictionary<string, Slice> Allocations = new();
        private Dictionary<nuint, int> Allocations = new();
        public nuint _minimumPossibleAddress { get; private set; }
        public nuint _maximumPossibleAddress { get; private set; }
        public uint WordSize { get; private set; }

        private SortedDictionary<nuint, uint> CommittedPages = new(); // (start, size)

        public unsafe AllocatorWin32(RedirectorContext context, Dictionary<string, ModuleBase<RedirectorContext>> modules) : base(context, modules)
        {
            _minimumPossibleAddress = nuint.Zero;
            _maximumPossibleAddress = (nuint)_context._baseAddress + uint.MaxValue;
            WordSize = 0;
        }
        public unsafe override void Register()
        {
        }

        public unsafe void OnLoaderInitialized()
        {
            _context._utils.Log($"Sleeping KernelTest thread...");
            Thread.Sleep(10000);
            var pSystemInfo = (Kernel32.SYSTEM_INFO*)NativeMemory.Alloc((nuint)sizeof(Kernel32.SYSTEM_INFO));
            Kernel32.GetSystemInfo(pSystemInfo);
            WordSize = pSystemInfo->dwPageSize;
            NativeMemory.Free(pSystemInfo);
            _context._utils.Log($"WORD SIZE: 0x{WordSize:X}");
            _minimumPossibleAddress = FindLowestReservedPage(WordSize);
            _context._utils.Log($"Lowest available page is at 0x{_minimumPossibleAddress:X}");
        }
        public unsafe nuint FindLowestReservedPage(uint minimumSize)
        {
            nuint lowestPossiblePage = (CommittedPages.Count == 0) 
                ? (nuint)(_context._baseAddress + _context._processModuleSize)
                : CommittedPages.Last().Key + CommittedPages.Last().Value;
            var pPageQuery = (Kernel32._MEMORY_BASIC_INFORMATION*)NativeMemory.Alloc((nuint)sizeof(Kernel32._MEMORY_BASIC_INFORMATION));
            nuint lowestReservedPage = 0;
            // Find the earliest reserved page
            while (lowestReservedPage == 0 && lowestPossiblePage < _maximumPossibleAddress)
            {
                Kernel32.VirtualQuery(lowestPossiblePage, pPageQuery, sizeof(Kernel32._MEMORY_BASIC_INFORMATION));
                _context._utils.Log($"base address 0x{pPageQuery->BaseAddress:X}, allocation 0x{pPageQuery->AllocationBase:X}, size 0x{pPageQuery->RegionSize:X}, state {pPageQuery->State}");
                if (pPageQuery->State == Kernel32.MEMORY_BASIC_INFO_STATE.MEM_RESERVE
                    && pPageQuery->RegionSize > minimumSize)
                    lowestReservedPage = pPageQuery->BaseAddress;
                lowestPossiblePage += pPageQuery->RegionSize;
            }
            NativeMemory.Free(pPageQuery);
            return lowestReservedPage;
        }

        public unsafe nuint CommitLowestReservedPage(uint pageTotalSize)
        {
            var newlyCommittedBase = Kernel32.VirtualAlloc(FindLowestReservedPage(pageTotalSize), pageTotalSize,
                Kernel32.MEM_ALLOCATION_TYPE.MEM_COMMIT, Kernel32.MEM_PROTECTION.PAGE_EXECUTE_READWRITE);
            CommittedPages.Add(newlyCommittedBase, pageTotalSize);
            return newlyCommittedBase;
        }

        public unsafe bool DetermineMinimumAddress()
        {
            _minimumPossibleAddress = FindLowestReservedPage(WordSize);
            return _minimumPossibleAddress != 0;
        }

        public unsafe void AllocatePages(int length)
        {
            //Kernel32.VirtualAlloc();
        }
    }
}
