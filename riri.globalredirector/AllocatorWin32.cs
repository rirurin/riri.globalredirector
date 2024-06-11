using riri.commonmodutils;
using System.Runtime.Versioning;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace riri.globalredirector
{
    [SupportedOSPlatform("windows")]
    public class AllocatorWin32 : ModuleBase<RedirectorContext>, Allocator
    {
        private SortedDictionary<string, nuint> NameToAllocation = new();
        private SortedDictionary<nuint, int> Allocations = new(); // (start, size)
        public nuint _minimumPossibleAddress { get; private set; }
        public nuint _maximumPossibleAddress { get; private set; }
        public uint PageSize { get; private set; }

        private SortedSet<nuint> CommittedPages = new();

        public unsafe AllocatorWin32(RedirectorContext context, Dictionary<string, ModuleBase<RedirectorContext>> modules) : base(context, modules)
        {
            _minimumPossibleAddress = nuint.Zero;
            _maximumPossibleAddress = (nuint)_context._baseAddress + uint.MaxValue;
            var systemInfo = new SYSTEM_INFO();
            GetSystemInfo(&systemInfo);
            PageSize = systemInfo.dwPageSize;
            _minimumPossibleAddress = FindLowestReservedPage(PageSize);
            _context._utils.Log($"Lowest available page is at 0x{_minimumPossibleAddress:X}");
        }
        public unsafe override void Register()
        {
        }
        private unsafe nuint FindLowestReservedPage(uint minimumSize)
        {
            nuint lowestPossiblePage = GetLowestFreeAddress();
            var pageQuery = new MEMORY_BASIC_INFORMATION();
            nuint lowestReservedPage = 0;
            while (lowestReservedPage == 0 && lowestPossiblePage < _maximumPossibleAddress)
            { // Find the earliest reserved page
                VirtualQuery((void*)lowestPossiblePage, &pageQuery, (nuint)sizeof(MEMORY_BASIC_INFORMATION));
                _context._utils.Log($"base address 0x{(nint)pageQuery.BaseAddress:X}, allocation 0x{(nint)pageQuery.AllocationBase:X}, size 0x{pageQuery.RegionSize:X}, state {pageQuery.State}");
                if (pageQuery.State == 0x2000 // MEM_RESERVE
                    && pageQuery.RegionSize > minimumSize)
                    lowestReservedPage = (nuint)pageQuery.BaseAddress;
                lowestPossiblePage += pageQuery.RegionSize;
            }
            return lowestReservedPage;
        }
        private unsafe nuint GetMinimumAddress() =>
            (_minimumPossibleAddress != 0) ? _minimumPossibleAddress : (nuint)(_context._baseAddress + _context._processModuleSize);
        private unsafe nuint GetLowestFreeAddress() =>
            (CommittedPages.Count > 0) ? GetLastPageMaxAddress() : GetMinimumAddress();
        private unsafe nuint GetLastPageMaxAddress() =>
            CommittedPages.Last() + PageSize;
        
        private unsafe nuint CommitLowestSuitablePages(uint pageTotalSize)
        {
            var alignment = pageTotalSize % PageSize;
            if (alignment > 0)
                pageTotalSize += PageSize - alignment;
            var lowestValidAddress = FindLowestReservedPage(pageTotalSize);
            var newlyCommittedBase = (nuint)VirtualAlloc((void*)lowestValidAddress, pageTotalSize, 0x1000, 0x40); // MEM_COMMIT, PAGE_EXECUTE_READWRITE
            var pages = pageTotalSize / PageSize;
            _context._utils.Log($"Committed new page at 0x{newlyCommittedBase:X}, {pages} pages long");
            for (int i = 0; i < pages; i++)
                CommittedPages.Add(lowestValidAddress + (nuint)(PageSize * i));
            return newlyCommittedBase;
        }

        private unsafe nuint GetAllocationBasePage(nuint address) => address - address % PageSize;
        private unsafe bool FindSuitableGap(nuint start, int lengthBytes, out nuint gapAddress)
        {
            gapAddress = start;
            foreach (var allocation in Allocations)
            {
                var newCursor = allocation.Key;
                if (gapAddress + (nuint)lengthBytes < newCursor
                    // remov transitions from our committed page into someone else's
                    && CommittedPages.Contains(GetAllocationBasePage(gapAddress + (nuint)lengthBytes)))
                    return true;
                gapAddress = allocation.Key + (nuint)allocation.Value;
            }
            return false;
        }

        public unsafe nuint Allocate(int lengthBytes, string name)
        {
            var allocation = Allocate(lengthBytes);
            _context._utils.Log($"Allocator added 0x{(nint)allocation:X}, size 0x{lengthBytes:X}");
            Allocations.Add(allocation, lengthBytes);
            NameToAllocation.Add(name, allocation);
            return allocation;
        }

        private unsafe nuint Allocate(int lengthBytes)
        {
            var allocatorCursor = _minimumPossibleAddress;
            nuint allocation;
            // note that pages can't be freed, only allocations (since we're moving global variables, there's an
            // assumption made by the program that their lifetime is static)
            if (CommittedPages.Count == 0)
            { // We have to commit a new page
                //_context._utils.Log($"first page committed!");
                allocation = CommitLowestSuitablePages((uint)lengthBytes);
            } else
            {
                // We can slot it inside of an existing allocation
                if (FindSuitableGap(allocatorCursor, lengthBytes, out var gapAddress))
                {
                    //_context._utils.Log($"Found suitable gap within existing allocations");
                    allocation = gapAddress;
                }
                else
                {
                    if (gapAddress + (nuint)lengthBytes > _maximumPossibleAddress) // not possible to allocate. fail it.
                    {
                        return 0;
                    }
                    else if (gapAddress + (nuint)lengthBytes < GetLastPageMaxAddress())
                    {
                        //_context._utils.Log($"Found suitable gap.");
                        allocation = gapAddress;
                    } else
                    { // we need to allocate a new area...
                        //_context._utils.Log($"Lowest free page: {GetLowestFreeAddress():X}, gap address {gapAddress:X}");
                        if (FindLowestReservedPage((uint)lengthBytes) == GetLowestFreeAddress())
                        { // this can be contiguous with existing allocations
                            var gapSize = (nuint)lengthBytes - (GetLowestFreeAddress() - gapAddress);
                            //_context._utils.Log($"Found suitable area to make contiguous allocation. size: {gapSize:X}");
                            CommitLowestSuitablePages((uint)gapSize);
                            allocation = gapAddress;
                        } else
                        { // this needs to be separate from everything else
                            //_context._utils.Log($"Found disparate area. Allocating full size.");
                            allocation = CommitLowestSuitablePages((uint)lengthBytes);
                        }
                    }
                }
            }
            return allocation;
        }
    }
}
