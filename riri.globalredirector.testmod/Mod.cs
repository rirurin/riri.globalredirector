using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using riri.commonmodutils;
using riri.globalredirector.Interfaces;
using riri.globalredirector.testmod.Configuration;
using riri.globalredirector.testmod.Template;
using SharedScans.Interfaces;
using System.Diagnostics;

namespace riri.globalredirector.testmod
{
    public class Mod : ModBase
    {
        private readonly IModLoader _modLoader;
        private readonly IReloadedHooks? _hooks;
        private readonly ILogger _logger;
        private readonly IMod _owner;
        private Config _configuration;
        private readonly IModConfig _modConfig;

        private TestModContext _context;
        private ModuleRuntime<TestModContext> _runtime;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            var mainModule = Process.GetCurrentProcess().MainModule;
            if (mainModule == null) throw new Exception($"[{_modConfig.ModName}] Could not get main module");
            var baseAddress = mainModule.BaseAddress;
            if (_hooks == null) throw new Exception($"[{_modConfig.ModName}] Could not get controller for Reloaded hooks");
            var startupScanner = GetDependency<IStartupScanner>("Reloaded Startup Scanner");
            var sharedScans = GetDependency<ISharedScans>("Shared Scans");
            Utils utils = new(startupScanner, _logger, _hooks, baseAddress, "Party Member Test", System.Drawing.Color.PaleVioletRed, LogLevel.Information);
            var memory = new Memory();
            var redirector = GetDependency<IRedirectorApi>("Global Redirector");
            _context = new(baseAddress, _configuration, _logger, startupScanner, _hooks,
                _modLoader.GetDirectoryForModId(_modConfig.ModId), utils, memory, sharedScans, redirector);
            _runtime = new(_context);
            _runtime.RegisterModules();
        }

        private IControllerType GetDependency<IControllerType>(string modName) where IControllerType : class
        {
            var controller = _modLoader.GetController<IControllerType>();
            if (controller == null || !controller.TryGetTarget(out var target))
                throw new Exception($"[{_modConfig.ModName}] Could not get controller for \"{modName}\". This depedency is likely missing.");
            return target;
        }

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}