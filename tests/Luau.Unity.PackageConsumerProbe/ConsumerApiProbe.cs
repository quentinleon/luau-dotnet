using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Luau;
using Luau.Unity;

namespace Luau.Unity.PackageConsumerProbe
{
    internal static class ConsumerApiProbe
    {
        public static LuauUnityOptions CreateOptions(Action<LuauState> configureHostApis)
        {
            return new LuauUnityOptions
            {
                CaptureUnitySynchronizationContext = false,
                ModuleMap = new LuauModuleMap(new Dictionary<string, byte[]>()),
                ConfigureHostApis = configureHostApis,
                Log = _ => { },
            };
        }

        public static ValueTask<LuauModuleBundle> CompileModulesAsync(
            LuauModuleMap moduleMap,
            CancellationToken cancellationToken)
        {
            return LuauUnity.CompileModuleBundleAsync(
                moduleMap,
                cancellationToken: cancellationToken);
        }
    }
}
