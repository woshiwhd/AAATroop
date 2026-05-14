using System;
using System.Collections.Generic;
using UnityEngine;

namespace Script.Core.Assets
{
    [CreateAssetMenu(fileName = "AssetGroupsConfig", menuName = "AAATroop/Assets/AssetGroupsConfig")]
    public class AssetGroupsConfig : ScriptableObject
    {
        [Serializable]
        public class GroupEntry
        {
            public string groupKey;
            public List<string> assetKeys = new List<string>();
        }

        public List<GroupEntry> groups = new List<GroupEntry>();

        public bool TryGetGroup(string groupKey, out GroupEntry group)
        {
            group = null;
            if (string.IsNullOrEmpty(groupKey) || groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && groups[i].groupKey == groupKey)
                {
                    group = groups[i];
                    return true;
                }
            }
            return false;
        }
    }
}
