using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 资源加载器接口 —— 整个框架"可替换资源系统"的关键抽象。
    ///
    /// 为什么需要这层接口？
    /// 界面管理层只应该依赖"给我一个 Prefab"，不应该知道它是 Resources、AssetBundle、
    /// Addressables 还是 YooAsset 加载的。否则以后从 Resources 换成 YooAsset，
    /// UIManager 里会到处是 if/else，插件式替换就无从谈起。
    ///
    /// 依赖方向：
    ///   UIManager  --->  IAssetLoader  <---  ResourcesAssetLoader
    ///                                  <---  YooAssetLoader（以后新增，不改 UIManager）
    ///
    /// 切换方式（业务入口只改一行）：
    ///   UIManager.InitializeAssetLoader(new ResourcesAssetLoader());   // 现在
    ///   UIManager.InitializeAssetLoader(new YooAssetLoader(...));      // 以后
    ///
    /// 为什么返回 Task 而不是 IEnumerator？
    /// 因为界面打开经常需要 await 串联（先加载、再初始化、再显示），
    /// Task 写起来更直观，也方便以后接入任何异步资源系统。
    /// Unity 2022 支持 .NET Standard 2.1，Task 可以放心使用。
    ///
    /// 关于取消：cancellationToken 是"取消等待"，不是"取消加载"。
    /// Resources / YooAsset 的请求一旦发出通常无法中断，所以实现类只需要：
    ///   1. 发起请求前检查一次 IsCancellationRequested；
    ///   2. 等待期间允许调用方提前返回。
    /// 底层加载仍会完成，加载结果留在缓存里，不会浪费。
    /// </summary>
    public interface IAssetLoader
    {
        /// <summary>
        /// 按地址异步加载资源。地址的具体含义由实现类决定。
        /// 失败时应抛出异常（而不是返回 null），这样调用方能按需把错误抛给业务层。
        /// </summary>
        Task<IAssetHandle> LoadAssetAsync(string loadAddress, CancellationToken cancellationToken = default);
    }
}
