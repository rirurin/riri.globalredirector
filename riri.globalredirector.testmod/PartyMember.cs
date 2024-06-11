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

        private string MakeActivePartyMemberList_SIG = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 48 83 EC 20 BB 01 00 00 00";
        private IHook<MakeActivePartyMemberList> _makeActivePartyMemberList;
        private unsafe delegate int MakeActivePartyMemberList(ushort* partyMembers);

        private string ChangePartyMemberSelected_SIG = "40 53 48 83 EC 20 48 8D 59 ?? 41 8B C0";
        private IHook<ChangePartyMemberSelected> _changePartyMemberSelected;
        private unsafe delegate byte ChangePartyMemberSelected(CampStatsSubmenu* statsSubmenu, int type, int target);

        private string DrawPartyMemberActiveType_SIG = "48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 57 48 81 EC F0 00 00 00 0F 29 70 ?? 41 0F B6 F1";
        private IHook<DrawPartyMemberActiveType> _drawPartyMemberActiveType;
        private unsafe delegate void DrawPartyMemberActiveType(float x, float y, byte mem, byte a4, byte a5, byte a6, byte a7, float a8, float a9, float a10, float a11, float a12, float a13);

        private int partyMemberLimit = 64;
        private int partyListOffset = 0;

        private string GetPersonaCount_SIG = "48 8D 85 ?? ?? ?? ?? 48 03 C1 F6 00 ?? 48 0F 45 D0";

        private unsafe DatUnit* _extendedPartyMembers = null;
        public unsafe PartyMember(TestModContext context, Dictionary<string, ModuleBase<TestModContext>> modules) : base(context, modules)
        {
            _context._utils.SigScan(TaskSchedulerLoop_SIG, "TaskSchedulerLoop", _context._utils.GetDirectAddress,
                addr => _taskSchedulerLoop = _context._utils.MakeHooker<TaskSchedulerLoop>(TaskSchedulerLoopImpl, addr));
            _context._redirectorApi.AddTargetRaw("gDatGlobal", sizeof(DatUnit) * 11, GetPersonaCount_SIG,
               x => (nuint)(_context._baseAddress + *(uint*)(_context._utils.GetDirectAddress(x) + 0x3) - 0x44));

            _context._utils.SigScan(MakeActivePartyMemberList_SIG, "MakeActivePartyMemberList", _context._utils.GetDirectAddress,
                addr => _makeActivePartyMemberList = _context._utils.MakeHooker<MakeActivePartyMemberList>(MakeActivePartyMemberListImpl, addr));
            _context._utils.SigScan(ChangePartyMemberSelected_SIG, "ChangePartyMemberSelected", _context._utils.GetDirectAddress,
                addr => _changePartyMemberSelected = _context._utils.MakeHooker<ChangePartyMemberSelected>(ChangePartyMemberSelectedImpl, addr));
            _context._utils.SigScan(DrawPartyMemberActiveType_SIG, "DrawPartyMemberActiveType", _context._utils.GetDirectAddress,
                addr => _drawPartyMemberActiveType = _context._utils.MakeHooker<DrawPartyMemberActiveType>(DrawPartyMemberActiveTypeImpl, addr));
        }

        public override void Register()
        {

        }

        public unsafe void TaskSchedulerLoopImpl(float delta, char a2)
        {
            if ((GetAsyncKeyState(116) & 0x1) != 0 && _extendedPartyMembers == null) // F5
            {
                _extendedPartyMembers = _context._redirectorApi.MoveGlobal<DatUnit>(null, "gDatGlobal", 64);
                var rng = new Random();
                for (int i = 0xb; i < 0x40; i++)
                {
                    (_extendedPartyMembers + i)->currHP = (uint)rng.Next(400, 500);
                    (_extendedPartyMembers + i)->currSP = (uint)rng.Next(200, 250);
                    (_extendedPartyMembers + i)->GetPersona(0)->ID = (ushort)(i - 6);
                    (_extendedPartyMembers + i)->GetPersona(0)->level = 60;
                }
            }
            _taskSchedulerLoop.OriginalFunction(delta, a2);
        }

        public unsafe int MakeActivePartyMemberListImpl(ushort* partyMembers)
        {
            for (int i = 1; i < 11; i++)
                partyMembers[i - 1] = (ushort)i;
            return 10;
        }

        public unsafe byte ChangePartyMemberSelectedImpl(CampStatsSubmenu* statsSubmenu, int type, int target)
        {
            _context._utils.Log($"{target}, {statsSubmenu->GetPartyMember(statsSubmenu->partyMemberCount - 1)}");
            if (target == statsSubmenu->partyMemberCount - 1)
            {
                // reset to bottom
                if (statsSubmenu->currentlyHighlighted == 0)
                {
                    for (int i = 0; i < statsSubmenu->partyMemberCount; i++)
                        statsSubmenu->SetPartyMember(i, (ushort)(partyMemberLimit - 10 + i + 1));
                }
                else if (statsSubmenu->GetPartyMember(statsSubmenu->partyMemberCount - 1) < partyMemberLimit)
                { // scroll down
                    target--;
                    for (int i = 0; i < statsSubmenu->partyMemberCount; i++)
                        statsSubmenu->SetPartyMember(i, (ushort)(statsSubmenu->GetPartyMember(i) + 1));
                }
            }
            if (target == 0)
            {
                // reset to top
                if (statsSubmenu->currentlyHighlighted == statsSubmenu->partyMemberCount - 1)
                {
                    for (int i = 0; i < statsSubmenu->partyMemberCount; i++)
                        statsSubmenu->SetPartyMember(i, (ushort)(i + 1));
                }
                else if (statsSubmenu->GetPartyMember(statsSubmenu->partyMemberCount - 1) > 10)
                { // scroll up
                    target++;
                    for (int i = 0; i < statsSubmenu->partyMemberCount; i++)
                        statsSubmenu->SetPartyMember(i, (ushort)(statsSubmenu->GetPartyMember(i) - 1));
                }
            }
            return _changePartyMemberSelected.OriginalFunction(statsSubmenu, type, target);
        }

        public unsafe void DrawPartyMemberActiveTypeImpl(float x, float y, byte mem, byte a4, byte a5, byte a6, byte a7, float a8, float a9, float a10, float a11, float a12, float a13)
        {

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

        [StructLayout(LayoutKind.Explicit, Size = 0x2178)]
        public unsafe struct CampStatsSubmenu
        {
            [FieldOffset(0x32)] public ushort currentlyHighlighted;
            // 0x1f0: ushort[10] partyMembers;
            [FieldOffset(0x204)] public ushort partyMemberCount;
            public ushort GetPartyMember(int i)
            {
                fixed (CampStatsSubmenu* self = &this)
                {
                    return *(ushort*)((nint)self + 0x1f0 + sizeof(ushort) * i);
                }
            }
            public void SetPartyMember(int i, ushort v)
            {
                fixed (CampStatsSubmenu* self = &this)
                {
                    *(ushort*)((nint)self + 0x1f0 + sizeof(ushort) * i) = v;
                }
            }
        }

        [DllImport("user32.dll")]
        public static extern ushort GetAsyncKeyState(int vKey);
    }
}
