// 职责：验证默认上下文拒绝 Dispose、UIRoot 新建 EventSystem 时遵循配置（不误调 DontDestroyOnLoad）。
// 依赖：UITestBase（静态状态重置与假加载器）、UIFramework.Create、UIRootConfig、EventSystem。
// 限制：仅 EditMode；Application.isPlaying 为 false，只能验证"不抛异常 + 状态正确"，无法验证真实跨场景常驻。
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CandyUI.Tests
{
    /// <summary>
    /// 上下文与根节点的生命周期守卫：默认上下文不可释放，EventSystem 的创建方式跟随 UIRootConfig。
    /// </summary>
    public class ContextGuardTests : UITestBase
    {
        /// <summary>默认上下文被静态 API 依赖，Dispose 必须抛异常且不破坏现有状态。</summary>
        [Test]
        public void DefaultContext_Dispose_ThrowsAndStaysUsable()
        {
            IgnoreEditModeObjectDestroyLogs();

            UIFramework framework = UIFramework.Default;
            Assert.IsNotNull(framework, "默认上下文应可用");
            Assert.Throws<InvalidOperationException>(() => framework.Dispose(), "默认上下文不允许 Dispose");
            Assert.AreSame(framework, UIFramework.Default, "拒绝 Dispose 后默认上下文应原样保留");
            Assert.IsTrue(UIManager.IsLoaderReady, "拒绝 Dispose 后加载器状态不应被清空");
        }

        /// <summary>EditMode 下自动创建 EventSystem 不应调用 DontDestroyOnLoad，否则会抛异常导致初始化失败。</summary>
        [Test]
        public void CreateRoot_AutoEventSystem_DoesNotThrowInEditMode()
        {
            IgnoreEditModeObjectDestroyLogs();
            DestroyAllEventSystems();

            UIFramework framework = null;
            try
            {
                framework = UIFramework.Create(new UIRootConfig
                {
                    CreateEventSystemIfMissing = true,
                    DontDestroyOnLoad = true,
                });

                Assert.IsNotNull(framework.Root, "应创建出新的 UIRoot");
                // EditMode 下 EventSystem 的 OnEnable 不会执行，EventSystem.current 仍为 null，只能按场景实例数断言。
                Assert.AreEqual(1, Object.FindObjectsOfType<EventSystem>(true).Length, "配置要求自动创建时应建出一个 EventSystem");
            }
            finally
            {
                framework?.Dispose();
                DestroyAllEventSystems();
            }
        }

        /// <summary>配置关闭自动创建时，不应凭空生成 EventSystem。</summary>
        [Test]
        public void CreateRoot_EventSystemDisabled_DoesNotCreate()
        {
            IgnoreEditModeObjectDestroyLogs();
            DestroyAllEventSystems();

            UIFramework framework = null;
            try
            {
                framework = UIFramework.Create(new UIRootConfig { CreateEventSystemIfMissing = false });
                Assert.IsNotNull(framework.Root, "应创建出新的 UIRoot");
                Assert.AreEqual(0, Object.FindObjectsOfType<EventSystem>(true).Length, "配置关闭自动创建时不应生成 EventSystem");
            }
            finally
            {
                framework?.Dispose();
                DestroyAllEventSystems();
            }
        }

        /// <summary>清空场景里的 EventSystem，让用例从"没有 EventSystem"的初始状态开始。</summary>
        private static void DestroyAllEventSystems()
        {
            EventSystem[] systems = Object.FindObjectsOfType<EventSystem>(true);
            foreach (EventSystem system in systems)
            {
                if (system != null)
                    Object.DestroyImmediate(system.gameObject);
            }
        }
    }
}
