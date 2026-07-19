using System;
using System.Collections.Generic;
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
    }
}
