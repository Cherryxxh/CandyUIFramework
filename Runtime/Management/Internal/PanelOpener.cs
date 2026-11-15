using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 面板打开的在途状态（框架内部）：去重、等待者计数、最后一个等待者取消后的废弃判定与收尾调度。
    ///
    /// 不变量：
    /// 1. 同一类型同一时刻最多一个共享创建 Task；等待方等的是它，取消只放弃自己的等待；
    /// 2. 只有"最后一个等待者取消"才可能把一次打开标记为废弃；
    /// 3. 收尾判定只看两点：废弃标记仍在 && 等待者计数为 0。谁取消谁负责调度收尾，上层不补调用；
    /// 4. 共享 Task 从不因某个调用方取消而中断，所以收尾必须等它结束后再做。
    ///
    /// 它不认识面板注册表，也不实例化面板：标记被消费后通过构造时传入的回调通知 PanelService 去销毁。
    /// 依赖方向：PanelService -> PanelOpener（单向）。
    /// </summary>
    internal sealed class PanelOpener
    {
        /// <summary>按面板类型索引的在途打开；条目可能只剩等待者或废弃标记。</summary>
        private readonly Dictionary<Type, PanelOpenOperation> _dicOperations =
            new Dictionary<Type, PanelOpenOperation>();

        /// <summary>废弃标记被消费后调用：由 PanelService 强制关闭该类型的幽灵面板。</summary>
        private readonly Action<Type> _finalizeAbandoned;

        /// <summary>登记了共享创建 Task 的类型数量（调试快照的"加载中"）。</summary>
        private int _loadingCount;

        /// <summary>
        /// 接收废弃收尾回调：标记被消费后由 PanelService 强制关闭没人能拿到的幽灵面板。
        /// </summary>
        internal PanelOpener(Action<Type> finalizeAbandoned)
        {
            _finalizeAbandoned = finalizeAbandoned;
        }

        /// <summary>对外只读的“加载中”数量，供调试快照显示。</summary>
        internal int LoadingCount
        {
            get { return _loadingCount; }
        }

        /// <summary>重置全部在途状态（CloseAll / Reset 用）。</summary>
        internal void Reset()
        {
            // 必须逐个清状态再清字典：在途 Task 的续体仍持有 operation 引用，
            // 只清字典的话它结束时会按旧标记误收尾新一次打开。
            foreach (PanelOpenOperation operation in _dicOperations.Values)
            {
                operation.Task = null;
                operation.WaiterCount = 0;
                operation.IsAbandoned = false;
            }

            _dicOperations.Clear();
            _loadingCount = 0;
        }

        /// <summary>取某个类型的在途打开；没有正在创建的 Task（创建已结束）时返回 false。</summary>
        internal bool TryGetOperation(Type type, out PanelOpenOperation operation)
        {
            if (_dicOperations.TryGetValue(type, out operation) && operation.Task != null)
                return true;

            operation = null;
            return false;
        }

        /// <summary>登记"开始创建"；同类型残留的等待者/废弃标记由新对象继续持有。</summary>
        internal PanelOpenOperation BeginOperation(Type type, Task<UIPanel> task)
        {
            if (!_dicOperations.TryGetValue(type, out PanelOpenOperation operation))
            {
                operation = new PanelOpenOperation(type);
                _dicOperations[type] = operation;
            }

            if (operation.Task == null)
                _loadingCount++;

            operation.Task = task;
            return operation;
        }

        /// <summary>创建 Task 结束后注销登记（按对象与 Task 双重校验，避免误删后来者）。</summary>
        internal void EndOperation(PanelOpenOperation operation, Task<UIPanel> task)
        {
            if (!ReferenceEquals(operation.Task, task))
                return; // 已被 Reset 清空，或类型又被新一次创建接手：不是本次的登记。

            operation.Task = null;
            if (_loadingCount > 0)
                _loadingCount--;

            RemoveIfEmpty(operation);
        }

        /// <summary>
        /// 登记一个等待者；创建者自己也算，“还有没有别人要”的判定依赖这个计数。
        /// </summary>
        internal void AddWaiter(PanelOpenOperation operation)
        {
            operation.AddWaiter();
        }

        /// <summary>等共享创建 Task 结束后移除创建者自己登记的"加载中"条目；异常由各等待方处理。</summary>
        internal async Task UnregisterWhenDoneAsync(PanelOpenOperation operation, Task<UIPanel> task)
        {
            try
            {
                await task;
            }
            catch
            {
                // 异常由调用方处理；这里吞掉只是为了确保后面一定能清理登记。
            }

            EndOperation(operation, task);
        }

        /// <summary>等待者取消/拿到面板后调用；对象未被字典跟踪时什么都不做。</summary>
        internal void RemoveWaiter(PanelOpenOperation operation)
        {
            if (!IsTracked(operation))
                return;

            operation.RemoveWaiter();
            RemoveIfEmpty(operation);
        }

        /// <summary>
        /// 某个等待者取消后调用：先退掉自己的等待记账，再判断是否还有别人在等。
        /// 如果它是最后一个等待者，就把这次打开标记为废弃并立刻调度收尾：
        /// 共享 Task 仍会完成并 InternalShow 面板，但没有任何调用方还能拿到它，
        /// 由收尾回调在创建结束后强制关闭/销毁（收尾只在这里触发，不依赖上层服务）。
        /// 注意：这里不取消共享 Task（取消不中断别人的加载）。
        ///
        /// 这里刻意不走 RemoveWaiter：接下来要靠废弃标记把对象留在字典里等收尾，
        /// 提前剪枝会让收尾判定找不到标记。
        /// </summary>
        internal void AbandonIfLastWaiter(PanelOpenOperation operation)
        {
            if (IsTracked(operation))
                operation.RemoveWaiter();

            if (operation.HasWaiters)
                return; // 还有别的调用方在等，面板仍然有人要。

            operation.IsAbandoned = true;

            Task<UIPanel> openingTask = operation.Task;
            if (openingTask == null)
            {
                // 取消与创建完成恰好同时发生：共享 Task 已结束、登记已清掉，
                // 但面板可能已经进了打开表却没有人拿到它。这里同步收尾。
                FinalizeAbandoned(operation.PanelType);
                return;
            }

            _ = CleanupAbandonedOpeningAsync(operation, openingTask);
        }

        /// <summary>废弃标记是否仍在（期间没有新调用方接手）。</summary>
        internal bool IsAbandoned(Type type)
        {
            return _dicOperations.TryGetValue(type, out PanelOpenOperation operation)
                   && operation.IsAbandoned;
        }

        /// <summary>清掉废弃标记：任何会拿到面板的路径都必须先调用，避免后台收尾误伤。</summary>
        internal void ClearAbandoned(Type type)
        {
            if (!_dicOperations.TryGetValue(type, out PanelOpenOperation operation))
                return;

            operation.IsAbandoned = false;
            RemoveIfEmpty(operation);
        }

        /// <summary>
        /// 后台等共享创建结束再收尾：创建失败只清标记，成功则交给唯一判定处销毁。
        /// </summary>
        private async Task CleanupAbandonedOpeningAsync(
            PanelOpenOperation operation,
            Task<UIPanel> openingTask)
        {
            try
            {
                await openingTask;
            }
            catch
            {
                // 创建本身失败：创建路径已按失败清理，这里只需要清掉废弃标记。
                ClearAbandoned(operation.PanelType);
                return;
            }

            FinalizeAbandoned(operation.PanelType);
        }

        /// <summary>
        /// 收尾判定（唯一一处）：只有"废弃标记仍在 && 没有等待者"才允许销毁。
        /// 标记代表最后一个等待者已取消；等待者计数非 0 说明期间又有调用方接手，面板有人要，绝不能关。
        /// 返回 true 表示标记已被消费，此时通知 PanelService 强制关闭（不进 KeepInstance 缓存）。
        /// </summary>
        private void FinalizeAbandoned(Type type)
        {
            if (!_dicOperations.TryGetValue(type, out PanelOpenOperation operation)
                || !operation.IsAbandoned)
            {
                return;
            }

            if (operation.HasWaiters)
                return;

            operation.IsAbandoned = false;
            RemoveIfEmpty(operation);
            _finalizeAbandoned(type);
        }

        /// <summary>
        /// 判断对象是否仍是字典里登记的那个实例，避免旧对象误删新一次打开的状态。
        /// </summary>
        private bool IsTracked(PanelOpenOperation operation)
        {
            return _dicOperations.TryGetValue(operation.PanelType, out PanelOpenOperation tracked)
                   && ReferenceEquals(tracked, operation);
        }

        /// <summary>三个状态都为空时才把对象移出字典；字典条目对外不可见。</summary>
        private void RemoveIfEmpty(PanelOpenOperation operation)
        {
            if (operation.Task != null || operation.IsAbandoned || operation.HasWaiters)
                return;

            if (IsTracked(operation))
                _dicOperations.Remove(operation.PanelType);
        }
    }
}
