namespace CandyUI.Demo
{
    /// <summary>
    /// 背包道具（演示用的 Model）。
    ///
    /// 它不继承 MonoBehaviour、不引用任何 UI 控件：
    /// 这样 Presenter 和 Service 可以只依赖数据本身，
    /// 将来把数据换成配置表/服务器下发，界面代码也不用改。
    /// </summary>
    public sealed class BagItem
    {
        public int Id { get; }
        public string Name { get; }
        public BagTab Tab { get; }
        public string Description { get; }
        public int Count { get; private set; }

        /// <summary>
        /// 构造一条背包数据；字段只读，数量只能由 TryUseOne 递减。
        /// </summary>
        public BagItem(int id, string name, BagTab tab, int count, string description)
        {
            Id = id;
            Name = name;
            Tab = tab;
            Count = count;
            Description = description;
        }

        /// <summary>使用一个：数量减一。数量为 0 时返回 false（业务上不允许使用）。</summary>
        public bool TryUseOne()
        {
            if (Count <= 0)
                return false;

            Count--;
            return true;
        }
    }
}
