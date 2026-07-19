using Luau;

namespace Luau.Unity.PackageConsumerProbe
{
    [LuauLibrary("consumerProbe")]
    internal sealed partial class ConsumerGeneratedLibrary
    {
        [LuauMember("addOne")]
        public static long AddOne(long value)
        {
            return value + 1;
        }

        public static ILuauLibrary CreateGeneratedLibrary()
        {
            return new ConsumerGeneratedLibrary();
        }
    }
}
