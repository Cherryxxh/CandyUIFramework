// 职责：校验 [UIPanel] 面板的类型、地址、Prefab 引用与常量命名，输出到 Console，编辑器内弹窗汇总。
// 依赖：UnityEditor（AssetDatabase/EditorUtility）、PanelKeyGenerator（复用类型扫描与命名规则）、Resources。
// 限制：只能静态确认 Resources 下的资源；YooAsset/Addressables 加载的面板存在性仍需手动确认。
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 面板引用校验器：检查 [UIPanel] 的类型继承、地址唯一性、Resources 下的 Prefab 与根组件、PanelKeys 命名冲突。
    ///
    /// 为什么放在 CandyUI 命名空间：batchmode 通过 -executeMethod CandyUI.PanelValidator.Run 调用，全名必须固定。
    /// </summary>
    public static class PanelValidator
    {
        private static int _errorCount;
        private static int _warningCount;

        /// <summary>按类型缓存脚本资产，保证 Console 的上下文可点击定位同一脚本。</summary>
        private static readonly Dictionary<Type, MonoScript> ScriptCache = new Dictionary<Type, MonoScript>();

        #region 菜单与入口

        /// <summary>菜单入口：完整校验一次并弹窗汇总；命令行请用 -executeMethod CandyUI.PanelValidator.Run。</summary>
        [MenuItem("CandyUI/校验/面板引用", priority = 41)]
        public static void Run()
        {
            RunInternal(showDialog: true);
        }

        /// <summary>校验主流程：类型 → 地址 → Prefab → 命名 → 弹窗配置提示 → 汇总。</summary>
        internal static void RunInternal(bool showDialog)
        {
            _errorCount = 0;
            _warningCount = 0;
            ScriptCache.Clear();

            List<Type> attributedTypes = PanelKeyGenerator.GetAttributedTypes();
            CheckPanelTypes(attributedTypes);

            List<PanelKeyGenerator.PanelEntry> entries = PanelKeyGenerator.CollectEntries(out List<string> namingConflicts);
            CheckDuplicateAddresses(entries);
            CheckPrefabs(entries);
            CheckNamingConflicts(namingConflicts);
            CheckPopupDefaults(entries);

            Report(showDialog);
        }

        #endregion

        #region 类型与地址校验

        /// <summary>校验类型本身：必须继承 UIPanel、特性可读、地址非空白。</summary>
        private static void CheckPanelTypes(List<Type> attributedTypes)
        {
            foreach (Type type in attributedTypes)
            {
                if (!typeof(UIPanel).IsAssignableFrom(type))
                {
                    Error($"类型 {type.FullName} 带 [UIPanel] 但没有继承 UIPanel，框架无法打开该面板。", FindScriptAsset(type));
                    continue;
                }

                if (!PanelKeyGenerator.TryGetAttribute(type, out UIPanelAttribute attribute, out Exception error))
                {
                    Error($"类型 {type.FullName} 的 [UIPanel] 特性读取失败（{error?.Message}），常见原因是地址为空。", FindScriptAsset(type));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(attribute.PrefabAddress))
                    Error($"类型 {type.FullName} 的 [UIPanel] 地址为空或只有空白。", FindScriptAsset(type));
            }
        }

        /// <summary>校验地址唯一：同一地址被多个面板类型共用时运行时会互相覆盖，按 Error 报出全部类型。</summary>
        private static void CheckDuplicateAddresses(List<PanelKeyGenerator.PanelEntry> entries)
        {
            var dicByAddress = new Dictionary<string, List<PanelKeyGenerator.PanelEntry>>(StringComparer.Ordinal);
            foreach (PanelKeyGenerator.PanelEntry entry in entries)
            {
                if (!dicByAddress.TryGetValue(entry.PrefabAddress, out List<PanelKeyGenerator.PanelEntry> list))
                {
                    list = new List<PanelKeyGenerator.PanelEntry>();
                    dicByAddress.Add(entry.PrefabAddress, list);
                }

                list.Add(entry);
            }

            foreach (KeyValuePair<string, List<PanelKeyGenerator.PanelEntry>> pair in dicByAddress)
            {
                if (pair.Value.Count <= 1)
                    continue;

                var typeNames = new List<string>();
                foreach (PanelKeyGenerator.PanelEntry entry in pair.Value)
                    typeNames.Add(entry.Type.FullName);
                Error($"地址 \"{pair.Key}\" 被 {pair.Value.Count} 个面板类型共用：{string.Join("、", typeNames)}。", FindScriptAsset(pair.Value[0].Type));
            }
        }

        #endregion

        #region Prefab 校验

        /// <summary>校验 Resources 下的 Prefab：找不到只报 Warning（可能是 YooAsset），找到则要求根节点挂对应面板组件。</summary>
        private static void CheckPrefabs(List<PanelKeyGenerator.PanelEntry> entries)
        {
            foreach (PanelKeyGenerator.PanelEntry entry in entries)
            {
                GameObject prefab = Resources.Load<GameObject>(entry.PrefabAddress);
                if (prefab == null)
                {
                    Warning($"在 Resources 下找不到地址 \"{entry.PrefabAddress}\" 对应的 Prefab（类型 {entry.Type.FullName}）。如果你用 YooAsset/Addressables，这条可以忽略。", FindScriptAsset(entry.Type));
                    continue;
                }

                if (prefab.GetComponent(entry.Type) == null)
                    Error($"Prefab \"{entry.PrefabAddress}\" 根节点没有挂 {entry.Type.Name} 组件（{entry.Type.FullName}）。", prefab);
            }
        }

        /// <summary>把生成器的常量命名冲突转成 Warning，提示有人用同名类型但地址不同。</summary>
        private static void CheckNamingConflicts(List<string> namingConflicts)
        {
            foreach (string conflict in namingConflicts)
                Warning(conflict, null);
        }

        /// <summary>Popup 层沿用默认 popupPriority=0 / maskAlpha=0.6 时给一条聚合提示，避免逐个刷屏。</summary>
        private static void CheckPopupDefaults(List<PanelKeyGenerator.PanelEntry> entries)
        {
            var defaultPopupNames = new List<string>();
            foreach (PanelKeyGenerator.PanelEntry entry in entries)
            {
                UIPanelAttribute attribute = entry.Attribute;
                if (attribute.Layer != UILayer.Popup)
                    continue;
                if (attribute.PopupPriority == 0 && Mathf.Approximately(attribute.MaskAlpha, 0.6f))
                    defaultPopupNames.Add(entry.Type.Name);
            }

            if (defaultPopupNames.Count > 0)
                Debug.Log($"[CandyUI] 提示：{defaultPopupNames.Count} 个弹窗沿用默认 popupPriority=0 / maskAlpha=0.6（{string.Join("、", defaultPopupNames)}），需要排序或调整遮罩时请显式指定。");
        }

        #endregion

        #region 汇总与工具

        /// <summary>输出汇总：命令行只打一行固定格式日志，编辑器内再弹窗；错误/警告已随校验逐条输出。</summary>
        private static void Report(bool showDialog)
        {
            Debug.Log($"[CandyUI] 面板校验完成：错误 {_errorCount} 条，警告 {_warningCount} 条");
            if (showDialog && !Application.isBatchMode)
                EditorUtility.DisplayDialog("CandyUI 面板校验", $"错误 {_errorCount} 条，警告 {_warningCount} 条，详情见 Console", "好");
        }

        /// <summary>记一条错误并打到 Console；context 用于点击定位文件或 Prefab。</summary>
        private static void Error(string message, UnityEngine.Object context)
        {
            _errorCount++;
            if (context != null)
                Debug.LogError("[CandyUI] " + message, context);
            else
                Debug.LogError("[CandyUI] " + message);
        }

        /// <summary>记一条警告并打到 Console；context 用于点击定位文件或 Prefab。</summary>
        private static void Warning(string message, UnityEngine.Object context)
        {
            _warningCount++;
            if (context != null)
                Debug.LogWarning("[CandyUI] " + message, context);
            else
                Debug.LogWarning("[CandyUI] " + message);
        }

        /// <summary>按类型找对应 .cs 资产用于 Console 定位；找不到返回 null（不影响校验结论）。</summary>
        internal static MonoScript FindScriptAsset(Type type)
        {
            if (ScriptCache.TryGetValue(type, out MonoScript cached))
                return cached;

            MonoScript result = null;
            string[] guids = AssetDatabase.FindAssets(type.Name + " t:MonoScript");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (script != null && script.GetClass() == type)
                {
                    result = script;
                    break;
                }
            }

            ScriptCache[type] = result;
            return result;
        }

        #endregion
    }
}
