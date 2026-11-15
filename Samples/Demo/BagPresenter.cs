using System.Collections.Generic;

namespace CandyUI.Demo
{
    /// <summary>
    /// 背包的 Presenter（M3 的 Presenter 示例）。
    ///
    /// 它负责"背包装什么、选中谁、使用后怎么变"：
    ///   - 从 BagService 取数据并按页签过滤；
    ///   - 记住当前选中的道具；
    ///   - 处理"使用"：扣数量、重新过滤、刷新列表和详情；
    ///   - 把结果交给 BagPanel 显示。
    ///
    /// 它不 new UI、不碰 RectTransform、不订阅 Unity 生命周期，
    /// 所以这段逻辑将来可以直接搬到别的地方（甚至服务器）复用。
    /// </summary>
    public sealed class BagPresenter : PresenterBase
    {
        private readonly List<BagItem> _items = new List<BagItem>();

        private BagTab _tab = BagTab.All;
        private int _selectedId = -1;

        private BagPanel Panel
        {
            get { return (BagPanel)View; }
        }

        /// <summary>
        /// 绑定 View 时订阅页签、格子、使用三个事件；生命周期由框架驱动。
        /// </summary>
        protected override void OnBind()
        {
            Panel.TabClicked += HandleTabClicked;
            Panel.SlotClicked += HandleSlotClicked;
            Panel.UseClicked += HandleUseClicked;
        }

        /// <summary>
        /// 每次打开时按 BagPanelData 重置页签与选中项，再重新拉数据。
        /// </summary>
        public override void OnShow(UIPanelData data)
        {
            // 每次显示都重新决定"从哪个页签开始"：这就是"刷新数据放 OnShow"的示例。
            BagPanelData bagData = data as BagPanelData;
            _tab = bagData != null ? bagData.Tab : BagTab.All;
            _selectedId = -1;

            Panel.SetTab(_tab);
            Panel.ClearDetail();
            Reload();
        }

        /// <summary>
        /// 面板销毁前退订三个事件，避免 Presenter 比 View 活得久时留下悬空订阅。
        /// </summary>
        public override void OnClose()
        {
            if (!IsBound)
                return;

            Panel.TabClicked -= HandleTabClicked;
            Panel.SlotClicked -= HandleSlotClicked;
            Panel.UseClicked -= HandleUseClicked;
        }

        /// <summary>
        /// 切页签：清掉选中项、刷新高亮，并按新页签重新过滤列表。
        /// </summary>
        private void HandleTabClicked(BagTab tab)
        {
            _tab = tab;
            _selectedId = -1;

            Panel.SetTab(tab);
            Panel.ClearDetail();
            Reload();
        }

        /// <summary>
        /// 点格子：选中该道具并刷新格子高亮和右侧详情；越界索引直接忽略。
        /// </summary>
        private void HandleSlotClicked(int index)
        {
            if (index < 0 || index >= _items.Count)
                return;

            _selectedId = _items[index].Id;
            Panel.ShowSlots(_items, index);
            Panel.ShowDetail(_items[index]);
        }

        /// <summary>
        /// 点“使用”：由 Service 扣数量后刷新列表和详情；用完的道具从两处一起消失。
        /// </summary>
        private void HandleUseClicked()
        {
            BagItem item = FindSelected();
            if (item == null)
                return;

            // 真正的业务判断在 Service：数量够不够、能不能用。
            if (!BagService.Use(item.Id))
                return;

            Reload();

            int index = _items.FindIndex(candidate => candidate.Id == item.Id);
            if (index >= 0)
            {
                _selectedId = item.Id;
                Panel.ShowSlots(_items, index);
                Panel.ShowDetail(_items[index]);
            }
            else
            {
                // 用完了，从列表和详情里一起消失。
                _selectedId = -1;
                Panel.ClearDetail();
            }
        }

        /// <summary>
        /// 按 Id 查找当前选中的道具；没选中或已用完时返回 null，调用方直接跳过。
        /// </summary>
        private BagItem FindSelected()
        {
            if (_selectedId < 0)
                return null;

            return _items.Find(item => item.Id == _selectedId);
        }

        /// <summary>
        /// 按当前页签重新取数据，并尽量保持原来的选中项（找不到就清空）。
        /// </summary>
        private void Reload()
        {
            _items.Clear();
            _items.AddRange(BagService.GetItems(_tab));

            int selectedIndex = _items.FindIndex(item => item.Id == _selectedId);
            Panel.ShowSlots(_items, selectedIndex);
        }
    }
}
