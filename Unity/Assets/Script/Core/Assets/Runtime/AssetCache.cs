using System;
using System.Collections.Generic;
using UnityEngine;

namespace Script.Core.Assets
{
    public class AssetCache
    {
        private class Entry
        {
            public UnityEngine.Object asset;
            public int refCount;
            public AssetBackendType backendType;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public bool TryRetain(string key, out UnityEngine.Object asset)
        {
            asset = null;
            if (!_entries.TryGetValue(key, out var entry) || entry == null || entry.asset == null) return false;
            entry.refCount++;
            asset = entry.asset;
            return true;
        }

        public void AddOrRetain(string key, UnityEngine.Object asset, AssetBackendType backendType)
        {
            if (asset == null || string.IsNullOrEmpty(key)) return;
            if (_entries.TryGetValue(key, out var entry) && entry != null)
            {
                entry.refCount++;
                return;
            }

            _entries[key] = new Entry
            {
                asset = asset,
                refCount = 1,
                backendType = backendType
            };
        }

        public bool Release(string key, out UnityEngine.Object asset, out AssetBackendType backendType)
        {
            asset = null;
            backendType = AssetBackendType.Resources;
            if (!_entries.TryGetValue(key, out var entry) || entry == null) return false;

            entry.refCount--;
            if (entry.refCount > 0) return false;

            asset = entry.asset;
            backendType = entry.backendType;
            _entries.Remove(key);
            return true;
        }

        public int Count => _entries.Count;
    }
}
