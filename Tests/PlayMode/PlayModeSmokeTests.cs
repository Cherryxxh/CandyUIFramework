using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CandyUI.PlayModeTests
{
    /// <summary>
    /// PlayMode 冒烟测试：用真实的 Resources 加载器 + 真实的 Demo Prefab 跑一遍
    /// "打开 -> 显示 -> 关闭 -> 销毁 / 入缓存 / 恢复"的完整链路。
    ///
    /// 它补的是 EditMode 覆盖不到的部分：真实 Instantiate/Destroy、协程驱动的时间轴、
    /// 真实的 DontDestroyOnLoad、以及 UI 控件（TMP/UGUI）在 Play 模式下的实际创建。
    /// </summary>
    public sealed class PlayModeSmokeTests
    {
        private const string HelloAddress = "CandyUI/Demo/HelloPanel";
        private const string HomeAddress = "CandyUI/Demo/HomeWindow";
        private const string SettingsAddress = "CandyUI/Demo/SettingsWindow";
        private const string ConfirmAddress = "CandyUI/Demo/ConfirmPopup";
        private const string RewardAddress = "CandyUI/Demo/RewardPopup";

        private static Dictionary<string, Type> s_panelTypes;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            UIManager.InitializeAssetLoader(new ResourcesAssetLoader());
            UIManager.DefaultTransition = new FadeScaleTransition
            {
                InDuration = 0.02f,
                OutDuration = 0.02f,
            };

            // 让 UIRoot 先建出来，后面的用例都基于同一个根节点。
            MethodInfo ensure = typeof(UIRoot).GetMethod(
                "EnsureUIRoot", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(ensure, "找不到 UIRoot.EnsureUIRoot");
            ensure.Invoke(null, null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UIManager.CloseAll();
            yield return null;
            yield return null;
        }

        /// <summary>Resources 里的真实 Prefab 能加载、能打开、能播入场、状态正确。</summary>
        [UnityTest]
        public IEnumerator RealPrefab_Opens_And_ReachesVisible()
        {
            Assert.IsNotNull(Resources.Load<GameObject>(HelloAddress), "Demo Prefab 不存在：" + HelloAddress);

            UIPanel panel = null;
            yield return OpenPlain(HelloAddress, p => panel = p);

            Assert.IsNotNull(panel);
            Assert.AreEqual(UIPanelState.Visible, panel.State);
            Assert.IsNull(panel.InstanceName, "默认实例的实例名应为 null");
            Assert.IsNotNull(panel.transform.parent, "面板应挂在 UIRoot 的层容器下");
            Assert.IsNotNull(panel.transform.parent.parent.GetComponent<UIRoot>(),
                "面板的祖父节点应该是 UIRoot");
        }

        /// <summary>ReleaseOnClose 的面板：CloseAsync 播完退场后被真正销毁。</summary>
        [UnityTest]
        public IEnumerator ReleaseOnClose_CloseAsync_DestroysInstance()
        {
            UIPanel panel = null;
            yield return OpenPlain(HelloAddress, p => panel = p);
            Assert.IsNotNull(panel);

            Task<bool> closing = UIManager.CloseAsync(panel);
            Assert.AreEqual(UIPanelState.Closing, panel.State, "关闭应立刻进入 Closing（退场中）");
            yield return Await(closing);
            yield return null;
            yield return null;

            Assert.IsTrue(panel == null, "ReleaseOnClose 的面板关闭后应被销毁");
            StringAssert.Contains("打开 0", UIManager.GetDebugSnapshot());
        }

        /// <summary>窗口栈：打开第二个窗口，NavigateBack 能恢复上一个窗口且不重新加载。</summary>
        [UnityTest]
        public IEnumerator WindowStack_NavigateBack_RestoresPrevious()
        {
            UIPanel home = null;
            yield return OpenWindow(HomeAddress, p => home = p);
            Assert.IsNotNull(home);
            Assert.AreEqual(UIPanelState.Visible, home.State);

            UIPanel settings = null;
            yield return OpenWindow(SettingsAddress, p => settings = p);
            Assert.IsNotNull(settings);
            Assert.AreEqual(UIPanelState.Visible, settings.State);
            Assert.IsTrue(UIManager.CanNavigateBack, "栈深 2 时应能返回");

            int homeInstanceId = home.GetInstanceID();
            UIManager.NavigateBack();
            yield return WaitUntil(() => settings == null || settings.State == UIPanelState.Closed, 10f);

            Assert.IsNotNull(home, "主城窗口不应被销毁");
            Assert.AreEqual(homeInstanceId, home.GetInstanceID(), "返回应复用原实例，而不是重新创建");
            Assert.AreEqual(UIPanelState.Visible, home.State, "返回后主城应恢复显示");
            Assert.IsFalse(UIManager.CanNavigateBack, "栈深回到 1 后不应再能返回");
        }

        /// <summary>弹窗：同类型重复请求直接复用当前弹窗，不进队列。</summary>
        [UnityTest]
        public IEnumerator Popup_SameType_ReusesActiveInstance()
        {
            UIPanel first = null;
            yield return OpenPopup(ConfirmAddress, p => first = p);
            Assert.IsNotNull(first);
            Assert.AreEqual(UIPanelState.Visible, first.State);

            Task second = InvokeGeneric("OpenPopupAsync", ResolvePanelType(ConfirmAddress), null, CancellationToken.None);
            yield return Await(second);
            Assert.AreSame(first, ResultOf(second), "同类型弹窗在显示时，第二次请求应复用同一实例而不是排队");
            StringAssert.Contains("打开 1", UIManager.GetDebugSnapshot());
        }

        /// <summary>弹窗：另一个弹窗在显示时，新请求排队返回 null，关掉当前后队列自动推进。</summary>
        [UnityTest]
        public IEnumerator Popup_DifferentType_QueuesAndAdvances()
        {
            UIPanel first = null;
            yield return OpenPopup(ConfirmAddress, p => first = p);
            Assert.IsNotNull(first);

            Task second = InvokeGeneric("OpenPopupAsync", ResolvePanelType(RewardAddress), null, CancellationToken.None);
            yield return Await(second);
            Assert.IsNull(ResultOf(second), "已有弹窗在显示时，另一个弹窗请求应排队并返回 null");

            UIManager.CloseTopPopup();
            yield return new WaitForSecondsRealtime(0.3f);
            StringAssert.Contains("打开 1", UIManager.GetDebugSnapshot(), "队列应已推进到第二个弹窗");

            UIManager.CloseAllPopups();
            yield return new WaitForSecondsRealtime(0.3f);
            StringAssert.Contains("打开 0", UIManager.GetDebugSnapshot());
        }

        /// <summary>同类型多实例：不同实例名同时存在，实例名体现在节点名上。</summary>
        [UnityTest]
        public IEnumerator MultiInstance_SameType_TwoNames()
        {
            UIPanel left = null;
            UIPanel right = null;
            yield return OpenPlain(HelloAddress, p => left = p, "left");
            yield return OpenPlain(HelloAddress, p => right = p, "right");

            Assert.IsNotNull(left);
            Assert.IsNotNull(right);
            Assert.AreNotSame(left, right);
            Assert.AreEqual("left", left.InstanceName);
            Assert.AreEqual("right", right.InstanceName);
            Assert.AreEqual("HelloPanel_left", left.gameObject.name);
            Assert.AreEqual("HelloPanel_right", right.gameObject.name);
        }

        /// <summary>UIRoot 在 PlayMode 下确实挂在 DontDestroyOnLoad 场景，切场景不会丢。</summary>
        [UnityTest]
        public IEnumerator Root_LivesInDontDestroyOnLoad()
        {
            yield return null;
            GameObject root = GameObject.Find("[CandyUI] UIRoot");
            Assert.IsNotNull(root, "找不到 UIRoot 根节点");
            Assert.AreEqual("DontDestroyOnLoad", root.scene.name);
        }

        /// <summary>CloseAll 能一次清空所有面板（KeepInstance 的进缓存，不残留打开状态）。</summary>
        [UnityTest]
        public IEnumerator CloseAll_ClearsEverything()
        {
            yield return OpenPlain(HelloAddress, _ => { }, "x");
            yield return OpenWindow(HomeAddress, _ => { });
            yield return OpenPopup(ConfirmAddress, _ => { });

            UIManager.CloseAll();
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.2f);

            StringAssert.Contains("打开 0", UIManager.GetDebugSnapshot());
        }

        #region 反射与等待辅助

        private static IEnumerator OpenPlain(string address, Action<UIPanel> onDone, string instanceName = null)
        {
            Task task = instanceName == null
                ? InvokeGeneric("OpenPlainPanelAsync", ResolvePanelType(address), null, CancellationToken.None)
                : InvokeGeneric("OpenPlainPanelAsync", ResolvePanelType(address), instanceName, null, CancellationToken.None);
            yield return Await(task);
            onDone(ResultOf(task) as UIPanel);
        }

        private static IEnumerator OpenWindow(string address, Action<UIPanel> onDone)
        {
            Task task = InvokeGeneric("OpenWindowAsync", ResolvePanelType(address), null, CancellationToken.None);
            yield return Await(task);
            onDone(ResultOf(task) as UIPanel);
        }

        private static IEnumerator OpenPopup(string address, Action<UIPanel> onDone)
        {
            Task task = InvokeGeneric("OpenPopupAsync", ResolvePanelType(address), null, CancellationToken.None);
            yield return Await(task);
            onDone(ResultOf(task) as UIPanel);
        }

        private static Task InvokeGeneric(string methodName, Type panelType, params object[] args)
        {
            MethodInfo method = typeof(UIManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == methodName
                                     && m.IsGenericMethodDefinition
                                     && m.GetParameters().Length == args.Length);
            Assert.IsNotNull(method, "找不到泛型方法 " + methodName + "（参数数量 " + args.Length + "）");
            return (Task)method.MakeGenericMethod(panelType).Invoke(null, args);
        }

        private static object ResultOf(Task task)
        {
            return task.GetType().GetProperty("Result").GetValue(task);
        }

        private static IEnumerator Await(Task task, float timeout = 15f)
        {
            float elapsed = 0f;
            while (!task.IsCompleted)
            {
                if (elapsed > timeout)
                {
                    Assert.Fail("等待任务超时：" + task);
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (task.IsFaulted && task.Exception != null)
            {
                throw task.Exception;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float elapsed = 0f;
            while (!condition())
            {
                if (elapsed > timeout)
                {
                    Assert.Fail("等待条件超时：" + condition);
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static Type ResolvePanelType(string address)
        {
            if (s_panelTypes == null)
            {
                s_panelTypes = new Dictionary<string, Type>();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(t => t != null).ToArray();
                    }

                    foreach (Type type in types)
                    {
                        if (!typeof(UIPanel).IsAssignableFrom(type) || type.IsAbstract)
                        {
                            continue;
                        }

                        var attribute = Attribute.GetCustomAttribute(type, typeof(UIPanelAttribute), false) as UIPanelAttribute;
                        if (attribute != null)
                        {
                            s_panelTypes[attribute.PrefabAddress] = type;
                        }
                    }
                }
            }

            Assert.IsTrue(s_panelTypes.TryGetValue(address, out Type panelType),
                "找不到地址为 " + address + " 的面板类型（Demo 脚本是否已编译？）");
            return panelType;
        }

        #endregion
    }
}
