// 职责：把 YooAsset 的 AssetHandle 适配成 CandyUI 的 IAssetHandle。
// 依赖：YooAsset 3.x 的 AssetHandle；Release 由 AssetCache 在引用计数归零时调用。
// 限制：只做一层转发，不负责引用计数与资源生命周期决策。
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace CandyUI.YooAsset
{
    /// <summary>
    /// CandyUI 的资源句柄在 YooAsset 上的实现。
    /// 包装一个 YooAsset 的 AssetHandle：
    /// - Asset：YooAsset 加载出来的资源对象；
    /// - IsValid：句柄是否还有效（底层句柄被 Release 后即为 false）；
    /// - Release：归还一次引用，AssetCache 的引用计数归零时才会调用到这里。
    /// </summary>
    public sealed class YooAssetHandle : IAssetHandle
    {
        private readonly AssetHandle mHandle;
        private Object mAsset;

        /// <summary>
        /// 包装 YooAsset 句柄并缓存资源对象；handle 为空时保留空引用，由使用者自行判空。
        /// </summary>
        public YooAssetHandle(AssetHandle handle)
        {
            mHandle = handle;
            mAsset = handle != null ? handle.AssetObject : null;
        }

        /// <summary>加载出来的资源对象（这里是 UI Prefab）。</summary>
        public Object Asset => mAsset;

        /// <summary>句柄是否还有效；释放后不应再使用。</summary>
        public bool IsValid => mHandle != null && mHandle.IsValid;

        /// <summary>释放一次引用；由 AssetCache 在引用计数归零时调用。</summary>
        public void Release()
        {
            mAsset = null;
            if (mHandle != null && mHandle.IsValid)
                mHandle.Release();
        }
    }
}
