using UnityEngine;

namespace Script.AI
{
    /// <summary>
    /// 可作为目标的单位/物体基础接口。
    /// </summary>
    public interface ITargetable
    {
        /// <summary>世界空间中的位置（通常返回 Transform）。</summary>
        Transform Transform { get; }

        /// <summary>所属阵营。</summary>
        Faction Faction { get; }

        /// <summary>当前是否存活，可被锁定/攻击。</summary>
        bool IsAlive { get; }
    }
}

