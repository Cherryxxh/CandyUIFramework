namespace CandyUI.Demo
{
    /// <summary>
    /// 商店商品（Model）。和 BagItem 一样：纯数据，不碰 UI。
    /// </summary>
    public sealed class ShopItem
    {
        public int Id { get; }
        public string Name { get; }
        public int Price { get; }
        public string Description { get; }

        /// <summary>
        /// 构造一条商品数据；价格等字段只读，扣钱由 ShopService 负责。
        /// </summary>
        public ShopItem(int id, string name, int price, string description)
        {
            Id = id;
            Name = name;
            Price = price;
            Description = description;
        }
    }
}
