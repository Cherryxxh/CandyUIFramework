// 职责：扫描所有带 [UIPanel] 的面板类型，生成 PanelKeys 常量文件；域重载后自动检查，内容没变不写文件。
// 依赖：UnityEditor.TypeCache（类型索引，能扫到 Assembly-CSharp）、AssetDatabase（导入生成文件）、PanelValidator（生成后校验）。
// 约束：生成文件 UTF-8 无 BOM、4 空格缩进、LF 换行、按成员名 ordinal 排序，保证重复生成字节一致。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CandyUI
{
    /// <summary>
    /// 面板地址常量生成器：把 [UIPanel] 声明的地址生成为 PanelKeys 常量，业务代码不用再写裸字符串。
    ///
    /// 为什么用 UnityEditor.TypeCache：它是 Unity 维护的类型索引，能扫到 Assembly-CSharp 等动态编译程序集；
    /// 自己遍历 AppDomain.GetAssemblies() 反射会拖慢域重载。
    /// </summary>
    [InitializeOnLoad]
    public static class PanelKeyGenerator
    {
        /// <summary>生成文件所在目录；生成文件要提交进仓库，保证程序集首次编译就有 PanelKeys。</summary>
        internal const string GeneratedFolder = "Assets/CandyUIFramework/Runtime/Core/Generated";

        /// <summary>生成文件路径；该文件由本生成器独占，禁止手改。</summary>
        internal const string GeneratedFilePath = GeneratedFolder + "/PanelKeys.g.cs";

        /// <summary>UTF-8 无 BOM 编码，与仓库其它 .cs 文件一致。</summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        #region 初始化与菜单

        /// <summary>域重载后自动检查一次：内容没变化不写文件，避免触发无意义的重新导入。</summary>
        static PanelKeyGenerator()
        {
            EditorApplication.delayCall += GenerateIfChanged;
        }

        /// <summary>菜单入口：强制重新生成一次，并跑一遍面板校验（只打 Console，不弹窗）。</summary>
        [MenuItem("CandyUI/生成/面板地址常量", priority = 40)]
        public static void GenerateFromMenu()
        {
            Generate(forceWrite: true, validate: true);
        }

        #endregion

        #region 生成

        /// <summary>域重载后的自动检查入口；编译/导入未结束就下一帧再试，避免读到半成品程序集。</summary>
        private static void GenerateIfChanged()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += GenerateIfChanged;
                return;
            }

            Generate(forceWrite: false, validate: true);
        }

        /// <summary>
        /// 生成并（可选）校验：内容与磁盘一致且非强制时直接返回，不写文件也不重复导入。
        /// 强制生成时即使内容没变也会重新 ImportAsset 并打日志。
        /// </summary>
        internal static void Generate(bool forceWrite, bool validate)
        {
            List<PanelEntry> entries = CollectEntries(out _);
            string source = BuildSource(entries);
            string absolutePath = ToAbsolutePath(GeneratedFilePath);

            bool changed = !IsSameContent(absolutePath, source);
            if (!changed && !forceWrite)
                return;

            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllText(absolutePath, source, Utf8NoBom);
                AssetDatabase.ImportAsset(GeneratedFilePath, ImportAssetOptions.ForceUpdate);
            }

            Debug.Log($"[CandyUI] 已生成 {entries.Count} 个面板地址常量：{GeneratedFilePath}");

            if (validate)
                PanelValidator.RunInternal(showDialog: false);
        }

        #endregion

        #region 扫描与命名

        /// <summary>扫描所有 [UIPanel] 类型，筛选出可生成常量的面板并分配稳定成员名。</summary>
        internal static List<PanelEntry> CollectEntries(out List<string> namingConflicts)
        {
            var entries = new List<PanelEntry>();
            foreach (Type type in GetAttributedTypes())
            {
                if (!typeof(UIPanel).IsAssignableFrom(type) || type.IsAbstract)
                    continue;
                if (!TryGetAttribute(type, out UIPanelAttribute attribute, out _))
                    continue;
                if (string.IsNullOrWhiteSpace(attribute.PrefabAddress))
                    continue;
                entries.Add(new PanelEntry(type, attribute));
            }

            AssignMemberNames(entries, out namingConflicts);
            entries.Sort((left, right) => string.CompareOrdinal(left.MemberName, right.MemberName));
            return entries;
        }

        /// <summary>用 TypeCache 扫出所有带 [UIPanel] 的类型；按完整类型名 ordinal 排序保证遍历顺序稳定。</summary>
        internal static List<Type> GetAttributedTypes()
        {
            var types = new List<Type>(TypeCache.GetTypesWithAttribute<UIPanelAttribute>());
            types.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
            return types;
        }

        /// <summary>读取特性实例；构造参数非法（如空地址）会抛异常，这里捕获后交给校验器报错，不中断整次扫描。</summary>
        internal static bool TryGetAttribute(Type type, out UIPanelAttribute attribute, out Exception error)
        {
            try
            {
                attribute = type.GetCustomAttribute<UIPanelAttribute>(inherit: false);
                error = null;
                return attribute != null;
            }
            catch (Exception exception)
            {
                attribute = null;
                error = exception.GetBaseException();
                return false;
            }
        }

        /// <summary>
        /// 成员名规则：类型名唯一时直接用类型名；同名类型改用「命名空间最后一段_类型名」，并记录冲突给校验器报 Warning。
        /// </summary>
        private static void AssignMemberNames(List<PanelEntry> entries, out List<string> conflicts)
        {
            conflicts = new List<string>();
            var dicBySimpleName = new Dictionary<string, List<PanelEntry>>(StringComparer.Ordinal);
            foreach (PanelEntry entry in entries)
            {
                if (!dicBySimpleName.TryGetValue(entry.Type.Name, out List<PanelEntry> list))
                {
                    list = new List<PanelEntry>();
                    dicBySimpleName.Add(entry.Type.Name, list);
                }

                list.Add(entry);
            }

            foreach (KeyValuePair<string, List<PanelEntry>> pair in dicBySimpleName)
            {
                if (pair.Value.Count == 1)
                {
                    pair.Value[0].MemberName = pair.Key;
                    continue;
                }

                conflicts.Add($"面板类型名 {pair.Key} 重名（{FormatTypeNames(pair.Value)}），已改用「命名空间末段_类型名」作为常量名。");
                foreach (PanelEntry entry in pair.Value)
                    entry.MemberName = GetNamespaceLastSegment(entry.Type) + "_" + entry.Type.Name;
            }

            // 兜底：命名空间末段也可能相同，此时按完整类型名排序追加序号，保证成员名唯一且输出稳定。
            var dicByCandidate = new Dictionary<string, List<PanelEntry>>(StringComparer.Ordinal);
            foreach (PanelEntry entry in entries)
            {
                if (!dicByCandidate.TryGetValue(entry.MemberName, out List<PanelEntry> list))
                {
                    list = new List<PanelEntry>();
                    dicByCandidate.Add(entry.MemberName, list);
                }

                list.Add(entry);
            }

            foreach (KeyValuePair<string, List<PanelEntry>> pair in dicByCandidate)
            {
                if (pair.Value.Count <= 1)
                    continue;

                conflicts.Add($"面板常量名 {pair.Key} 加命名空间前缀后仍重名（{FormatTypeNames(pair.Value)}），已追加序号区分。");
                pair.Value.Sort((left, right) => string.CompareOrdinal(left.Type.FullName, right.Type.FullName));
                for (int index = 0; index < pair.Value.Count; index++)
                    pair.Value[index].MemberName = pair.Key + "_" + (index + 1);
            }
        }

        /// <summary>取命名空间最后一段；无命名空间时用 Global，保证候选名是合法标识符。</summary>
        private static string GetNamespaceLastSegment(Type type)
        {
            string nameSpace = type.Namespace;
            if (string.IsNullOrEmpty(nameSpace))
                return "Global";
            int lastDot = nameSpace.LastIndexOf('.');
            return lastDot < 0 ? nameSpace : nameSpace.Substring(lastDot + 1);
        }

        /// <summary>把类型列表拼成「全名、全名」形式，用于冲突提示信息。</summary>
        private static string FormatTypeNames(List<PanelEntry> entries)
        {
            var names = new List<string>();
            foreach (PanelEntry entry in entries)
                names.Add(entry.Type.FullName);
            return string.Join("、", names);
        }

        #endregion

        #region 生成文件与工具

        /// <summary>拼出 PanelKeys.g.cs 全文；不含时间戳等易变内容，保证同样输入得到同样字节。</summary>
        private static string BuildSource(List<PanelEntry> entries)
        {
            var builder = new StringBuilder();
            builder.Append("// <auto-generated>\n");
            builder.Append("// 本文件由 Assets/CandyUIFramework/Editor/PanelKeyGenerator.cs 自动生成，请勿手动修改。\n");
            builder.Append("// 修改面板地址后请执行菜单 CandyUI/生成/面板地址常量。\n");
            builder.Append("// </auto-generated>\n");
            builder.Append("namespace CandyUI\n{\n    public static class PanelKeys\n    {\n");
            foreach (PanelEntry entry in entries)
            {
                builder.Append("        public const string ").Append(entry.MemberName)
                    .Append(" = \"").Append(EscapeAddress(entry.PrefabAddress)).Append("\";\n");
            }

            builder.Append("    }\n}\n");
            return builder.ToString();
        }

        /// <summary>转义地址里的反斜杠和双引号，保证生成的字符串字面量合法。</summary>
        private static string EscapeAddress(string address)
        {
            return address.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>把 "Assets/..." 资源路径转成绝对路径；不依赖 Unity 进程的工作目录。</summary>
        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>逐字节比较磁盘文件与期望内容，BOM/换行差异也算不同，避免静默残留不一致。</summary>
        private static bool IsSameContent(string absolutePath, string expected)
        {
            if (!File.Exists(absolutePath))
                return false;

            byte[] actual = File.ReadAllBytes(absolutePath);
            byte[] wanted = Utf8NoBom.GetBytes(expected);
            if (actual.Length != wanted.Length)
                return false;

            for (int index = 0; index < actual.Length; index++)
            {
                if (actual[index] != wanted[index])
                    return false;
            }

            return true;
        }

        #endregion

        /// <summary>扫描到的一条面板记录：类型、特性、地址与要生成的常量名。</summary>
        internal sealed class PanelEntry
        {
            /// <summary>构造一条面板记录，并从特性里取地址。</summary>
            internal PanelEntry(Type type, UIPanelAttribute attribute)
            {
                Type = type;
                Attribute = attribute;
                PrefabAddress = attribute.PrefabAddress;
            }

            /// <summary>面板类型。</summary>
            internal Type Type { get; }

            /// <summary>类型上的 [UIPanel] 实例，校验 Layer 等配置时要用。</summary>
            internal UIPanelAttribute Attribute { get; }

            /// <summary>面板资源地址（声明值，可能为空）。</summary>
            internal string PrefabAddress { get; }

            /// <summary>生成的常量成员名。</summary>
            internal string MemberName { get; set; }
        }
    }
}
