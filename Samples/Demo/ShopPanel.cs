using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 商店界面（M3 最小 View 示例）。
    ///
    /// 对比 BagPanel：这里没有页签、没有过滤，View 更"薄"，
    /// 只做三件事：搭控件、把 Presenter 给的数据显示出来、把点击抛成事件。
    /// 购买逻辑（钱够不够、扣多少）全在 ShopPresenter / ShopService。
    /// </summary>
    [UIPanel("CandyUI/Demo/ShopPanel", UILayer.Window)]
    public sealed class ShopPanel : UIPanel
    {
        public event Action<int> BuyClicked;

        private const int MaxRows = 3;
        private static readonly Color GoldColor = new Color(1f, 0.86f, 0.45f, 1f);
        private static readonly Color PriceNormal = new Color(1f, 0.86f, 0.45f, 1f);
        private static readonly Color PriceTooExpensive = new Color(1f, 0.45f, 0.45f, 1f);

        private readonly Image[] _rows = new Image[MaxRows];
        private readonly TMP_Text[] _names = new TMP_Text[MaxRows];
        private readonly TMP_Text[] _prices = new TMP_Text[MaxRows];
        private readonly Button[] _buyButtons = new Button[MaxRows];
        private readonly int[] _itemIds = new int[MaxRows];

        private TMP_Text _goldText;
        private TMP_Text _messageText;

        /// <summary>
        /// 创建 ShopPresenter；购买逻辑全在 Presenter 和 Service，View 不参与。
        /// </summary>
        protected override PresenterBase CreatePresenter()
        {
            return new ShopPresenter();
        }

        /// <summary>
        /// 面板创建时搭背景、卡片、金币栏和 3 行商品；行控件由 CreateRow 统一生成。
        /// </summary>
        protected override void OnCreate()
        {
            Image backdrop = DemoUi.CreateImage("Backdrop", transform, new Color(0f, 0f, 0f, 0.88f));
            DemoUi.Stretch(backdrop.rectTransform);

            RectTransform card = DemoUi.CreateCard(transform, new Vector2(940f, 640f),
                new Color(0.14f, 0.16f, 0.20f, 0.99f));
            card.name = "ShopCard";

            TMP_Text title = DemoUi.CreateTitle(card, "杂货铺", 50, 265f);

            _goldText = DemoUi.CreateText("Gold", card, string.Empty, 30, TextAnchor.MiddleRight);
            Place(_goldText.rectTransform, new Vector2(300f, 265f), new Vector2(300f, 50f));
            _goldText.color = GoldColor;

            TMP_Text hint = DemoUi.CreateText("Hint", card,
                "View 只负责显示；点击购买后由 Presenter 找 Service 扣金币，再回来刷新界面",
                20, TextAnchor.MiddleCenter);
            hint.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            Place(hint.rectTransform, new Vector2(0f, 205f), new Vector2(820f, 40f));

            for (int i = 0; i < MaxRows; i++)
                CreateRow(card, i);

            _messageText = DemoUi.CreateText("Message", card, string.Empty, 24, TextAnchor.MiddleCenter);
            _messageText.color = new Color(0.70f, 1f, 0.80f, 1f);
            Place(_messageText.rectTransform, new Vector2(0f, -245f), new Vector2(820f, 44f));

            DemoUi.CreateButton("Close", card, "关闭",
                new Vector2(410f, 265f), new Vector2(110f, 54f),
                new Color(0.55f, 0.22f, 0.22f, 1f), CloseSelf);

            _ = title;
        }

        /// <summary>接口：显示当前金币和商品列表（金币不够的按钮置灰、价格标红）。</summary>
        public void ShowItems(IReadOnlyList<ShopItem> items, int gold)
        {
            _goldText.text = "金币：" + gold;

            for (int i = 0; i < MaxRows; i++)
            {
                bool hasItem = i < items.Count;
                _rows[i].gameObject.SetActive(hasItem);
                if (!hasItem)
                    continue;

                ShopItem item = items[i];
                _itemIds[i] = item.Id;
                _names[i].text = item.Name + "\n" + item.Description;
                _prices[i].text = item.Price + " 金币";

                bool canBuy = gold >= item.Price;
                _buyButtons[i].interactable = canBuy;
                _prices[i].color = canBuy ? PriceNormal : PriceTooExpensive;
            }
        }

        /// <summary>接口：显示一行提示（购买成功/金币不足）。</summary>
        public void ShowMessage(string message)
        {
            _messageText.text = message;
        }

        /// <summary>
        /// 创建一行商品控件；购买按钮点击时把商品 Id 抛给 Presenter 处理。
        /// </summary>
        private void CreateRow(RectTransform card, int index)
        {
            float y = 110f - index * 100f;

            Image row = DemoUi.CreateImage("Row_" + index, card, new Color(0.18f, 0.20f, 0.25f, 1f));
            RectTransform rect = row.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(820f, 84f);
            rect.anchoredPosition = new Vector2(0f, y);

            TMP_Text name = DemoUi.CreateText("Name", rect, string.Empty, 24, TextAnchor.MiddleLeft);
            Place(name.rectTransform, new Vector2(-210f, 0f), new Vector2(380f, 76f));

            TMP_Text price = DemoUi.CreateText("Price", rect, string.Empty, 24, TextAnchor.MiddleRight);
            Place(price.rectTransform, new Vector2(90f, 0f), new Vector2(160f, 60f));
            price.color = PriceNormal;

            int capturedIndex = index;
            Button buy = DemoUi.CreateButton("Buy", rect, "购买",
                new Vector2(320f, 0f), new Vector2(120f, 56f),
                new Color(0.24f, 0.50f, 0.30f, 1f),
                () => BuyClicked?.Invoke(_itemIds[capturedIndex]));

            _rows[index] = row;
            _names[index] = name;
            _prices[index] = price;
            _buyButtons[index] = buy;
        }

        /// <summary>
        /// 把 RectTransform 设成居中锚点并放到指定位置和尺寸，减少重复的锚点代码。
        /// </summary>
        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
