using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CandyUI.Demo
{
    /// <summary>
    /// 背包界面（M3 的 View 示例）。
    ///
    /// 这个类只做三件事：
    ///   1. OnCreate：把控件搭出来；
    ///   2. 提供 ShowSlots / ShowDetail 这类"显示方法"，让 Presenter 调；
    ///   3. 把用户操作（切页签、点格子、点使用）用事件抛给 Presenter。
    ///
    /// 它不查数据、不算过滤、不决定"用了之后还剩几个"——那些都是 Presenter 的活。
    /// 这样将来把逻辑换到服务器校验，界面代码一行不用改。
    /// </summary>
    [UIPanel("CandyUI/Demo/BagPanel", UILayer.Window, closeBehavior: PanelCloseBehavior.KeepInstance)]
    public sealed class BagPanel : UIPanel
    {
        public event Action<BagTab> TabClicked;
        public event Action<int> SlotClicked;
        public event Action UseClicked;

        private const int Columns = 4;
        private const int Rows = 5;
        private const int SlotCount = Columns * Rows;
        private const float SlotSize = 112f;
        private const float SlotGap = 12f;

        private static readonly Color CardColor = new Color(0.14f, 0.16f, 0.20f, 0.99f);
        private static readonly Color TabActive = new Color(0.26f, 0.48f, 0.85f, 1f);
        private static readonly Color TabInactive = new Color(0.20f, 0.23f, 0.29f, 1f);
        private static readonly Color SlotEmpty = new Color(0.15f, 0.16f, 0.19f, 1f);
        private static readonly Color SlotMaterial = new Color(0.20f, 0.30f, 0.22f, 1f);
        private static readonly Color SlotEquipment = new Color(0.20f, 0.27f, 0.40f, 1f);
        private static readonly Color DetailColor = new Color(0.10f, 0.12f, 0.16f, 1f);

        private readonly BagTab[] _tabs = { BagTab.All, BagTab.Material, BagTab.Equipment };
        private readonly Image[] _tabBackgrounds = new Image[3];
        private readonly Button[] _slotButtons = new Button[SlotCount];
        private readonly Image[] _slotBackgrounds = new Image[SlotCount];
        private readonly TMP_Text[] _slotNames = new TMP_Text[SlotCount];
        private readonly TMP_Text[] _slotCounts = new TMP_Text[SlotCount];

        private Image _detailRoot;
        private TMP_Text _emptyHint;
        private TMP_Text _detailName;
        private TMP_Text _detailDescription;
        private TMP_Text _detailCount;
        private Button _useButton;
        private TMP_Text _useLabel;

        /// <summary>
        /// 面板创建时一次性搭好界面；之后只通过 ShowSlots/ShowDetail 刷新，不重建控件。
        /// </summary>
        protected override void OnCreate()
        {
            BuildBackdropAndCard();
            // ---- 页签 ----
            string[] tabLabels = { "全部", "材料", "装备" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                BagTab tab = _tabs[i];
                Button button = DemoUi.CreateButton("Tab_" + tabLabels[i], GetCard(),
                    tabLabels[i], new Vector2(-180f + i * 180f, 300f),
                    new Vector2(160f, 58f), TabInactive,
                    () => TabClicked?.Invoke(tab));
                _tabBackgrounds[i] = button.GetComponent<Image>();
            }

            // ---- 格子 ----
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    int index = row * Columns + column;
                    float x = -210f + (column - 1.5f) * (SlotSize + SlotGap);
                    float y = -40f + (2 - row) * (SlotSize + SlotGap);
                    CreateSlot(index, new Vector2(x, y));
                }
            }

            // ---- 右侧详情 ----
            CreateDetail();

            // ---- 关闭按钮 ----
            DemoUi.CreateButton("Close", GetCard(), "关闭",
                new Vector2(470f, 395f), new Vector2(120f, 58f),
                new Color(0.55f, 0.22f, 0.22f, 1f), CloseSelf);

            ClearDetail();
        }

        /// <summary>
        /// 创建配套的 BagPresenter；框架负责创建、绑定和转发生命周期。
        /// </summary>
        protected override PresenterBase CreatePresenter()
        {
            // 这一行就把"显示"和"逻辑"接起来了：框架负责创建/绑定/转发生命周期。
            return new BagPresenter();
        }

        /// <summary>接口：切换页签时刷新高亮（Presenter 决定"当前是哪个页签"）。</summary>
        public void SetTab(BagTab activeTab)
        {
            for (int i = 0; i < _tabs.Length; i++)
                _tabBackgrounds[i].color = _tabs[i] == activeTab ? TabActive : TabInactive;
        }

        /// <summary>接口：把过滤后的列表画到格子里。selectedIndex 为 -1 表示没有选中。</summary>
        public void ShowSlots(IReadOnlyList<BagItem> items, int selectedIndex)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                bool hasItem = i < items.Count;
                _slotButtons[i].interactable = hasItem;
                _slotNames[i].text = hasItem ? items[i].Name : string.Empty;
                _slotCounts[i].text = hasItem ? "x" + items[i].Count : string.Empty;

                Color background = SlotEmpty;
                if (hasItem)
                {
                    background = items[i].Tab == BagTab.Equipment ? SlotEquipment : SlotMaterial;
                    if (i == selectedIndex)
                        background = Color.Lerp(background, Color.white, 0.20f);
                }

                _slotBackgrounds[i].color = background;
            }
        }

        /// <summary>接口：显示某个道具的详情。</summary>
        public void ShowDetail(BagItem item)
        {
            if (item == null)
            {
                ClearDetail();
                return;
            }

            _detailRoot.gameObject.SetActive(true);
            _emptyHint.gameObject.SetActive(false);

            _detailName.text = item.Name;
            _detailDescription.text = item.Description;
            _detailCount.text = "剩余数量：" + item.Count;
            _useButton.interactable = item.Count > 0;
            _useLabel.text = item.Count > 0 ? "使用一个" : "已用完";
        }

        /// <summary>接口：清空详情（没选中任何道具时）。</summary>
        public void ClearDetail()
        {
            _detailRoot.gameObject.SetActive(false);
            _emptyHint.gameObject.SetActive(true);
        }

        /// <summary>
        /// 搭半透明背景和主卡片（Demo 用代码搭 UI，正式项目应改用 Prefab）。
        /// </summary>
        private void BuildBackdropAndCard()
        {
            Image backdrop = DemoUi.CreateImage("Backdrop", transform, new Color(0f, 0f, 0f, 0.88f));
            DemoUi.Stretch(backdrop.rectTransform);

            RectTransform card = DemoUi.CreateCard(transform, new Vector2(1120f, 860f), CardColor);
            card.name = "BagCard";

            TMP_Text title = DemoUi.CreateTitle(card, "背包", 52, 390f);
            title.color = Color.white;

            TMP_Text hint = DemoUi.CreateText("Hint", card,
                "M3 示例：View（本类）只负责显示，Presenter 负责过滤/选中/使用，BagService 提供数据",
                20, TextAnchor.MiddleCenter);
            hint.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            RectTransform hintRect = hint.rectTransform;
            hintRect.sizeDelta = new Vector2(1000f, 40f);
            hintRect.anchoredPosition = new Vector2(0f, 345f);
        }

        /// <summary>
        /// 取主卡片节点：页签、格子、详情都挂到它下面，避免各处重复查找。
        /// </summary>
        private RectTransform GetCard()
        {
            return transform.Find("BagCard") as RectTransform;
        }

        /// <summary>
        /// 创建一个格子：底图、按钮、名称和数量文本，并登记到数组供 ShowSlots 复用。
        /// </summary>
        private void CreateSlot(int index, Vector2 position)
        {
            Image background = DemoUi.CreateImage("Slot_" + index, GetCard(), SlotEmpty);
            RectTransform rect = background.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(SlotSize, SlotSize);
            rect.anchoredPosition = position;

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SlotClicked?.Invoke(index));

            TMP_Text name = DemoUi.CreateText("Name", rect, string.Empty, 18, TextAnchor.MiddleCenter);
            name.rectTransform.sizeDelta = new Vector2(SlotSize - 12f, 56f);
            name.rectTransform.anchoredPosition = new Vector2(0f, 8f);

            TMP_Text count = DemoUi.CreateText("Count", rect, string.Empty, 16, TextAnchor.MiddleCenter);
            count.color = new Color(0.85f, 0.9f, 1f, 1f);
            count.rectTransform.sizeDelta = new Vector2(SlotSize - 12f, 28f);
            count.rectTransform.anchoredPosition = new Vector2(0f, -36f);

            _slotButtons[index] = button;
            _slotBackgrounds[index] = background;
            _slotNames[index] = name;
            _slotCounts[index] = count;
        }

        /// <summary>
        /// 搭右侧详情面板和“未选中”空提示；空提示放卡片上，避免随详情一起被隐藏。
        /// </summary>
        private void CreateDetail()
        {
            Image detail = DemoUi.CreateImage("Detail", GetCard(), DetailColor);
            RectTransform rect = detail.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(460f, 620f);
            rect.anchoredPosition = new Vector2(300f, -40f);
            _detailRoot = detail;

            _detailName = DemoUi.CreateText("DetailName", rect, string.Empty, 32, TextAnchor.MiddleCenter);
            _detailName.fontStyle = FontStyles.Bold;
            _detailName.rectTransform.sizeDelta = new Vector2(400f, 60f);
            _detailName.rectTransform.anchoredPosition = new Vector2(0f, 245f);

            _detailDescription = DemoUi.CreateText("DetailDescription", rect, string.Empty, 21,
                TextAnchor.UpperLeft);
            _detailDescription.color = new Color(0.82f, 0.86f, 0.92f, 1f);
            _detailDescription.rectTransform.sizeDelta = new Vector2(400f, 260f);
            _detailDescription.rectTransform.anchoredPosition = new Vector2(0f, 80f);

            _detailCount = DemoUi.CreateText("DetailCount", rect, string.Empty, 24,
                TextAnchor.MiddleCenter);
            _detailCount.rectTransform.sizeDelta = new Vector2(400f, 40f);
            _detailCount.rectTransform.anchoredPosition = new Vector2(0f, -80f);

            _useButton = DemoUi.CreateButton("Use", rect, "使用一个",
                new Vector2(0f, -230f), new Vector2(260f, 70f),
                new Color(0.24f, 0.50f, 0.30f, 1f), () => UseClicked?.Invoke());
            _useLabel = _useButton.GetComponentInChildren<TMP_Text>();

            // 空状态提示放在卡片上，而不是详情面板里：
            // 详情面板会被整体隐藏，如果提示是它的子物体，也会跟着消失。
            _emptyHint = DemoUi.CreateText("EmptyHint", GetCard(),
                "点击左侧格子查看道具详情", 22, TextAnchor.MiddleCenter);
            _emptyHint.color = new Color(0.6f, 0.66f, 0.75f, 1f);
            _emptyHint.rectTransform.sizeDelta = new Vector2(400f, 80f);
            _emptyHint.rectTransform.anchoredPosition = new Vector2(300f, -40f);
        }
    }
}
