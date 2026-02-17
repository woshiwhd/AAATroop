using UnityEngine;
using Managers = Script.Managers;

namespace Script
{
    /// <summary>
    /// 向后兼容 wrapper：代表旧的 Script.ChunkManager。
    /// 实际实现已移动到 Script.Managers.ChunkManager。
    /// </summary>
    public class ChunkManager : Managers.ChunkManager { }
}
