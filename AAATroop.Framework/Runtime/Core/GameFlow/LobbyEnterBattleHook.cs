using Script.Utilities;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// 挂在大厅场景任意物体上：把 UI Button 的 OnClick 指到 <see cref="OnEnterBattleClicked"/>，仅在点击后进入战斗场景。
    /// </summary>
    public sealed class LobbyEnterBattleHook : MonoBehaviour
    {
        public void OnEnterBattleClicked()
        {
            if (SceneFlowManager.Instance == null)
            {
                GameLog.LogWarning("LobbyEnterBattleHook: SceneFlowManager 不存在，无法进入战斗。");
                return;
            }

            SceneFlowManager.Instance.EnterBattle();
        }
    }
}
