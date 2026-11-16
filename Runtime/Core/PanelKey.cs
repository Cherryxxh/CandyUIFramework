using System;

namespace CandyUI
{
    /// <summary>
    /// 面板实例键：面板类型 + 实例名，是打开表/缓存表/在途打开表的唯一索引。
    /// instanceName 为 null 或空白时表示"默认实例"，与不传实例名的旧 API 完全等价；
    /// 用值类型做字典 key，避免每次打开/查询都拼字符串与装箱。
    /// </summary>
    public readonly struct PanelKey : IEquatable<PanelKey>
    {
        /// <summary>面板类型；构造时校验非 null。</summary>
        public readonly Type PanelType;

        /// <summary>实例名；null 表示默认实例（同一类型只保留一个）。</summary>
        public readonly string InstanceName;

        /// <summary>
        /// 构造实例键：空白实例名规范化为 null（等于"没传名字"）；类型为 null 时抛 ArgumentNullException。
        /// </summary>
        public PanelKey(Type panelType, string instanceName)
        {
            if (panelType == null)
                throw new ArgumentNullException(nameof(panelType));

            PanelType = panelType;
            InstanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName;
        }

        /// <summary>按泛型面板类型构造实例键；instanceName 传 null（默认）表示默认实例。</summary>
        public static PanelKey Of<T>(string instanceName = null) where T : UIPanel
        {
            return new PanelKey(typeof(T), instanceName);
        }

        /// <summary>判断两个键是否相等：类型引用相等，实例名按序数比较。</summary>
        public bool Equals(PanelKey other)
        {
            return ReferenceEquals(PanelType, other.PanelType)
                   && string.Equals(InstanceName, other.InstanceName, StringComparison.Ordinal);
        }

        /// <summary>与任意对象比较：只认 PanelKey，其他类型一律不相等。</summary>
        public override bool Equals(object obj)
        {
            return obj is PanelKey other && Equals(other);
        }

        /// <summary>类型与实例名混合哈希，保证同一个键在字典里定位稳定。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int typeHash = PanelType != null ? PanelType.GetHashCode() : 0;
                int nameHash = InstanceName != null ? InstanceName.GetHashCode() : 0;
                return (typeHash * 397) ^ nameHash;
            }
        }

        /// <summary>调试输出：默认实例为 "TypeName"，命名实例为 "TypeName#实例名"。</summary>
        public override string ToString()
        {
            if (PanelType == null)
                return string.Empty;

            return InstanceName == null ? PanelType.Name : $"{PanelType.Name}#{InstanceName}";
        }

        /// <summary>比较两个键是否相等。</summary>
        public static bool operator ==(PanelKey left, PanelKey right)
        {
            return left.Equals(right);
        }

        /// <summary>比较两个键是否不相等。</summary>
        public static bool operator !=(PanelKey left, PanelKey right)
        {
            return !left.Equals(right);
        }
    }
}
