using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace zebra
{
    public static class ZebraLogger
    {
        [Conditional("ZEBRA_ENABLE_LOG")]
        public static void Log(in string msg)
        {
            Debug.Log(msg);
        }


        [Conditional("ZEBRA_ENABLE_LOG")]
        public static void LogError(in string msg)
        {
            Debug.LogError(msg);
        }


        [Conditional("ZEBRA_ENABLE_LOG")]
        public static void LogWarning(string msg)
        {
            Debug.LogWarning(msg);
        }
    }
}