using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using riri.commonmodutils;
using riri.globalredirector.Configuration;
using SharedScans.Interfaces;

namespace riri.globalredirector
{
    public class RedirectorContext : Context
    {
        public new Config _config { get; set; }
        public string _fileName { get; private set; }
        public int _processModuleSize { get; private set; }
        public RedirectorContext(long baseAddress, IConfigurable config, ILogger logger, IStartupScanner startupScanner, IReloadedHooks hooks,
            string modLocation, Utils utils, Memory memory, ISharedScans sharedScans, string processFileName, int processModuleSize)
            : base(baseAddress, config, logger, startupScanner, hooks, modLocation, utils, memory, sharedScans)
        {
            _config = (Config)config;
            _fileName = processFileName;
            _processModuleSize = processModuleSize;
        }
        public override void OnConfigUpdated(IConfigurable newConfig) => _config = (Config)newConfig;
    }
}