// 职责：EditMode 测试基类，统一准备 UIRoot/EventSystem、假资源加载器与用例间清理。
// 依赖：反射调用 UIManager.ResetStatics、UIRoot.InitUIRoot 等 internal 入口。
// 限制：仅适用于 EditMode；异步等待靠泵 UnitySynchronizationContext 推进，不用真实帧计时。
using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CandyUI.Tests
{
    /// <summary>
    /// EditMode 测试基类。
    /// 每个用例前：重置 UIManager 三个服务的静态状态、注入 FakeAssetLoader、清理残留面板与遮罩。
    /// 每个用例后：强制关闭所有面板、释放假资源、再次重置服务。
    /// 注意：EditMode 下 Object.Destroy 不会真正销毁对象（Unity 会报 "Destroy may not be called from edit mode"），
    /// 所以断言不依赖"GameObject 真被销毁"，而是检查框架可观察状态（注销、State、句柄释放计数）。
    /// </summary>
    public abstract class UITestBase
    {
        protected const float WaitTimeoutSeconds = 15f;

        private static MethodInfo s_syncContextExecMethod;
        private static Type s_syncContextExecType;

        /// <summary>当前用例注入的假资源加载器，SetUp 中创建、TearDown 中清理资源。</summary>
        protected FakeAssetLoader Loader { get; private set; }

        /// <summary>
        /// 每个用例前重置框架静态状态、注入 FakeAssetLoader，并清掉上个用例残留的面板与遮罩。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            // EditMode 下框架调用 Object.Destroy / DontDestroyOnLoad 会打错误日志（该环境无效但会被测试框架判失败）。
            LogAssert.ignoreFailingMessages = true;
            UIManager.DebugLogEnabled = false;

            ResetFrameworkStatics();
            CleanupLeftoverPanelObjects();
            HideLeftoverPopupMask();
            EnsureEditModeSafeUIRootAndEventSystem();

            TestPanelBase.ResetSwitches();
            Loader = new FakeAssetLoader(typeof(UITestBase).Assembly);
            UIManager.InitializeAssetLoader(Loader);
            Assert.IsTrue(UIManager.IsLoaderReady);
        }

        /// <summary>
        /// 每个用例后关闭所有面板、释放假资源并再次重置服务，避免状态泄漏到下一个用例。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // 清理阶段也会走框架的 Object.Destroy 路径（EditMode 下每次关闭面板都会打一条 Error 日志），
            // test-framework 的 TearDown 也在独立的 LogScope 里，所以这里需要再次打开忽略。
            LogAssert.ignoreFailingMessages = true;
            try
            {
                UIManager.CloseAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            // 先重置服务（释放资源缓存里的句柄），再清 GameObject，避免句柄/实例互相引用残留。
            ResetFrameworkStatics();
            CleanupLeftoverPanelObjects();
            if (Loader != null) Loader.DestroyAllAssets();

            UIManager.DebugLogEnabled = true;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// EditMode 下框架关闭面板会调用 Object.Destroy，Unity 每次都打一条 Error 日志
        /// （"Destroy may not be called from edit mode!"）。test-framework 会为每个测试方法体、
        /// SetUp、TearDown 分别建立独立的 LogScope，SetUp 里设置的 LogAssert.ignoreFailingMessages
        /// 不会继承到方法体，所以每个用例开头都要显式调用一次。
        /// </summary>
        protected static void IgnoreEditModeObjectDestroyLogs()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>GetDebugSnapshot 的三行输出：0=面板, 1=窗口栈, 2=弹窗。</summary>
        protected static string GetDebugLine(int index)
        {
            string[] lines = UIManager.GetDebugSnapshot().Split('\n');
            Assert.Greater(lines.Length, index, "GetDebugSnapshot 输出行数不足");
            return lines[index];
        }

        /// <summary>
        /// 反射调用 UIManager.ResetStatics，把三个服务的静态状态恢复到未初始化。
        /// </summary>
        private static void ResetFrameworkStatics()
        {
            MethodInfo method = typeof(UIManager).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "找不到 UIManager.ResetStatics");
            method.Invoke(null, null);
        }

        /// <summary>
        /// 立即销毁场景残留的面板对象；EditMode 下 Object.Destroy 不生效，所以用 DestroyImmediate。
        /// </summary>
        private static void CleanupLeftoverPanelObjects()
        {
            UIPanel[] panels = Object.FindObjectsOfType<UIPanel>(true);
            foreach (UIPanel panel in panels)
            {
                if (panel != null) Object.DestroyImmediate(panel.gameObject);
            }
        }

        /// <summary>上次用例异常中断时遮罩可能仍是激活状态；它不是 UIPanel，需要单独复位。</summary>
        private static void HideLeftoverPopupMask()
        {
            foreach (MonoBehaviour behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "PopupMask") continue;
                behaviour.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// EditMode 兼容：框架的 UIRoot.EnsureUIRoot / EnsureEventSystem 在"首次创建"时会无条件调用
        /// Object.DontDestroyOnLoad，而 EditMode 下该调用会抛 InvalidOperationException。
        /// 测试在打开任何面板前先自行创建 UIRoot 与 EventSystem：
        /// - UIRoot 用反射调用其 internal InitUIRoot()（内部只在 Application.isPlaying 时才 DontDestroyOnLoad）；
        /// - EventSystem 预先放进场景，框架的 FindObjectsOfType 分支会直接复用它，不再创建。
        /// 两者跨用例复用，不随 TearDown 销毁。
        /// </summary>
        private static void EnsureEditModeSafeUIRootAndEventSystem()
        {
            // 必须先建 EventSystem：UIRoot.InitUIRoot 内部会调 EnsureEventSystem，
            // 如果那时场景里还没有 EventSystem，框架会新建并 DontDestroyOnLoad（EditMode 下抛异常）。
            EnsureEventSystemForEditMode();
            EnsureUIRootForEditMode();
        }

        /// <summary>
        /// 反射创建并初始化 UIRoot 单例，绕过 EditMode 下会抛异常的 DontDestroyOnLoad 路径。
        /// </summary>
        private static void EnsureUIRootForEditMode()
        {
            string namespaceName = typeof(UIManager).Namespace;
            Type uiRootType = typeof(UIManager).Assembly.GetType(namespaceName + ".UIRoot", false);
            if (uiRootType == null) return;

            PropertyInfo instanceProperty = uiRootType.GetProperty(
                "Instance", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (instanceProperty != null && instanceProperty.GetValue(null) != null) return;

            GameObject rootObject = new GameObject("[" + namespaceName + "] EditModeUIRoot", typeof(RectTransform));
            rootObject.AddComponent(uiRootType);

            MethodInfo initMethod = uiRootType.GetMethod(
                "InitUIRoot", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(initMethod, "找不到 UIRoot.InitUIRoot（EditMode 兼容准备失败）");
            initMethod.Invoke(rootObject.GetComponent(uiRootType), null);

            Assert.NotNull(instanceProperty, "找不到 UIRoot.Instance（EditMode 兼容准备失败）");
            Assert.NotNull(instanceProperty.GetValue(null), "UIRoot.Instance 仍为空（EditMode 兼容准备失败）");
        }

        /// <summary>
        /// 预先在场景里放好 EventSystem，避免 UIRoot 初始化时自行创建并调用 DontDestroyOnLoad。
        /// </summary>
        private static void EnsureEventSystemForEditMode()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            if (Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>(true).Length > 0) return;
            new GameObject("[CandyUI] EditModeEventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        }

        /// <summary>推进一次 UnitySynchronizationContext 的续体队列。
        /// EditMode 下 UnityTest 只会 yield null，因此用反射调用 UnitySynchronizationContext.Exec 主动排空，
        /// 让 Manual 模式（TCS 延迟完成）的异步链路在断言前确定性地跑完。</summary>
        protected static void PumpOnce()
        {
            SynchronizationContext context = SynchronizationContext.Current;
            if (context == null) return;

            Type contextType = context.GetType();
            if (s_syncContextExecMethod == null || s_syncContextExecType != contextType)
            {
                s_syncContextExecType = contextType;
                s_syncContextExecMethod = contextType.GetMethod("Exec", BindingFlags.Public | BindingFlags.Instance);
            }

            if (s_syncContextExecMethod == null) return;
            s_syncContextExecMethod.Invoke(context, null);
            s_syncContextExecMethod.Invoke(context, null);
        }

        /// <summary>
        /// 返回本次等待的截止时间（当前时间 + WaitTimeoutSeconds），供 AdvanceWait 判定超时。
        /// </summary>
        protected static float BeginWait()
        {
            return Time.realtimeSinceStartup + WaitTimeoutSeconds;
        }

        /// <summary>[UnityTest] 轮询用：主动泵一次续体队列并检查条件；未满足且超时则断言失败。</summary>
        protected static bool AdvanceWait(Func<bool> condition, string what, float deadline)
        {
            PumpOnce();
            if (condition()) return true;
            Assert.Less(Time.realtimeSinceStartup, deadline, $"等待超时（{WaitTimeoutSeconds}s）：{what}");
            return false;
        }

        /// <summary>
        /// 反射读取指定类型的非公开静态字段；找不到时直接断言失败并给出类型与字段名。
        /// </summary>
        protected static object GetStaticField(Type owner, string fieldName)
        {
            FieldInfo field = owner.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field, $"找不到静态字段 {owner.Name}.{fieldName}");
            return field.GetValue(null);
        }

        /// <summary>
        /// 反射读取实例的非公开字段，用于检查 PanelService / AssetCache 等 internal 状态。
        /// </summary>
        protected static object GetInstanceField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"找不到实例字段 {target.GetType().Name}.{fieldName}");
            return field.GetValue(target);
        }

        /// <summary>
        /// 反射读取实例属性（含非公开），找不到时断言失败并输出类型与属性名。
        /// </summary>
        protected static object GetInstanceProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property, $"找不到实例属性 {target.GetType().Name}.{propertyName}");
            return property.GetValue(target);
        }

        /// <summary>
        /// 反射调用实例方法（含非公开），用于驱动 AssetCache 等 internal 类型的加载/释放接口。
        /// </summary>
        protected static object InvokeInstanceMethod(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(method, $"找不到实例方法 {target.GetType().Name}.{methodName}");
            return method.Invoke(target, arguments);
        }

        /// <summary>拿到 PanelService 内部的 AssetCache（internal 类型，用反射直测引用计数）。</summary>
        protected static object GetAssetCache()
        {
            object panelService = GetStaticField(typeof(UIManager), "s_panels");
            object cache = GetInstanceField(panelService, "_cache");
            Assert.NotNull(cache, "AssetCache 尚未创建（是否漏了 InitializeAssetLoader？）");
            return cache;
        }
    }
}
