using UnityEngine;

namespace Script.Core
{
    [CreateAssetMenu(fileName = "SceneFlowConfig", menuName = "AAATroop/GameFlow/SceneFlowConfig")]
    public class SceneFlowConfig : ScriptableObject
    {
        [Header("YooAsset Runtime")]
        public string yooAssetPackageName = "Main";

        [Header("Scene Keys (YooAsset keys)")]
        public string bootstrapSceneKey = "Bootstrap";
        public string lobbySceneKey = "Lobby";
        public string battleSceneKey = "Battle";

        [Header("Bootstrap Flow")]
        [Tooltip("运行后自动走：初始化 → Bootstrap（等待 Loading 完成）→ 大厅。")]
        public bool autoEnterLobby = true;

        [Tooltip("Bootstrap Loading 完成后、加载大厅场景前的额外延时（秒）。")]
        public float enterLobbyDelaySeconds = 0.2f;

        [Tooltip("Bootstrap 阶段等待 NotifyBootstrapLoadingComplete 的最长秒数；超时后记录错误并继续进大厅。≤0 表示不启用超时。")]
        public float bootstrapLoadingGateTimeoutSeconds = 120f;

        [Header("Battle Flow")]
        public bool preloadBattleGroup = false;
        public string battlePreloadGroupKey = "battle_core";
    }
}
