using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Script.Core
{
    public enum GameFlowState
    {
        None = 0,
        Initializing = 1,
        Bootstrap = 2,
        Lobby = 3,
        BattleLoading = 4,
        Battle = 5
    }

    public interface IGameFlowState
    {
        UniTask EnterAsync(CancellationToken ct);
        UniTask ExitAsync(CancellationToken ct);
    }

    /// <summary>
    /// 流程状态机：负责状态切换顺序（Exit -> Enter）与变更通知。
    /// 具体初始化/释放逻辑由各状态实现。
    /// </summary>
    public class GameStateMachine
    {
        public GameFlowState CurrentState { get; private set; } = GameFlowState.None;
        public event Action<GameFlowState, GameFlowState> OnStateChanged;
        private readonly Dictionary<GameFlowState, IGameFlowState> _states = new Dictionary<GameFlowState, IGameFlowState>();

        public void RegisterState(GameFlowState stateKey, IGameFlowState state)
        {
            if (state == null) return;
            _states[stateKey] = state;
        }

        public async UniTask<bool> ChangeStateAsync(GameFlowState next, CancellationToken ct = default)
        {
            if (next == CurrentState) return true;
            if (!_states.TryGetValue(next, out var nextState) || nextState == null) return false;

            var prev = CurrentState;
            // 先 Exit 再切 CurrentState 再 Enter：保证旧状态释放监听/订阅后再加载新场景，避免双场景并存时的资源竞争。
            if (_states.TryGetValue(prev, out var prevState) && prevState != null)
                await prevState.ExitAsync(ct);

            CurrentState = next;
            await nextState.EnterAsync(ct);
            OnStateChanged?.Invoke(prev, next);
            return true;
        }
    }
}
