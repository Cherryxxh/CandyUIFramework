using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 框架统一的日志出口。
    ///
    /// 为什么不用裸 Debug.Log？
    /// 1. 统一前缀 "[CandyUI]"，方便在 Console 里过滤，也方便 Demo 的调试面板抓取；
    /// 2. 可以一键关掉（UIManager.DebugLogEnabled = false），正式包不会刷屏；
    /// 3. 以后要接自己的日志系统（上报/文件），只改这一处。
    ///
    /// 这些日志故意写得比较啰嗦，目的是让人能"看着 Console 理解框架在干什么"：
    /// 加载了哪个 Prefab、缓存有没有命中、窗口压栈/出栈、弹窗排队第几个。
    /// </summary>
    internal static class CandyUILog
    {
        /// <summary>是否输出框架日志。默认开启，Demo 和排查问题很有用。</summary>
        internal static bool Enabled = true;

        /// <summary>
        /// 输出普通日志；受 Enabled 开关控制，正式包可整体关掉。
        /// </summary>
        internal static void Info(string message)
        {
            if (Enabled)
                Debug.Log("[CandyUI] " + message);
        }

        /// <summary>
        /// 输出警告日志，用于可恢复的配置/使用问题；同样受 Enabled 控制。
        /// </summary>
        internal static void Warn(string message)
        {
            if (Enabled)
                Debug.LogWarning("[CandyUI] " + message);
        }
    }
}
