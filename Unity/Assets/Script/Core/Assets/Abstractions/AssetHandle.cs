using System;

namespace Script.Core.Assets
{
    public class AssetHandle<T> where T : UnityEngine.Object
    {
        public string key;
        public AssetLoadStatus status;
        public float progress;
        public T asset;
        public string error;

        private Action _releaseAction;

        public bool IsDone => status == AssetLoadStatus.Succeeded || status == AssetLoadStatus.Failed;
        public bool IsSuccess => status == AssetLoadStatus.Succeeded && asset != null;

        public void SetReleaseAction(Action releaseAction) => _releaseAction = releaseAction;

        public void Release()
        {
            _releaseAction?.Invoke();
            _releaseAction = null;
        }
    }
}
