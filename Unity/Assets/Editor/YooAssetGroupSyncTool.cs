using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// YooAsset Group 自动同步工具（反射实现，避免直接依赖 YooAsset.Editor 程序集）。
/// 规则：
/// - 扫描 Assets/GameAssets 下一级目录
/// - 非 UI 目录：目录名 -> GroupName: ga_{lower}，标签: {lower}
/// - UI 目录：按二级子目录拆组 -> GroupName: ui_{lower}，标签: ui_{lower}
/// - Collector 按目录路径匹配，重复执行幂等更新
/// </summary>
public static class YooAssetGroupSyncTool
{
    private const string MenuPath = "Tools/YoAsset/Sync Groups";
    private const string RootPath = "Assets/GameAssets";
    private const string UiFolderName = "UI";
    private const string DefaultPackageName = "Main";

    private static readonly string[] PackageNameMembers = { "PackageName", "packageName", "Name", "name" };
    private static readonly string[] GroupNameMembers = { "GroupName", "groupName", "Name", "name" };
    private static readonly string[] CollectorPathMembers = { "CollectPath", "collectPath", "CollectorPath", "collectorPath" };
    private static readonly string[] CollectorTagsMembers = { "AssetTags", "assetTags", "Tags", "tags", "Label", "label" };
    private static readonly string[] PackageGroupsMembers = { "Groups", "groups" };
    private static readonly string[] GroupCollectorsMembers = { "Collectors", "collectors" };
    private static readonly string[] SettingPackagesMembers = { "Packages", "packages" };

    [MenuItem(MenuPath)]
    public static void SyncGroupsMenu()
    {
        try
        {
            var setting = LoadCollectorSettingAsset();
            if (setting == null)
            {
                Debug.LogError("YooAsset Sync: 未找到 AssetBundleCollectorSetting 资产，请先在 YooAsset 中初始化配置。");
                return;
            }

            if (!AssetDatabase.IsValidFolder(RootPath))
            {
                Debug.LogWarning($"YooAsset Sync: 目录不存在，已跳过：{RootPath}");
                return;
            }

            var package = EnsurePackage(setting, DefaultPackageName);
            if (package == null)
            {
                Debug.LogError("YooAsset Sync: 无法获取或创建 Package。");
                return;
            }

            var firstLevelDirs = Directory.GetDirectories(RootPath, "*", SearchOption.TopDirectoryOnly);
            int createdGroups = 0;
            int updatedGroups = 0;
            int createdCollectors = 0;
            int removedGroups = 0;
            int skippedDirs = 0;
            var managedGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var absDir in firstLevelDirs)
            {
                string dirName = Path.GetFileName(absDir);
                if (string.IsNullOrWhiteSpace(dirName) || dirName.StartsWith(".")) { skippedDirs++; continue; }
                if (string.Equals(dirName, "Editor", StringComparison.OrdinalIgnoreCase)) { skippedDirs++; continue; }

                string unityPath = ToUnityPath(absDir);
                if (!AssetDatabase.IsValidFolder(unityPath)) { skippedDirs++; continue; }

                // UI 目录按二级子目录拆组，避免 UI 全量打成一个大包。
                if (string.Equals(dirName, UiFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    var uiSubDirs = Directory.GetDirectories(absDir, "*", SearchOption.TopDirectoryOnly);
                    int uiSynced = 0;
                    foreach (var uiAbsSubDir in uiSubDirs)
                    {
                        string uiSubName = Path.GetFileName(uiAbsSubDir);
                        if (string.IsNullOrWhiteSpace(uiSubName) || uiSubName.StartsWith(".")) continue;

                        string uiUnityPath = ToUnityPath(uiAbsSubDir);
                        if (!AssetDatabase.IsValidFolder(uiUnityPath)) continue;

                        string subNormalized = NormalizeToken(uiSubName);
                        string uiGroupName = $"ui_{subNormalized}";
                        string uiLabel = uiGroupName;
                        managedGroupNames.Add(uiGroupName);

                        if (SyncOneGroup(package, uiGroupName, uiLabel, uiUnityPath, ref createdGroups, ref updatedGroups, ref createdCollectors))
                            uiSynced++;
                    }

                    // 若 UI 下没有可同步的二级目录，回退一级目录组。
                    if (uiSynced == 0)
                    {
                        string fallbackNormalized = NormalizeToken(dirName);
                        string fallbackGroupName = $"ga_{fallbackNormalized}";
                        string fallbackLabel = fallbackNormalized;
                        managedGroupNames.Add(fallbackGroupName);
                        SyncOneGroup(package, fallbackGroupName, fallbackLabel, unityPath, ref createdGroups, ref updatedGroups, ref createdCollectors);
                    }
                    continue;
                }

                string normalized = NormalizeToken(dirName);
                string groupName = $"ga_{normalized}";
                string label = normalized;
                managedGroupNames.Add(groupName);
                SyncOneGroup(package, groupName, label, unityPath, ref createdGroups, ref updatedGroups, ref createdCollectors);
            }

            removedGroups = PruneManagedGroups(package, managedGroupNames);

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"YooAsset Sync: 完成。createdGroups={createdGroups}, updatedGroups={updatedGroups}, " +
                $"createdCollectors={createdCollectors}, removedGroups={removedGroups}, skippedDirs={skippedDirs}, root={RootPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"YooAsset Sync: 执行失败：{ex}");
        }
    }

    private static bool SyncOneGroup(
        object package,
        string groupName,
        string label,
        string unityPath,
        ref int createdGroups,
        ref int updatedGroups,
        ref int createdCollectors)
    {
        var groupsList = GetOrCreateList(package, PackageGroupsMembers);
        if (groupsList == null)
        {
            Debug.LogError("YooAsset Sync: Package 中未找到 Groups 列表，可能与当前 YooAsset 版本结构不匹配。");
            return false;
        }

        bool groupCreated;
        object group = GetOrCreateByName(groupsList, groupName, GroupNameMembers, out groupCreated);
        if (group == null)
        {
            Debug.LogWarning($"YooAsset Sync: 无法创建或获取 Group：{groupName}");
            return false;
        }

        if (groupCreated) createdGroups++;
        else updatedGroups++;

        var collectorsList = GetOrCreateList(group, GroupCollectorsMembers);
        if (collectorsList == null)
        {
            Debug.LogWarning($"YooAsset Sync: Group 无 Collectors 列表，已跳过：{groupName}");
            return false;
        }

        bool collectorCreated;
        object collector = GetOrCreateByPath(collectorsList, unityPath, CollectorPathMembers, out collectorCreated);
        if (collector == null)
        {
            Debug.LogWarning($"YooAsset Sync: 无法创建或获取 Collector：{groupName} -> {unityPath}");
            return false;
        }

        if (collectorCreated) createdCollectors++;

        // 仅设置与规则强相关的字段，避免覆盖用户已有打包规则细节。
        SetAnyStringMember(collector, CollectorPathMembers, unityPath);
        SetAnyStringMember(collector, CollectorTagsMembers, label);
        return true;
    }

    private static int PruneManagedGroups(object package, HashSet<string> expectedGroupNames)
    {
        var groupsList = GetOrCreateList(package, PackageGroupsMembers);
        if (groupsList == null) return 0;

        int removed = 0;
        for (int i = groupsList.Count - 1; i >= 0; i--)
        {
            var group = groupsList[i];
            if (group == null) continue;

            string groupName = GetAnyStringMember(group, GroupNameMembers);
            if (string.IsNullOrWhiteSpace(groupName)) continue;
            if (!IsManagedGroupName(groupName)) continue;
            if (expectedGroupNames.Contains(groupName)) continue;

            groupsList.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    private static bool IsManagedGroupName(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return false;
        return groupName.StartsWith("ga_", StringComparison.OrdinalIgnoreCase)
            || groupName.StartsWith("ui_", StringComparison.OrdinalIgnoreCase);
    }

    private static ScriptableObject LoadCollectorSettingAsset()
    {
        // 优先按资产名定位
        var guids = AssetDatabase.FindAssets("AssetBundleCollectorSetting");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
            if (asset == null) continue;
            if (asset.name == "AssetBundleCollectorSetting") return asset;
            if (asset.GetType().Name.IndexOf("CollectorSetting", StringComparison.OrdinalIgnoreCase) >= 0) return asset;
        }
        return null;
    }

    private static object EnsurePackage(object setting, string packageName)
    {
        var packages = GetOrCreateList(setting, SettingPackagesMembers);
        if (packages == null) return null;

        bool created;
        var package = GetOrCreateByName(packages, packageName, PackageNameMembers, out created);
        if (package == null && packages.Count > 0) package = packages[0];
        return package;
    }

    private static IList GetOrCreateList(object owner, string[] memberCandidates)
    {
        foreach (var name in memberCandidates)
        {
            if (TryGetMemberValue(owner, name, out var value) && value is IList list)
            {
                return list;
            }
        }

        // 若存在字段但为 null，尝试实例化后写回
        foreach (var name in memberCandidates)
        {
            var memberType = GetMemberType(owner, name);
            if (memberType == null || !typeof(IList).IsAssignableFrom(memberType)) continue;
            object instance = Activator.CreateInstance(memberType);
            if (instance is IList newList && TrySetMemberValue(owner, name, instance))
            {
                return newList;
            }
        }

        return null;
    }

    private static object GetOrCreateByName(IList list, string wantedName, string[] nameMembers, out bool created)
    {
        created = false;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item == null) continue;
            string name = GetAnyStringMember(item, nameMembers);
            if (string.Equals(name, wantedName, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        var newItem = CreateListElement(list);
        if (newItem == null) return null;
        SetAnyStringMember(newItem, nameMembers, wantedName);
        list.Add(newItem);
        created = true;
        return newItem;
    }

    private static object GetOrCreateByPath(IList list, string wantedPath, string[] pathMembers, out bool created)
    {
        created = false;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item == null) continue;
            string path = GetAnyStringMember(item, pathMembers);
            if (string.Equals(path, wantedPath, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        var newItem = CreateListElement(list);
        if (newItem == null) return null;
        SetAnyStringMember(newItem, pathMembers, wantedPath);
        list.Add(newItem);
        created = true;
        return newItem;
    }

    private static object CreateListElement(IList list)
    {
        var listType = list.GetType();
        Type elementType = null;
        if (listType.IsArray) elementType = listType.GetElementType();
        if (elementType == null && listType.IsGenericType) elementType = listType.GetGenericArguments()[0];
        if (elementType == null) return null;
        try
        {
            return Activator.CreateInstance(elementType);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) chars[i] = '_';
        }
        return new string(chars).Trim('_');
    }

    private static string ToUnityPath(string absPath)
    {
        string fullProject = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string fullInput = Path.GetFullPath(absPath);
        if (fullInput.StartsWith(fullProject, StringComparison.OrdinalIgnoreCase))
        {
            string relative = fullInput.Substring(fullProject.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }
        return absPath.Replace('\\', '/');
    }

    private static string GetAnyStringMember(object obj, string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (TryGetMemberValue(obj, name, out var value) && value is string s) return s;
        }
        return null;
    }

    private static bool SetAnyStringMember(object obj, string[] candidateNames, string value)
    {
        foreach (var name in candidateNames)
        {
            if (TrySetMemberValue(obj, name, value)) return true;
        }
        return false;
    }

    private static bool TryGetMemberValue(object obj, string name, out object value)
    {
        value = null;
        if (obj == null) return false;
        var type = obj.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var field = type.GetField(name, flags);
        if (field != null)
        {
            value = field.GetValue(obj);
            return true;
        }

        var prop = type.GetProperty(name, flags);
        if (prop != null && prop.CanRead)
        {
            value = prop.GetValue(obj, null);
            return true;
        }

        return false;
    }

    private static bool TrySetMemberValue(object obj, string name, object value)
    {
        if (obj == null) return false;
        var type = obj.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var field = type.GetField(name, flags);
        if (field != null)
        {
            if (value == null || field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(obj, value);
                return true;
            }
            return false;
        }

        var prop = type.GetProperty(name, flags);
        if (prop != null && prop.CanWrite)
        {
            if (value == null || prop.PropertyType.IsInstanceOfType(value))
            {
                prop.SetValue(obj, value, null);
                return true;
            }
        }

        return false;
    }

    private static Type GetMemberType(object obj, string name)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = type.GetField(name, flags);
        if (field != null) return field.FieldType;
        var prop = type.GetProperty(name, flags);
        if (prop != null) return prop.PropertyType;
        return null;
    }
}
