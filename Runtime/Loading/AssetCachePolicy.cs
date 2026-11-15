namespace CandyUI
{
    /// <summary>
    /// 面板 Prefab 的缓存策略：关闭界面时，它的资源要不要留在内存里。
    ///
    /// 什么时候用 KeepAlive？
    /// - 主城/大厅这类"玩家 90% 时间都在"的界面；
    /// - 打开频率极高、加载时间又长的界面（背包、角色面板）。
    /// 什么时候用 ReleaseOnClose（默认）？
    /// - 大多数一次性界面（活动页、公告、设置）；
    /// - 内存紧张的项目。
    ///
    /// 注意：KeepAlive 只是"引用计数归零后不卸载"，不会阻止手动释放。
    /// 切换加载器（InitializeAssetLoader）或调用 CloseAll 之外的清理时，
    /// 仍然可以强制释放全部缓存。
    /// </summary>
    public enum AssetCachePolicy
    {
        /// <summary>关闭界面时释放引用；计数归零就卸载（默认，省内存）。</summary>
        ReleaseOnClose = 0,

        /// <summary>资源常驻内存：引用计数归零也不卸载，下次打开直接复用。</summary>
        KeepAlive = 1,
    }
}
