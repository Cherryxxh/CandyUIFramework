using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 一个已加载资源的句柄，负责"释放"。
    ///
    /// 为什么不像 Resources.Load 那样直接返回 Object？
    /// 因为 YooAsset/Addressables 这类系统需要成对调用 Load/Release 来维护引用计数。
    /// 如果框架只拿 Object，就没法在关闭界面时正确 Release，资源会一直留在内存里。
    /// 句柄就是"借条"：谁借的，谁负责还。
    ///
    /// 注意：面板拿到的句柄通常由 AssetCache 包了一层，
    /// Release() 减少的是"缓存引用计数"，不是直接卸载底层资源；
    /// 计数归零且策略不是 KeepAlive 时，才由缓存调用底层句柄的 Release()。
    /// </summary>
    public interface IAssetHandle
    {
        /// <summary>加载到的资源（面板场景里是 Prefab）。</summary>
        Object Asset { get; }

        /// <summary>资源是否还有效（可能被外部卸载）。</summary>
        bool IsValid { get; }

        /// <summary>释放这个句柄。同一个句柄只应释放一次；对缓存返回的句柄，这只是引用计数减一，归零后由缓存决定是否真正卸载。</summary>
        void Release();
    }
}
