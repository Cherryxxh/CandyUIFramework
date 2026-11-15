using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CandyUI
{
    /// <summary>
    /// 一条"待显示的弹窗"请求。
    ///
    /// 为什么不直接存面板类型？
    /// 因为弹窗是异步加载的，队列里可能同时有多个类型（网络错误、奖励、升级），
    /// 而"打开某个类型"的代码是泛型的。这里用委托把泛型调用包起来，
    /// 队列就不需要知道具体的 Type 了。
    ///
    /// 取消：排队中的请求可以被取消。调用方传入 CancellationToken 后，
    /// 一旦取消，请求会立刻从队列里移除，之后不会再弹出来。
    /// 已经显示出来的弹窗不受影响，用 CloseTopPopup 关闭即可。
    /// </summary>
    internal sealed class PopupRequest
    {
        public Type PanelType;
        public UIPanelData Data;
        public int Priority;

        /// <summary>调用方传入的取消令牌（排队期间有效）。</summary>
        public CancellationToken Token;

        /// <summary>取消时把自己从队列里摘掉的注册；出队/清空时 Dispose。</summary>
        public CancellationTokenRegistration Registration;

        public Func<UIPanelData, CancellationToken, Task<UIPanel>> Open;
    }

    /// <summary>
    /// 弹窗队列：同一时间只显示一个弹窗，其余的排队等待。
    ///
    /// 排序规则：Priority 大的先显示；Priority 相同按入队先后（FIFO）。
    /// 这样"网络错误"可以配置成 100，"活动奖励"10，"升级提示"0，
    /// 强制提示永远优先于普通提示。
    ///
    /// 典型场景：战斗中先后触发"升级""获得成就""网络断开"三个弹窗，
    /// 玩家关闭一个后自动显示下一个，而不是三个叠在一起。
    ///
    /// 注意：本类不做线程同步，所有调用都应发生在主线程
    /// （CancellationToken 的取消回调也应在主线程触发）。
    /// </summary>
    internal sealed class PopupQueue
    {
        private readonly List<PopupRequest> _requests = new List<PopupRequest>();

        public int Count
        {
            get { return _requests.Count; }
        }

        /// <summary>
        /// 弹窗请求入队：按优先级插到第一个更低优先级之前（同优先级保持 FIFO），并注册排队期取消。
        /// </summary>
        public void Enqueue(Type panelType, UIPanelData data, int priority,
            CancellationToken token,
            Func<UIPanelData, CancellationToken, Task<UIPanel>> open)
        {
            PopupRequest request = new PopupRequest
            {
                PanelType = panelType,
                Data = data,
                Priority = priority,
                Token = token,
                Open = open,
            };

            // 还在排队时就被取消：直接从队列里摘掉，避免之后又弹出来。
            if (token.CanBeCanceled)
                request.Registration = token.Register(() => Remove(request));

            // 插入到第一个"优先级更低"的请求前面；相同优先级留在后面，保证 FIFO。
            int index = _requests.Count;
            for (int i = 0; i < _requests.Count; i++)
            {
                if (priority > _requests[i].Priority)
                {
                    index = i;
                    break;
                }
            }

            _requests.Insert(index, request);
        }

        /// <summary>
        /// 取出队首请求并释放其取消注册；队列为空时返回 null。
        /// </summary>
        public PopupRequest Dequeue()
        {
            if (_requests.Count == 0)
                return null;

            PopupRequest request = _requests[0];
            _requests.RemoveAt(0);

            // 已经出队，取消回调不需要再把自己摘掉了。
            request.Registration.Dispose();
            return request;
        }

        /// <summary>把某个请求从队列里移除（取消回调、CloseAllPopups 用）。</summary>
        public bool Remove(PopupRequest request)
        {
            if (request == null)
                return false;

            return _requests.Remove(request);
        }

        /// <summary>队列里是否已经有这个类型的弹窗（防止同一提示重复入队）。</summary>
        public bool Contains(Type panelType)
        {
            for (int i = 0; i < _requests.Count; i++)
            {
                if (_requests[i].PanelType == panelType)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 清空队列并释放所有取消注册；Reset / CloseAllPopups 用它丢弃尚未显示的弹窗。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _requests.Count; i++)
                _requests[i].Registration.Dispose();

            _requests.Clear();
        }

        /// <summary>队列内容的可读描述（调试面板用），例如 "ConfirmPopup(0), RewardPopup(10)"。</summary>
        public string Describe()
        {
            if (_requests.Count == 0)
                return "空";

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < _requests.Count; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(_requests[i].PanelType.Name);
                builder.Append('(').Append(_requests[i].Priority).Append(')');
            }

            return builder.ToString();
        }
    }
}
