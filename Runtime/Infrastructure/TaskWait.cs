using System;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 异步等待工具：让"被共享的加载 Task"和"每个调用方自己的取消"解耦。
    ///
    /// 背景：同一个资源 / 面板的加载 Task 会被多个调用方共享。
    /// 如果创建 Task 时绑定某一个调用方的 CancellationToken，这个调用方取消时
    /// 会把其他正常等待的调用方一起取消；但直接 await 又无法响应自己的取消。
    ///
    /// 约定：共享加载 Task 内部一律用 CancellationToken.None 创建，
    /// 每个等待方用本方法等待 —— 取消只放弃"这一次等待"，不取消共享加载。
    /// </summary>
    internal static class TaskWait
    {
        /// <summary>
        /// 等待 task，同时响应调用方自己的取消。
        /// 取消时抛 OperationCanceledException(cancellationToken)；
        /// 底层 task 继续执行，由它的创建者负责清理。
        /// </summary>
        internal static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            // 调用方没有取消能力：直接等，省掉一次 WhenAny 的分配。
            if (!cancellationToken.CanBeCanceled)
                return await task;

            // Task.WhenAny 不能直接"取消等待"，用一个 TCS 在 token 取消时抢先完成，
            // 这样 await WhenAny 立刻返回；task 本身不受影响。
            TaskCompletionSource<bool> canceledSignal = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => canceledSignal.TrySetResult(true)))
            {
                Task completedTask = await Task.WhenAny(task, canceledSignal.Task);
                if (completedTask != task)
                    throw new OperationCanceledException(cancellationToken);
            }

            return await task;
        }
    }
}
