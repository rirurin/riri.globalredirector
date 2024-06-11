using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using riri.commonmodutils;
using riri.globalredirector.Interfaces;
using riri.globalredirector.testmod.Configuration;
using SharedScans.Interfaces;

namespace riri.globalredirector.testmod
{
    public class TestModContext : Context
    {
        public new Config _config { get; set; }
        public IRedirectorApi _redirectorApi { get; private set; }
        public TestModContext(long baseAddress, IConfigurable config, ILogger logger, IStartupScanner startupScanner, IReloadedHooks hooks,
            string modLocation, Utils utils, Memory memory, ISharedScans sharedScans, IRedirectorApi redirectorApi)
            : base(baseAddress, config, logger, startupScanner, hooks, modLocation, utils, memory, sharedScans)
        {
            _config = (Config)config;
            _redirectorApi = redirectorApi;
        }
        public override void OnConfigUpdated(IConfigurable newConfig) => _config = (Config)newConfig;
    }
}
