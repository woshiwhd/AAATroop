using UnityEngine;

namespace Script.Core
{
    [CreateAssetMenu(fileName = "SceneFlowConfig", menuName = "AAATroop/Flow/SceneFlowConfig")]
    public class SceneFlowConfig : ScriptableObject
    {
        [Header("YooAsset Runtime")]
        public string yooAssetPackageName = "Main";

        [Header("Scene Keys (YooAsset keys)")]
        public string bootstrapSceneKey = "Bootstrap";
        public string lobbySceneKey = "Lobby";
        public string battleSceneKey = "Battle";

        [Header("Bootstrap Flow")]
        public bool autoEnterLobby = true;
        public float enterLobbyDelaySeconds = 0.2f;

        [Header("Battle Flow")]
        public bool preloadBattleGroup = false;
        public string battlePreloadGroupKey = "battle_core";
    }
}
