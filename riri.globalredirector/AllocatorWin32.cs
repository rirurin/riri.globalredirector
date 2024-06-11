using riri.commonmodutils;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

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
            var systemInfo = new SYSTEM_INFO();
            GetSystemInfo(&systemInfo);
            WordSize = systemInfo.dwPageSize;
            _context._utils.Log($"WORD SIZE: 0x{WordSize:X}");
            _minimumPossibleAddress = FindLowestReservedPage(WordSize);
            _context._utils.Log($"Lowest available page is at 0x{_minimumPossibleAddress:X}");
        }
        public unsafe nuint FindLowestReservedPage(uint minimumSize)
        {
            nuint lowestPossiblePage = (CommittedPages.Count == 0) 
                ? (nuint)(_context._baseAddress + _context._processModuleSize)
                : CommittedPages.Last().Key + CommittedPages.Last().Value;
            var pageQuery = new MEMORY_BASIC_INFORMATION();
            nuint lowestReservedPage = 0;
            // Find the earliest reserved page
            while (lowestReservedPage == 0 && lowestPossiblePage < _maximumPossibleAddress)
            {
                VirtualQuery((void*)lowestPossiblePage, &pageQuery, (nuint)sizeof(MEMORY_BASIC_INFORMATION));
                _context._utils.Log($"base address 0x{(nint)pageQuery.BaseAddress:X}, allocation 0x{(nint)pageQuery.AllocationBase:X}, size 0x{pageQuery.RegionSize:X}, state {pageQuery.State}");
                if (pageQuery.State == 0x2000 // MEM_RESERVE
                    && pageQuery.RegionSize > minimumSize)
                    lowestReservedPage = (nuint)pageQuery.BaseAddress;
                lowestPossiblePage += pageQuery.RegionSize;
            }
            return lowestReservedPage;
        }

        /*
        public unsafe nuint CommitLowestReservedPage(uint pageTotalSize)
        {
            var newlyCommittedBase = (nuint)VirtualAlloc((void*)FindLowestReservedPage(pageTotalSize), pageTotalSize,
                0x1000, 0x40);
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
        */
    }
}
