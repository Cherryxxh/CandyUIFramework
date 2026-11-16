// 职责：验证 YooAssetLoader 适配器的真实链路（编辑器模拟包 -> 加载 Demo Prefab -> 面板生命周期）。
// 依赖：YooAssetTestBootstrap、Demo 的 Resources/CandyUI/Demo 下的真实 Prefab。
// 限制：只跑编辑器模拟模式；真机联机/真 AB 包不在覆盖范围内。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CandyUI.YooAsset;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CandyUI.PlayModeTests
{
    /// <summary>
    /// YooAsset 适配器冒烟：确认框架确实在用 YooAsset 加载器、地址能命中真实 Prefab、
    /// 关闭后实例被销毁、不存在的地址会带上下文报错。
    /// </summary>
    public sealed class YooAssetLoaderSmokeTests
    {
        private const string HelloAddress = "CandyUI/Demo/HelloPanel";
        private const string HomeAddress = "CandyUI/Demo/HomeWindow";

        private static Dictionary<string, Type> s_panelTypes;

        [UnitySetUp]
        public IEnumerator InitOnce()
        {
            yield return Await(YooAssetTestBootstrap.EnsureInitializedAsync());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UIManager.CloseAll();
            yield return null;
            yield return null;
        }

        /// <summary>初始化成功时，框架拿到的必须是 YooAsset 适配器，而不是 Resources 回退。</summary>
        [UnityTest]
        public IEnumerator Loader_Is_YooAssetLoader()
        {
            RequireReady();
            Assert.IsInstanceOf<YooAssetLoader>(UIManager.AssetLoader,
                "UIManager.AssetLoader 应是 YooAssetLoader");
            Assert.AreEqual(YooAssetTestBootstrap.PackageName,
                ((YooAssetLoader)UIManager.AssetLoader).Package.PackageName);
            yield return null;
        }

        /// <summary>通过 YooAsset 地址加载真实 Prefab，打开到 Visible，关闭后实例被销毁。</summary>
        [UnityTest]
        public IEnumerator RealPrefab_ThroughYooAsset_OpensAndCloses()
        {
            RequireReady();

            UIPanel panel = null;
            yield return OpenPlain(HelloAddress, p => panel = p);

            Assert.IsNotNull(panel, "面板应通过 YooAsset 打开");
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsNotNull(panel.transform.parent, "面板应挂在 UIRoot 的层容器下");

            yield return Await(UIManager.CloseAsync(panel));
            // Destroy 是延迟到帧末执行的，等一帧再判断"已被销毁"。
            yield return null;
            yield return null;

            Assert.IsTrue(panel == null, "ReleaseOnClose 面板关闭后应被销毁");
            StringAssert.DoesNotContain("HelloPanel", UIManager.GetDebugSnapshot(),
                "关闭后不应再出现在调试快照里");
        }

        /// <summary>预加载走 YooAsset：加载过的窗口再打开时能直接显示并入栈。</summary>
        [UnityTest]
        public IEnumerator Preload_ThenOpen_ThroughYooAsset()
        {
            RequireReady();

            yield return Await(InvokePreload(HomeAddress));

            UIPanel panel = null;
            yield return OpenWindow(HomeAddress, p => panel = p);

            Assert.IsNotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            StringAssert.Contains("HomeWindow", UIManager.GetDebugSnapshot(),
                "窗口打开后应出现在窗口栈快照里");
        }

        /// <summary>地址不存在时应抛出带 YooAsset 上下文与地址的异常，而不是静默返回 null。</summary>
        [UnityTest]
        public IEnumerator MissingAddress_ThrowsWithContext()
        {
            RequireReady();

            LogAssert.ignoreFailingMessages = true;
            Task<IAssetHandle> task = UIManager.AssetLoader.LoadAssetAsync("CandyUI/Demo/__MissingForTest");
            while (!task.IsCompleted)
                yield return null;
            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(task.IsFaulted, "加载不存在的地址应进入异常分支");
            Exception captured = task.Exception?.GetBaseException();
            Assert.IsNotNull(captured);
            StringAssert.Contains("YooAsset", captured.Message);
            StringAssert.Contains("__MissingForTest", captured.Message);
        }

        /// <summary>YooAsset 包没准备好时跳过，而不是让整个套件失败。</summary>
        private static void RequireReady()
        {
            if (!YooAssetTestBootstrap.IsReady)
                Assert.Ignore("YooAsset 模拟包未就绪：" + YooAssetTestBootstrap.Error);
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;
        }

        private static IEnumerator OpenPlain(string address, Action<UIPanel> onOpened)
        {
            return OpenGeneric("OpenPlainPanelAsync", address, onOpened);
        }

        private static IEnumerator OpenWindow(string address, Action<UIPanel> onOpened)
        {
            return OpenGeneric("OpenWindowAsync", address, onOpened);
        }

        private static IEnumerator OpenGeneric(string methodName, string address, Action<UIPanel> onOpened)
        {
            Type type = ResolvePanelType(address);
            Task task = InvokeGeneric(methodName, type,
                new object[] { null, null, default(System.Threading.CancellationToken) });

            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;

            onOpened((UIPanel)task.GetType().GetProperty("Result").GetValue(task));
        }

        private static Task InvokePreload(string address)
        {
            Type type = ResolvePanelType(address);
            return (Task)InvokeGeneric("PreloadAsync", type,
                new object[] { default(System.Threading.CancellationToken) });
        }

        /// <summary>按 [UIPanel] 地址找 Demo 面板类型（Demo 在 Assembly-CSharp，测试程序集引用不到）。</summary>
        private static Type ResolvePanelType(string address)
        {
            if (s_panelTypes == null)
            {
                s_panelTypes = new Dictionary<string, Type>();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException) { continue; }

                    foreach (Type type in types)
                    {
                        if (!typeof(UIPanel).IsAssignableFrom(type)) continue;
                        var attribute = (UIPanelAttribute)Attribute.GetCustomAttribute(type, typeof(UIPanelAttribute));
                        if (attribute == null) continue;
                        s_panelTypes[attribute.PrefabAddress] = type;
                    }
                }
            }

            if (!s_panelTypes.TryGetValue(address, out Type panelType))
                Assert.Fail("找不到地址为 " + address + " 的 [UIPanel] 类型");
            return panelType;
        }

        /// <summary>按方法名 + 参数个数找 UIManager 上的泛型方法，再代入面板类型调用。</summary>
        private static Task InvokeGeneric(string methodName, Type panelType, object[] arguments)
        {
            MethodInfo method = typeof(UIManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName && m.IsGenericMethodDefinition)
                .FirstOrDefault(m => m.GetParameters().Length == arguments.Length);

            Assert.IsNotNull(method, "找不到 UIManager." + methodName);
            return (Task)method.MakeGenericMethod(panelType).Invoke(null, arguments);
        }
    }
}
