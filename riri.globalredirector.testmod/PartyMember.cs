using Reloaded.Hooks.Definitions;
using riri.commonmodutils;
using System.Runtime.InteropServices;

namespace riri.globalredirector.testmod
{
    public class PartyMember : ModuleBase<TestModContext>
    {
        private string TaskSchedulerLoop_SIG = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 83 0D ?? ?? ?? ?? 01";
        private IHook<TaskSchedulerLoop> _taskSchedulerLoop;
        private unsafe delegate void TaskSchedulerLoop(float delta, char a2);

        private string GetPersonaCount_SIG = "48 8D 85 ?? ?? ?? ?? 48 03 C1 F6 00 ?? 48 0F 45 D0";

        private unsafe DatUnit* _extendedPartyMembers = null;
        public unsafe PartyMember(TestModContext context, Dictionary<string, ModuleBase<TestModContext>> modules) : base(context, modules)
        {
            _context._utils.SigScan(TaskSchedulerLoop_SIG, "TaskSchedulerLoop", _context._utils.GetDirectAddress,
                addr => _taskSchedulerLoop = _context._utils.MakeHooker<TaskSchedulerLoop>(TaskSchedulerLoopImpl, addr));
            _context._redirectorApi.AddTarget("gDatGlobal", sizeof(DatUnit) * 11, GetPersonaCount_SIG,
                x => (nuint)(_context._baseAddress + *(uint*)(_context._utils.GetDirectAddress(x) + 0x3) - 0x44));
        }

        public override void Register()
        {

        }

        public unsafe void TaskSchedulerLoopImpl(float delta, char a2)
        {
            if ((GetAsyncKeyState(116) & 0x1) != 0 && _extendedPartyMembers == null)
            {
                _context._utils.Log($"TODO: Hook allocation");
            }
            _taskSchedulerLoop.OriginalFunction(delta, a2);
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x30)]
        public unsafe struct DatPersona
        {
            [FieldOffset(0x2)] public ushort ID;
            [FieldOffset(0x4)] public byte level;
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x2a0)]
        public unsafe struct DatUnit
        {
            [FieldOffset(0x8)] public uint unitID;
            [FieldOffset(0xc)] public uint currHP;
            [FieldOffset(0x10)] public uint currSP;

            public DatPersona* GetPersona(int id)
            {
                if (id < 0 || id > 0xc) return null;
                fixed (DatUnit* self = &this)
                {
                    return (DatPersona*)((nint)self + 0x44) + id;
                }
            }
        }

        [DllImport("user32.dll")]
        public static extern ushort GetAsyncKeyState(int vKey);
    }
}
