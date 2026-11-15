using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 框架层资源缓存：把"地址 → 已加载句柄"缓存起来，并用引用计数管理释放。
    ///
    /// 为什么缓存放在框架层，而不是放进每个 IAssetLoader 实现？
    /// 因为"同一个资源只加载一次、关闭界面时成对释放"是界面框架的职责，
    /// 不是 Resources/YooAsset 的职责。放在这里，以后换任何加载器都自动获得缓存能力，
    /// 不需要在每个加载器里重复写一遍字典和计数。
    ///
    /// 引用计数怎么算？
    /// 每次 LoadAsync 成功返回，计数 +1；每次对返回的句柄 Release()，计数 -1。
    /// 计数归零时：
    ///   - ReleaseOnClose（默认）：调用底层句柄 Release() 并移除缓存；
    ///   - KeepAlive：保留缓存，下次打开直接复用。
    ///
    /// 注意：这里不负责"取消底层加载"。Resources.LoadAsync / YooAsset 的加载请求
    /// 一旦发出，取消等待只是让调用方提前返回，底层加载仍会完成——
    /// 如果还有别的等待者，完成后句柄写回缓存供它们复用；
    /// 如果已经没人要（等待者计数归零），完成后立刻释放，避免留下孤儿条目。
    /// </summary>
    internal sealed class AssetCache
    {
        private readonly IAssetLoader _loader;

        /// <summary>已加载完成的资源，按地址索引。</summary>
        private readonly Dictionary<string, Entry> _dicEntries = new Dictionary<string, Entry>();

        /// <summary>正在加载中的资源。同一个地址只会有一个 Task，避免重复加载。</summary>
        private readonly Dictionary<string, Task<IAssetHandle>> _dicLoading =
            new Dictionary<string, Task<IAssetHandle>>();

        #region 初始化

        /// <summary>
        /// 绑定底层加载器并创建缓存；loader 不能为 null，所有实际加载都经由它发起。
        /// </summary>
        public AssetCache(IAssetLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>缓存里已有资源的数量（调试面板用）。</summary>
        internal int Count
        {
            get { return _dicEntries.Count; }
        }

        /// <summary>正在加载中的资源数量（调试面板用）。</summary>
        internal int LoadingCount
        {
            get { return _dicLoading.Count; }
        }

        #endregion

        #region 加载


        /// <summary>
        /// 加载资源，并让缓存持有一个引用。
        /// 调用方拿到句柄后，必须在使用结束时调用 handle.Release()。
        /// </summary>
        public async Task<IAssetHandle> LoadAsync(
            string address,
            AssetCachePolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("资源地址不能为空", nameof(address));

            cancellationToken.ThrowIfCancellationRequested();

            // 1. 缓存命中：直接复用，计数 +1。
            if (_dicEntries.TryGetValue(address, out Entry cached)
                && cached.Handle != null
                && cached.Handle.IsValid)
            {
                cached.RefCount++;

                if (policy == AssetCachePolicy.KeepAlive)
                    cached.Pin = true;

                return new CachedHandle(this, address, cached.Handle);
            }

            // 2. 正在加载：等同一个 Task，避免重复加载。
            if (_dicLoading.TryGetValue(address, out Task<IAssetHandle> loadingTask))
            {
                if (!_dicEntries.TryGetValue(address, out Entry loadingEntry))
                {
                    // 加载期间缓存被清空/换了加载器（ReleaseAll / Initialize）：
                    // 这次加载的结果已经不归缓存所有，等它结束再按原语义抛明确异常。
                    await TaskWait.WithCancellation(loadingTask, cancellationToken);
                    throw new InvalidOperationException($"[CandyUI] 资源缓存条目已失效：{address}");
                }

                // 之前最后一个等待者取消过、条目被标记成"加载完成后释放"；
                // 现在又有人来等，就撤销这个标记：加载结果要留下来给当前等待者用。
                loadingEntry.ReleaseWhenLoaded = false;
                loadingEntry.Waiters++;

                IAssetHandle loaded;
                try
                {
                    loaded = await TaskWait.WithCancellation(loadingTask, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    AbandonWaiter(address, loadingEntry);
                    throw;
                }
                catch
                {
                    // 加载失败：条目已经被 LoadAndStoreAsync 的 catch 移除，只需退掉等待记录。
                    loadingEntry.Waiters--;
                    throw;
                }

                loadingEntry.Waiters--;

                if (!_dicEntries.TryGetValue(address, out Entry current) || current.Handle == null)
                    throw new InvalidOperationException($"[CandyUI] 资源缓存条目已失效：{address}");

                current.RefCount++;

                if (policy == AssetCachePolicy.KeepAlive)
                    current.Pin = true;

                return new CachedHandle(this, address, loaded);
            }

            // 3. 首次加载：先建条目占位，再发起加载。
            Entry entry = new Entry
            {
                Pin = policy == AssetCachePolicy.KeepAlive,

                // 发起者自己就是一个等待者，必须先记账再启动加载，
                // 否则加载同步完成时"等待者数量"会对不上。
                Waiters = 1,
            };
            _dicEntries[address] = entry;

            Task<IAssetHandle> task = LoadAndStoreAsync(address, entry);
            _dicLoading[address] = task;

            // 关键约束：_dicLoading 里不能留下"已经完成的 Task"。
            // 同步完成的加载器会在 LoadAndStoreAsync 内部同步跑完 finally、把地址从
            // _dicLoading 移除；而上面这行赋值发生在它之后，会把这条已完成的登记又写回去。
            // 一旦留下，条目释放到 0 之后再次 LoadAsync 会命中这条陈旧登记，
            // 然后因为缓存条目已不存在而抛"资源缓存条目已失效"。
            if (task.IsCompleted)
                TryRemoveLoading(address, task);

            IAssetHandle handle;
            try
            {
                handle = await TaskWait.WithCancellation(task, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 调用方放弃等待。底层加载不会中断（共享 Task 不绑定 token），
                // 但只有"已经没有别的等待者"才能标记加载完成后释放，
                // 否则会把并发等待者还要用的句柄提前释放掉。
                AbandonWaiter(address, entry);
                throw;
            }
            catch
            {
                // 加载失败：条目已经由 LoadAndStoreAsync 的 catch 移除，只需退掉等待记录。
                entry.Waiters--;
                throw;
            }

            entry.Waiters--;
            entry.RefCount++;

            return new CachedHandle(this, address, handle);
        }

        #endregion

        #region 释放


        /// <summary>释放一次引用（由 CachedHandle.Release 调用）。</summary>
        public void ReleaseReference(string address)
        {
            if (!_dicEntries.TryGetValue(address, out Entry entry))
                return; // 已经被释放过/已清空，忽略重复释放。

            entry.RefCount--;
            if (entry.RefCount > 0)
                return;

            if (entry.Pin)
            {
                // KeepAlive：引用归零也不卸载，保持热状态。
                entry.RefCount = 0;
                return;
            }

            _dicEntries.Remove(address);

            if (entry.Handle != null)
                entry.Handle.Release();
            else
                entry.ReleaseWhenLoaded = true;
        }

        /// <summary>释放所有缓存（切换加载器、退出游戏时用）。</summary>
        public void ReleaseAll()
        {
            List<Entry> entries = new List<Entry>(_dicEntries.Values);
            _dicEntries.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (entry.Handle != null)
                    entry.Handle.Release();
                else
                    entry.ReleaseWhenLoaded = true;
            }
        }

        #endregion

        #region 内部实现


        /// <summary>
        /// 一个等待者取消时调用：先退掉它的"等待"记账，再判断这个加载结果还有没有人要。
        /// 关键约束：只有"等待者归零、没有固定引用、也没有调用方引用"时才置 ReleaseWhenLoaded。
        /// 否则先取消的调用方会把并发等待者仍然需要的加载结果标记成"完成后释放"：
        /// 加载完成时句柄被直接释放、不写回条目，后者恢复等待时就会抛"资源缓存条目已失效"。
        /// </summary>
        private void AbandonWaiter(string address, Entry entry)
        {
            entry.Waiters--;

            if (entry.Waiters > 0 || entry.Pin || entry.RefCount > 0)
                return;

            entry.ReleaseWhenLoaded = true;

            // 取消发生在加载完成之后（或几乎同时）：句柄已经挂上来了，这里立刻释放。
            // 先确认条目还是它，避免释放已经被 ReleaseAll / 新一轮加载接管的句柄。
            if (entry.Handle != null
                && _dicEntries.TryGetValue(address, out Entry current)
                && ReferenceEquals(current, entry))
            {
                _dicEntries.Remove(address);
                entry.Handle.Release();
                entry.Handle = null;
            }
        }

        /// <summary>
        /// 按引用移除"加载中"登记：只移除仍然是同一条 Task 的登记，避免误删之后发起的加载。
        /// </summary>
        private void TryRemoveLoading(string address, Task<IAssetHandle> task)
        {
            if (_dicLoading.TryGetValue(address, out Task<IAssetHandle> current)
                && ReferenceEquals(current, task))
            {
                _dicLoading.Remove(address);
            }
        }


        /// <summary>
        /// 真正调用底层加载器，并把结果写进缓存条目。
        /// 单独的 Task 负责"加载 + 存储"，这样调用方的取消不会把缓存写坏。
        /// </summary>
        private async Task<IAssetHandle> LoadAndStoreAsync(string address, Entry entry)
        {
            try
            {
                // 传 CancellationToken.None：底层加载是共享的，不能因为某一个调用方取消就中断。
                IAssetHandle handle = await _loader.LoadAssetAsync(address, CancellationToken.None);

                if (handle == null || !handle.IsValid)
                {
                    if (handle != null)
                        handle.Release();

                    throw new InvalidOperationException($"[CandyUI] 资源加载器返回了无效句柄：{address}");
                }

                if (!_dicEntries.TryGetValue(address, out Entry current)
                    || !ReferenceEquals(current, entry)
                    || entry.ReleaseWhenLoaded)
                {
                    // 加载期间缓存被清空/换了加载器，或者调用方已提前放弃：直接释放，避免泄漏。
                    handle.Release();

                    // 没人要的条目不能留在缓存表里：否则 Count 会多出一条句柄为 null 的记录，
                    // 下一次 LoadAsync 还得先绕过它才能重新加载。
                    if (ReferenceEquals(current, entry))
                        _dicEntries.Remove(address);

                    return handle;
                }

                entry.Handle = handle;
                return handle;
            }
            catch
            {
                if (_dicEntries.TryGetValue(address, out Entry current) && ReferenceEquals(current, entry))
                    _dicEntries.Remove(address);

                throw;
            }
            finally
            {
                _dicLoading.Remove(address);
            }
        }

        #endregion

        #region 嵌套类型


        /// <summary>缓存条目的内部状态。</summary>
        private sealed class Entry
        {
            public IAssetHandle Handle;
            public int RefCount;
            public bool Pin;
            public bool ReleaseWhenLoaded;

            /// <summary>
            /// 正在等这个地址加载完成的调用方数量（含发起者）。
            /// 取消只退掉自己的等待；归零才能把加载结果标记为"完成后释放"。
            /// </summary>
            public int Waiters;
        }

        /// <summary>
        /// 返回给调用方的句柄：包一层，把 Release() 变成"减少缓存引用计数"。
        /// 底层加载器的原始句柄只由缓存负责释放一次，避免重复 Release。
        /// </summary>
        private sealed class CachedHandle : IAssetHandle
        {
            private readonly AssetCache _cache;
            private readonly string _address;
            private readonly IAssetHandle _inner;
            private bool _released;

            /// <summary>
            /// 由 AssetCache 为每个调用方单独创建；同一底层句柄可能被多个包装共享，释放只递减计数。
            /// </summary>
            public CachedHandle(AssetCache cache, string address, IAssetHandle inner)
            {
                _cache = cache;
                _address = address;
                _inner = inner;
            }

            public UnityEngine.Object Asset
            {
                get { return _inner.Asset; }
            }

            public bool IsValid
            {
                get { return !_released && _inner.IsValid; }
            }

            /// <summary>
            /// 释放一次引用：重复调用无副作用，真正卸载由 AssetCache 在计数归零后决定。
            /// </summary>
            public void Release()
            {
                if (_released)
                    return;

                _released = true;
                _cache.ReleaseReference(_address);
            }
        }

        #endregion

    }
}
