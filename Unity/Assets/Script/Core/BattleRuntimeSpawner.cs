using Cysharp.Threading.Tasks;
using Script.Core.Assets;
using Script.Utilities;
using UnityEngine;

namespace Script.Core
{
    /// <summary>
    /// Battle 场景运行时实体生成器：按 key 从 AssetService 加载并生成 Player/Enemy。
    /// </summary>
    public class BattleRuntimeSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject entitiesRoot;
        [SerializeField] private GameObject playerObject;
        [SerializeField] private string playerPrefabKey = "";
        [SerializeField] private Transform playerSpawnPoint;

        [System.Serializable]
        private class EnemySpawnEntry
        {
            public string prefabKey;
            public Transform spawnPoint;
        }

        [SerializeField] private EnemySpawnEntry[] enemySpawns;

        private void Start()
        {
            SpawnRuntimeEntitiesAsync().Forget();
        }

        private async UniTask SpawnRuntimeEntitiesAsync()
        {
            var service = AssetServiceLocator.Current;
            if (service == null)
            {
                GameLog.LogError("BattleRuntimeSpawner: AssetService 未初始化。");
                return;
            }

            Transform parent = entitiesRoot != null ? entitiesRoot.transform : null;

            if (playerObject == null && !string.IsNullOrWhiteSpace(playerPrefabKey))
            {
                var prefab = await service.LoadAssetAsync<GameObject>(playerPrefabKey);
                if (prefab == null)
                {
                    GameLog.LogError($"BattleRuntimeSpawner: 玩家预制体加载失败，key={playerPrefabKey}");
                }
                else
                {
                    var pos = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
                    var rot = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;
                    playerObject = Instantiate(prefab, pos, rot, parent);
                    playerObject.name = "Player";
                }
            }

            if (enemySpawns == null || enemySpawns.Length == 0) return;
            for (int i = 0; i < enemySpawns.Length; i++)
            {
                var entry = enemySpawns[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.prefabKey)) continue;

                var enemyPrefab = await service.LoadAssetAsync<GameObject>(entry.prefabKey);
                if (enemyPrefab == null)
                {
                    GameLog.LogError($"BattleRuntimeSpawner: 敌人预制体加载失败，key={entry.prefabKey}");
                    continue;
                }

                var pos = entry.spawnPoint != null ? entry.spawnPoint.position : Vector3.zero;
                var rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : Quaternion.identity;
                var enemy = Instantiate(enemyPrefab, pos, rot, parent);
                if (string.IsNullOrEmpty(enemy.name) || enemy.name.Contains("(Clone)"))
                    enemy.name = $"Enemy_{i + 1}";
            }
        }
    }
}
