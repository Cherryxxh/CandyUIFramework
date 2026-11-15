// 职责：测试用 IAssetLoader 实现，按 [UIPanel] 登记表现场创建面板 Prefab，并可手动控制加载完成时机。
// 依赖：测试程序集里的面板类型，以及 CandyUI 的 IAssetLoader / IAssetHandle 契约。
// 限制：仅在 EditMode 测试中使用，不查找 Resources / YooAsset 里的真实资源。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CandyUI.Tests
{
    /// <summary>
    /// 测试用资源加载器：不依赖 Resources / YooAsset 资源，按测试面板上的 [UIPanel] 地址
    /// 现场创建一个带 RectTransform + 面板组件的 GameObject 充当 Prefab。
    /// 默认 Immediate 模式立即完成（整条异步链同步执行）；ManualMode 下挂起等待测试代码手动完成，
    /// 用于构造"加载中 / 排队 / 取消"等时序场景。
    /// </summary>
    public sealed class FakeAssetLoader : IAssetLoader
    {
        private readonly Dictionary<string, Type> _dicAddressToPanelType = new Dictionary<string, Type>();
        private readonly Dictionary<string, int> _dicLoadCount = new Dictionary<string, int>();
        private readonly List<FakeAssetHandle> _listHandles = new List<FakeAssetHandle>();
        private readonly Queue<PendingLoad> _queuePending = new Queue<PendingLoad>();

        /// <summary>
        /// 扫描测试程序集里带 [UIPanel] 的面板类型，建立“资源地址 → 面板类型”映射。
        /// </summary>
        /// <param name="panelAssembly">用于扫描 [UIPanel] 测试面板的测试程序集，通常是 typeof(UITestBase).Assembly。</param>
        public FakeAssetLoader(Assembly panelAssembly)
        {
            if (panelAssembly == null) throw new ArgumentNullException(nameof(panelAssembly));

            foreach (Type type in panelAssembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(UIPanel).IsAssignableFrom(type)) continue;
                UIPanelAttribute attribute = (UIPanelAttribute)Attribute.GetCustomAttribute(type, typeof(UIPanelAttribute), false);
                if (attribute == null) continue;
                if (_dicAddressToPanelType.ContainsKey(attribute.PrefabAddress))
                    throw new InvalidOperationException($"测试面板资源地址重复：{attribute.PrefabAddress}（{type.Name}）");
                _dicAddressToPanelType.Add(attribute.PrefabAddress, type);
            }
        }

        /// <summary>true 时 LoadAssetAsync 返回未完成的 Task，直到测试调用 CompleteNext / CompleteAll。</summary>
        public bool ManualMode { get; set; }

        /// <summary>挂起中的加载请求数量（ManualMode 下有效）。</summary>
        public int PendingCount { get { return _queuePending.Count; } }

        /// <summary>
        /// 返回指定地址累计触发底层加载的次数，用于断言缓存是否命中。
        /// </summary>
        public int GetLoadCount(string address)
        {
            return _dicLoadCount.TryGetValue(address, out int count) ? count : 0;
        }

        /// <summary>
        /// 返回指定地址累计创建出的 Prefab 数量，用于断言是否复用了实例。
        /// </summary>
        public int GetCreatedCount(string address)
        {
            int count = 0;
            foreach (FakeAssetHandle handle in _listHandles)
            {
                if (handle.Address == address) count++;
            }
            return count;
        }

        /// <summary>
        /// 返回指定地址累计释放次数，用于断言引用计数归零才真正卸载。
        /// </summary>
        public int GetReleaseCount(string address)
        {
            int count = 0;
            foreach (FakeAssetHandle handle in _listHandles)
            {
                if (handle.Address == address) count += handle.ReleaseCount;
            }
            return count;
        }

        /// <summary>
        /// 按地址创建面板 Prefab；ManualMode 下挂起等待测试放行，已取消或未登记地址直接返回失败 Task。
        /// </summary>
        public Task<IAssetHandle> LoadAssetAsync(string loadAddress, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<IAssetHandle>(cancellationToken);
            }

            if (!_dicAddressToPanelType.TryGetValue(loadAddress, out Type panelType))
            {
                return Task.FromException<IAssetHandle>(
                    new InvalidOperationException($"[FakeAssetLoader] 未登记的测试面板资源地址：{loadAddress}"));
            }

            _dicLoadCount.TryGetValue(loadAddress, out int count);
            _dicLoadCount[loadAddress] = count + 1;

            if (ManualMode)
            {
                PendingLoad pending = new PendingLoad(loadAddress, panelType);
                _queuePending.Enqueue(pending);
                return pending.Completion.Task;
            }

            return Task.FromResult<IAssetHandle>(CreateHandle(loadAddress, panelType));
        }

        /// <summary>手动模式：完成最早发起的一次挂起加载。</summary>
        public FakeAssetHandle CompleteNext()
        {
            if (_queuePending.Count == 0) throw new InvalidOperationException("[FakeAssetLoader] 没有挂起的加载请求");
            PendingLoad pending = _queuePending.Dequeue();
            FakeAssetHandle handle = CreateHandle(pending.Address, pending.PanelType);
            pending.Completion.TrySetResult(handle);
            return handle;
        }

        /// <summary>
        /// 手动模式：按发起顺序完成所有挂起加载，用于一次放行多个排队请求。
        /// </summary>
        public void CompleteAll()
        {
            while (_queuePending.Count > 0) CompleteNext();
        }

        /// <summary>清理测试中造出的临时 Prefab 对象，避免跨用例残留。</summary>
        public void DestroyAllAssets()
        {
            foreach (FakeAssetHandle handle in _listHandles) handle.DestroyAssetNow();
            _listHandles.Clear();
            _queuePending.Clear();
        }

        /// <summary>
        /// 创建带 RectTransform 和面板组件的临时 Prefab，并登记到句柄列表以便统一清理。
        /// </summary>
        private FakeAssetHandle CreateHandle(string address, Type panelType)
        {
            GameObject prefab = new GameObject("TestPrefab_" + panelType.Name, typeof(RectTransform), panelType);
            FakeAssetHandle handle = new FakeAssetHandle(address, prefab);
            _listHandles.Add(handle);
            return handle;
        }

        private sealed class PendingLoad
        {
            public readonly string Address;
            public readonly Type PanelType;
            public readonly TaskCompletionSource<IAssetHandle> Completion = new TaskCompletionSource<IAssetHandle>();

            /// <summary>
            /// 记录一次挂起加载的地址与面板类型，并持有恢复异步链路的 TaskCompletionSource。
            /// </summary>
            public PendingLoad(string address, Type panelType)
            {
                Address = address;
                PanelType = panelType;
            }
        }
    }

    /// <summary>测试用资源句柄：记录释放次数，便于断言引用计数与卸载时机。</summary>
    public sealed class FakeAssetHandle : IAssetHandle
    {
        private bool _released;

        /// <summary>
        /// 包装一个测试资源对象并记录地址；是否随引用释放而销毁由 Release 控制。
        /// </summary>
        public FakeAssetHandle(string address, GameObject asset)
        {
            Address = address;
            Asset = asset;
        }

        /// <summary>资源地址，与 [UIPanel] 中登记的地址一致。</summary>
        public string Address { get; }

        /// <summary>测试用的临时 Prefab 对象。</summary>
        public UnityEngine.Object Asset { get; }

        /// <summary>累计调用 Release 的次数（重复释放也会计数）。</summary>
        public int ReleaseCount { get; private set; }

        /// <summary>是否仍有效：未被释放且底层资源对象未销毁。</summary>
        public bool IsValid { get { return !_released && Asset != null; } }

        /// <summary>
        /// 记录一次释放；首次释放时销毁测试资源，重复释放只累加计数。
        /// </summary>
        public void Release()
        {
            ReleaseCount++;
            if (_released) return;
            _released = true;
            DestroyAssetNow();
        }

        /// <summary>
        /// 立即销毁底层测试对象，供用例清理或 TearDown 统一回收临时 Prefab。
        /// </summary>
        public void DestroyAssetNow()
        {
            if (Asset != null) UnityEngine.Object.DestroyImmediate(Asset);
        }
    }
}
