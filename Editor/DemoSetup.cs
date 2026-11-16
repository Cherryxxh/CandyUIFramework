// 职责：菜单工具，生成 CandyUI Demo 的 10 个面板 Prefab 与演示场景。
// 依赖：UnityEditor API（PrefabUtility / EditorSceneManager）与 CandyUI.Demo 下的面板类型。
// 限制：生成物放在 Assets/CandyUIFramework/Samples/Demo 下；覆盖前先让用户确认保存当前场景。
using System.IO;
using CandyUI.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CandyUI.EditorTools
{
    /// <summary>
    /// Demo 一键生成工具。
    ///
    /// 为什么用 Editor 脚本生成 Prefab/场景，而不是手写 .prefab/.unity 的 YAML？
    /// 因为 YAML 里的 fileID、GUID、组件序列化字段非常容易写错，
    /// 一旦写错 Unity 不会报"文件错误"，而是默默显示成空物体，排查成本极高。
    /// 用 Unity 自己的 API 生成，GUID 和引用都由引擎负责，稳定得多。
    ///
    /// 这也是框架"快速开始"工具链的雏形：M5 会扩展成
    /// "新建项目 -> 菜单点一下 -> 自动生成 UIRoot 预制体/示例界面"。
    /// </summary>
    public static class DemoSetup
    {
        private const string DemoFolder = "Assets/CandyUIFramework/Samples/Demo";
        private const string ResourcesFolder = DemoFolder + "/Resources/CandyUI/Demo";
        private const string ScenePath = DemoFolder + "/CandyUIDemo.unity";

        /// <summary>
        /// 菜单入口：确认保存当前场景后生成全部 Demo 面板 Prefab 与演示场景，并弹窗说明用法。
        /// </summary>
        [MenuItem("CandyUI/1. Setup Demo (Create Prefabs + Scene)", priority = 0)]
        public static void SetupDemo()
        {
            // 当前场景如果有未保存修改，先让用户决定，避免被 NewScene 直接冲掉。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(ResourcesFolder);

            CreatePanelPrefab<HelloPanel>("HelloPanel");
            CreatePanelPrefab<HomeWindow>("HomeWindow");
            CreatePanelPrefab<SettingsWindow>("SettingsWindow");
            CreatePanelPrefab<MailWindow>("MailWindow");
            CreatePanelPrefab<BagPanel>("BagPanel");
            CreatePanelPrefab<ShopPanel>("ShopPanel");
            CreatePanelPrefab<ConfirmPopup>("ConfirmPopup");
            CreatePanelPrefab<RewardPopup>("RewardPopup");
            CreatePanelPrefab<ToastPanel>("ToastPanel");
            CreatePanelPrefab<TweenDemoWindow>("TweenDemoWindow");

            CreateDemoScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "CandyUI",
                "Demo 已生成：\n" + ScenePath +
                "\n\n点 Play：主城 -> 打开设置/邮件/背包 -> 弹窗队列 -> Toast；Esc 返回。\n" +
                "底部工具栏（F1 显示/隐藏）可以随时重新打开任何界面，并实时显示窗口栈/弹窗队列/遮罩。\n" +
                "工具栏的\"动效\"按钮演示 DOTween：金币滚动、卡片 Punch、方块逐个出现。",
                "好");
        }

        /// <summary>
        /// 生成一个面板 Prefab。面板的 UI 由脚本在 OnCreate 里用代码搭，
        /// 所以 Prefab 本身只需要 RectTransform + 面板脚本。
        /// </summary>
        private static void CreatePanelPrefab<T>(string prefabName) where T : UIPanel
        {
            string path = ResourcesFolder + "/" + prefabName + ".prefab";

            GameObject go = new GameObject(prefabName,
                typeof(RectTransform), typeof(T));

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// 新建只挂 DemoLauncher 的演示场景并保存到 Demo 目录，由它启动时初始化 UI 框架。
        /// </summary>
        private static void CreateDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single);

            GameObject launcher = new GameObject("DemoLauncher");
            launcher.AddComponent<DemoLauncher>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// AssetDatabase.CreateFolder 要求父目录已存在，所以递归创建。
        /// </summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                parent = parent.Replace('\\', '/');
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
