using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace riri.globalredirector
{
    [SupportedOSPlatform("windows")]
    public static partial class Kernel32
    {
        // https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualalloc
        [Flags]
        public enum MEM_ALLOCATION_TYPE : uint
        {
            MEM_COMMIT = 0x1000,
            MEM_RESERVE = 0x2000,
            MEM_RESET = 0x80000,
            MEM_RESET_UNDO = 0x1000000,
            MEM_LARGE_PAGES = 0x20000000,
            MEM_PHYSICAL = 0x400000,
            MEM_TOP_DOWN = 0x100000,
            MEM_WRITE_WATCH = 0x200000
        }

        // https://learn.microsoft.com/en-us/windows/win32/Memory/memory-protection-constants
        [Flags]
        public enum MEM_PROTECTION : uint
        {
            PAGE_NOACCESS = 0x1,
            PAGE_READONLY = 0x2,
            PAGE_READWRITE = 0x4,
            PAGE_WRITECOPY = 0x8,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400,
            PAGE_TARGETS_INVALID = 0x40000000,
            PAGE_TARGETS_NO_UPDATE = 0x40000000
        }

        // from https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-memory_basic_information
        [Flags]
        public enum MEMORY_BASIC_INFO_STATE : uint
        {
            MEM_COMMIT = 0x1000,
            MEM_RESERVE = 0x2000,
            MEM_FREE = 0x10000
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x30)]
        public unsafe struct _MEMORY_BASIC_INFORMATION
        {
            [FieldOffset(0x0)] public nuint BaseAddress;
            [FieldOffset(0x8)] public nuint AllocationBase;
            [FieldOffset(0x10)] public uint AllocationProtect;
            [FieldOffset(0x18)] public nuint RegionSize;
            [FieldOffset(0x20)] public MEMORY_BASIC_INFO_STATE State;
            [FieldOffset(0x24)] public uint Protect;
            [FieldOffset(0x28)] public uint Type;
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x24)]
        public unsafe struct SYSTEM_INFO
        {
            // DUMMYUNIONNAME START
            [FieldOffset(0x0)] public uint dwOemId;
            // DUMMYSTRUCTNAME
            [FieldOffset(0x0)] public ushort wProcessorArchitecture;
            [FieldOffset(0x2)] public ushort wReserved;
            // DUMMYUNIONNAME END
            [FieldOffset(0x4)] public uint dwPageSize;
            [FieldOffset(0x8)] public nint lpMinimumApplicatonAddress;
            [FieldOffset(0x10)] public nint lpMaximumApplicatonAddress;
        }

        [SuppressGCTransition]
        [LibraryImport("kernel32.dll")]
        public unsafe static partial nuint GetSystemInfo(SYSTEM_INFO* lpSystemInfo);

        [SuppressGCTransition]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.SysUInt)]
        public unsafe static partial nuint VirtualQuery(nuint lpAddress, _MEMORY_BASIC_INFORMATION* lpBuffer, nint dwLength);

        [SuppressGCTransition]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        public unsafe static partial nuint VirtualAlloc(nuint lpAddress, nuint dwSize, MEM_ALLOCATION_TYPE flAllocationType, MEM_PROTECTION flProtect);

        [SuppressGCTransition]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public unsafe static partial bool VirtualFree(nuint lpAddress, nuint size, uint dwFreeType);
    }
}
