// 职责：菜单工具，生成/修正 YooAsset 收集器配置，使 Demo 资源地址与 [UIPanel] 字符串一致。
// 依赖：YooAsset.Editor 的 BundleCollectorSetting / SettingLoader 与 Demo 的 Resources 目录。
// 限制：必须先执行 CandyUI/1 生成 Demo；只覆盖 DefaultPackage 包与 UIDemo 组。
using System;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace CandyUI.EditorTools
{
    /// <summary>
    /// YooAsset 一键配置（菜单 CandyUI/2）。
    ///
    /// 作用：
    /// 1) 创建/找到 Assets/BundleCollectorSetting.asset（YooAsset 的收集器配置）；
    /// 2) 建一个包 DefaultPackage；
    /// 3) 把 Demo 的 Resources 目录作为收集根（不移动文件，避免破坏 Resources 回退）；
    /// 4) 使用自定义地址规则 AddressByCandyUIDemoPath：
    ///    Assets/CandyUI/Samples/Demo/Resources/CandyUI/Demo/HomeWindow.prefab
    ///    -> CandyUI/Demo/HomeWindow
    ///    正好等于 [UIPanel("CandyUI/Demo/HomeWindow")] 里写的字符串，
    ///    所以业务代码一行都不用改。
    ///
    /// 为什么用 Editor 脚本而不是让用户手点窗口：
    /// 收集器配置是 ScriptableObject，字段很多且容易点错；
    /// 用代码生成可以保证包名、收集目录、地址规则三者永远一致。
    /// </summary>
    public static class YooAssetDemoSetup
    {
        private const string PackageName = "DefaultPackage";
        private const string GroupName = "UIDemo";
        private const string CollectPath = "Assets/CandyUI/Samples/Demo/Resources";

        /// <summary>
        /// 菜单入口：补全局配置，创建/修正 DefaultPackage 的收集器包、组与收集规则，使地址与 [UIPanel] 对齐。
        /// </summary>
        [MenuItem("CandyUI/2. Setup YooAsset Demo Package", priority = 20)]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(CollectPath))
            {
                Debug.LogError($"[CandyUI] 收集目录不存在：{CollectPath}。先执行 CandyUI/1 生成 Demo。");
                return;
            }

            // 第一次 Play 时如果缺这个资产，YooAsset 会打日志 "YooAssetSettings not found"，
            // 然后用默认配置（yoo 文件夹）。它本身不是错误，但会让日志看起来吓人，
            // 而且以后想改 YooFolderName/包名前缀时没地方改，所以这里顺手补出来。
            EnsureYooAssetSettings();

            // YooAsset 的规范入口：不存在 Assets/BundleCollectorSetting.asset 时会自动创建。
            BundleCollectorSetting setting = SettingLoader.LoadSettingData<BundleCollectorSetting>();
            if (setting == null)
            {
                Debug.LogError("[CandyUI] 无法创建/加载 BundleCollectorSetting。");
                return;
            }

            BundleCollectorPackage package = setting.Packages.Find(x => x.PackageName == PackageName);
            if (package == null)
            {
                package = new BundleCollectorPackage
                {
                    PackageName = PackageName,
                    PackageDesc = "CandyUI Demo 资源包",
                };
                setting.Packages.Add(package);
            }

            // 必须开启可寻址：否则 location 会退化成资源路径（Assets/...），
            // 与 [UIPanel] 里写的 "CandyUI/Demo/Xxx" 对不上。
            package.EnableAddressable = true;
            package.SupportExtensionless = true;
            package.AutoCollectShaders = true;

            BundleCollectorGroup group = package.Groups.Find(x => x.GroupName == GroupName);
            if (group == null)
            {
                group = new BundleCollectorGroup
                {
                    GroupName = GroupName,
                    GroupDesc = "CandyUI Demo 界面 Prefab",
                    ActiveRuleName = nameof(EnableGroup),
                };
                package.Groups.Add(group);
            }

            BundleCollector collector = group.Collectors.Find(x => x.CollectPath == CollectPath);
            if (collector == null)
            {
                collector = new BundleCollector { CollectPath = CollectPath };
                group.Collectors.Add(collector);
            }

            collector.CollectorGUID = AssetDatabase.AssetPathToGUID(CollectPath);
            collector.CollectorType = ECollectorType.MainAssetCollector;
            collector.AddressRuleName = nameof(AddressByCandyUIDemoPath);
            collector.PackRuleName = nameof(PackDirectory);
            collector.FilterRuleName = nameof(CollectAll);

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = setting;
            Debug.Log("[CandyUI] YooAsset 配置完成：包 DefaultPackage，收集目录 " + CollectPath +
                      "，地址规则 AddressByCandyUIDemoPath（CandyUI/Demo/Xxx）。");
            EditorUtility.DisplayDialog(
                "CandyUI · YooAsset",
                "配置完成：\n\n" +
                "包：DefaultPackage\n" +
                "收集目录：" + CollectPath + "\n" +
                "地址规则：CandyUI/Demo/Xxx\n\n" +
                "现在直接 Play 即可：DemoLauncher 会先尝试 YooAsset（编辑器模拟模式），失败自动回退 Resources。",
                "好");
        }

        /// <summary>
        /// 补出 Assets/Resources/YooAssetSettings.asset（YooAsset 全局配置）。
        /// 这个类型是 YooAsset 内部的 internal class，不能直接 new，
        /// 所以用反射拿类型 + 非公开构造创建；字段用默认值（YooFolderName = "yoo"）。
        /// </summary>
        private static void EnsureYooAssetSettings()
        {
            const string settingsPath = "Assets/Resources/YooAssetSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(settingsPath) != null)
                return;

            Type settingsType = typeof(YooAssets).Assembly.GetType("YooAsset.YooAssetSettings");
            if (settingsType == null)
            {
                Debug.LogWarning("[CandyUI] 找不到 YooAsset.YooAssetSettings 类型，跳过创建（YooAsset 会用默认配置）。");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var settings = Activator.CreateInstance(settingsType, nonPublic: true) as ScriptableObject;
            if (settings == null)
            {
                Debug.LogWarning("[CandyUI] 创建 YooAssetSettings 失败（构造器不可用），YooAsset 会用默认配置。");
                return;
            }

            AssetDatabase.CreateAsset(settings, settingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CandyUI] 已创建 YooAsset 全局配置：" + settingsPath);
        }
    }

    /// <summary>
    /// 把收集根路径去掉、去掉扩展名，得到和 [UIPanel] 一致的地址。
    /// 例：.../Resources/CandyUI/Demo/HomeWindow.prefab -> CandyUI/Demo/HomeWindow
    /// </summary>
    public class AddressByCandyUIDemoPath : IAddressRule
    {
        private const string CollectRoot = "Assets/CandyUI/Samples/Demo/Resources/";

        /// <summary>
        /// 去掉收集根路径与文件扩展名，得到与 [UIPanel] 地址一致的 location。
        /// </summary>
        string IAddressRule.GetAssetAddress(AddressRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/');
            if (path.StartsWith(CollectRoot))
                path = path.Substring(CollectRoot.Length);

            int dot = path.LastIndexOf('.');
            if (dot >= 0)
                path = path.Substring(0, dot);

            return path;
        }
    }
}
