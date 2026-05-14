using System.Threading;
using Cysharp.Threading.Tasks;
using Script.Core.Assets;
using Script.Utilities;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Script.Core
{
    /// <summary>
    /// 场景流上下文：封装状态执行需要的配置与基础能力，减少状态类对 MonoBehaviour 的耦合。
    /// </summary>
    public class SceneFlowContext
    {
        private readonly SceneFlowConfig _config;
        private bool _runtimeReady;
        private bool _yooReady;

        public SceneFlowContext(SceneFlowConfig config)
        {
            _config = config;
        }

        public string LobbySceneKey => _config != null ? _config.lobbySceneKey : "Lobby";
        public string BattleSceneKey => _config != null ? _config.battleSceneKey : "Battle";
        public string BootstrapSceneKey => _config != null ? _config.bootstrapSceneKey : "Bootstrap";
        public string YooAssetPackageName => _config != null && !string.IsNullOrWhiteSpace(_config.yooAssetPackageName) ? _config.yooAssetPackageName : "Main";
        public bool PreloadBattleGroup => _config != null && _config.preloadBattleGroup;
        public string BattlePreloadGroupKey => _config != null ? _config.battlePreloadGroupKey : "battle_core";
        public float EnterLobbyDelaySeconds => _config != null ? _config.enterLobbyDelaySeconds : 0f;

        public async UniTask<bool> EnsureRuntimeReadyAsync(CancellationToken ct)
        {
            // 一次就绪后复用，避免每次状态切换重复做重初始化。
            if (_runtimeReady) return true;

            var service = AssetServiceLocator.Current;
            if (service == null)
            {
                GameLog.LogError("SceneFlowContext: AssetService 未初始化。");
                return false;
            }

            // 确保统一资源服务完成后端初始化，避免状态流在未就绪时触发加载。
            await service.InitializeAsync(ct);
            _runtimeReady = await TryEnsureYooAssetsInitializedAsync(ct);
            return _runtimeReady;
        }

        public async UniTask<bool> LoadSceneByKeyAsync(string sceneKey, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sceneKey))
            {
                GameLog.LogError("SceneFlowContext: scene key 为空。");
                return false;
            }

            var service = AssetServiceLocator.Current;
            if (service == null)
            {
                GameLog.LogError("SceneFlowContext: AssetService 未初始化。");
                return false;
            }

            var scene = await service.LoadSceneAsync(sceneKey, LoadSceneMode.Single, ct);
            if (!scene.IsValid())
            {
                GameLog.LogError($"SceneFlowContext: 场景加载失败，key={sceneKey}");
                return false;
            }
            return true;
        }

        public async UniTask PreloadGroupAsync(string groupKey, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(groupKey)) return;
            var service = AssetServiceLocator.Current;
            if (service == null)
            {
                GameLog.LogWarning("SceneFlowContext: AssetService 未初始化，跳过预加载。");
                return;
            }
            await service.PreloadGroupAsync(groupKey, ct);
        }

        // 供 GameFlow 各状态调用：统一走 LoadSceneByKeyAsync + 配置中的 key，避免状态里散落魔法字符串。

        public UniTask<bool> LoadBootstrapSceneAsync(CancellationToken ct) =>
            LoadSceneByKeyAsync(BootstrapSceneKey, ct);

        public UniTask<bool> LoadLobbySceneAsync(CancellationToken ct) =>
            LoadSceneByKeyAsync(LobbySceneKey, ct);

        public UniTask<bool> LoadBattleSceneAsync(CancellationToken ct) =>
            LoadSceneByKeyAsync(BattleSceneKey, ct);

        public async UniTask PreloadBattleAsync(CancellationToken ct)
        {
            // 与 SceneFlowConfig.preloadBattleGroup 对齐：关闭时立即返回，BattleLoadingState 仍可有清晰语义。
            if (!PreloadBattleGroup) return;
            await PreloadGroupAsync(BattlePreloadGroupKey, ct);
        }


        private async UniTask<bool> TryEnsureYooAssetsInitializedAsync(CancellationToken ct)
        {
            if (_yooReady) return true;

            try
            {
                if (!YooAssets.Initialized)
                    YooAssets.Initialize();

                string packageName = YooAssetPackageName;
                ResourcePackage package = YooAssets.TryGetPackage(packageName);
                if (package == null)
                    package = YooAssets.CreatePackage(packageName);

                if (package == null)
                {
                    GameLog.LogError($"SceneFlowContext: 无法获取 YooAsset 包裹：{packageName}");
                    return false;
                }

                if (!await EnsurePackageInitializedAsync(package, packageName, ct))
                    return false;

                _yooReady = true;
                return true;
            }
            catch (System.Exception ex)
            {
                GameLog.LogError($"SceneFlowContext: YooAssets 初始化失败: {ex.Message}\n{ex}");
                return false;
            }
        }

        private async UniTask<bool> EnsurePackageInitializedAsync(ResourcePackage package, string packageName, CancellationToken ct)
        {
            if (package.InitializeStatus == EOperationStatus.Succeed)
                return true;

            // 初始化参数按平台分支：Editor 走模拟模式，运行时走离线模式。
            var initParams = CreateInitializeParameters(packageName);
            if (initParams == null) return false;

            var op = package.InitializeAsync(initParams);
            if (op == null) return false;

            while (!op.IsDone)
            {
                // 这里异步轮询初始化操作，避免阻塞主线程导致状态机卡死。
                await UniTask.Yield(cancellationToken: ct);
            }

            if (op.Status == EOperationStatus.Succeed)
            {
                // 官方流程：初始化后请求版本并更新清单，激活 ActiveManifest。
                var versionOp = package.RequestPackageVersionAsync();
                while (!versionOp.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(cancellationToken: ct);
                }
                if (versionOp.Status != EOperationStatus.Succeed || string.IsNullOrWhiteSpace(versionOp.PackageVersion))
                {
                    string versionErr = string.IsNullOrEmpty(versionOp.Error) ? "unknown" : versionOp.Error;
                    GameLog.LogError($"SceneFlowContext: 请求包版本失败 package={packageName}, error={versionErr}");
                    return false;
                }

                var manifestOp = package.UpdatePackageManifestAsync(versionOp.PackageVersion);
                while (!manifestOp.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Yield(cancellationToken: ct);
                }
                if (manifestOp.Status != EOperationStatus.Succeed)
                {
                    string manifestErr = string.IsNullOrEmpty(manifestOp.Error) ? "unknown" : manifestOp.Error;
                    GameLog.LogError($"SceneFlowContext: 更新包清单失败 package={packageName}, version={versionOp.PackageVersion}, error={manifestErr}");
                    return false;
                }

                return true;
            }

            string err = string.IsNullOrEmpty(op.Error) ? "unknown" : op.Error;
            GameLog.LogError($"SceneFlowContext: 包初始化失败 package={packageName}, error={err}");
            return false;
        }

        private InitializeParameters CreateInitializeParameters(string packageName)
        {
#if UNITY_EDITOR
            var initParams = new EditorSimulateModeParameters();
            var simulateResult = EditorSimulateModeHelper.SimulateBuild(packageName);
            if (simulateResult == null || string.IsNullOrWhiteSpace(simulateResult.PackageRootDirectory))
            {
                GameLog.LogError($"SceneFlowContext: SimulateBuild 失败，未生成包目录。package={packageName}。请先在 YooAsset Collector 窗口对该包执行一次 Save/Export。");
                return null;
            }
            string packageRoot = simulateResult.PackageRootDirectory;
            initParams.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
            return initParams;
#else
            var initParams = new OfflinePlayModeParameters();
            initParams.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            return initParams;
#endif
        }
    }
}
