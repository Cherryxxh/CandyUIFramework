// 职责：测试专用可控转场——入场立即完成、退场挂起，由用例手动放行，用于稳定复现"关闭动画期间"的竞态。
// 依赖：仅依赖 IPanelTransition 接口约定；用例通过反射把它挂到 UIPanel.Transition 上。
// 限制：同一时刻只跟踪一次退场；用例必须在发起关闭后调用 CompleteOut，否则关闭 Task 永远挂起。
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI.Tests
{
    /// <summary>
    /// 可控假转场：PlayInAsync 立即完成，PlayOutAsync 返回由用例手动完成的 Task。
    /// </summary>
    public sealed class ControllableTransition : IPanelTransition
    {
        private TaskCompletionSource<bool> _tcsPlayOut;

        /// <summary>退场动画被调用的次数，用于断言重复关闭没有重复播放退场。</summary>
        public int PlayOutCount { get; private set; }

        /// <summary>入场动画被调用的次数。</summary>
        public int PlayInCount { get; private set; }

        /// <summary>当前是否存在尚未放行的退场动画。</summary>
        public bool IsPlayOutPending => _tcsPlayOut != null && !_tcsPlayOut.Task.IsCompleted;

        /// <summary>入场立即完成，用例只关心退场何时结束。</summary>
        public Task PlayInAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            PlayInCount++;
            return Task.CompletedTask;
        }

        /// <summary>退场挂起：返回的 Task 由用例调用 CompleteOut 放行。</summary>
        public Task PlayOutAsync(UIPanel panel, CancellationToken cancellationToken)
        {
            PlayOutCount++;
            _tcsPlayOut = new TaskCompletionSource<bool>();
            return _tcsPlayOut.Task;
        }

        /// <summary>放行最近一次退场动画；没有挂起的退场时抛异常提示调用顺序错误。</summary>
        public void CompleteOut()
        {
            if (_tcsPlayOut == null || _tcsPlayOut.Task.IsCompleted)
            {
                throw new InvalidOperationException("[ControllableTransition] 当前没有挂起的退场动画，请先发起关闭。");
            }

            _tcsPlayOut.TrySetResult(true);
        }
    }
}
