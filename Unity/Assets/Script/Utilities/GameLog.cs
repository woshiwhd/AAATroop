using UnityEngine;

namespace Script.Utilities
{
    /// <summary>
    /// 统一日志入口，通过 EnableLogs 控制是否输出，避免与 Script.Debug 命名空间冲突。
    /// </summary>
    public static class GameLog
    {
        /// <summary>全局开关：false 时 Log/LogWarning/LogError/LogException 均不输出。</summary>
        public static bool EnableLogs = true;

        public static void Log(object message)
        {
            if (EnableLogs) UnityEngine.Debug.Log(message);
        }

        public static void LogWarning(object message)
        {
            if (EnableLogs) UnityEngine.Debug.LogWarning(message);
        }

        public static void LogError(object message)
        {
            if (EnableLogs) UnityEngine.Debug.LogError(message);
        }

        public static void LogException(System.Exception ex)
        {
            if (EnableLogs) UnityEngine.Debug.LogException(ex);
        }
    }
}
