using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Netcode calculates prefab hashes in NetworkObject.OnValidate. Some imported
// prefab variants in this project were saved before that validation ran and
// still contain zero, which makes every registration collide at startup.
[InitializeOnLoad]
public static class NetworkPrefabHashRepair
{
    const string PrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    static NetworkPrefabHashRepair()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += RepairInvalidHashes;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += RepairInvalidHashes;
    }

    [MenuItem("Tools/Wizard Duel/Repair network prefab hashes")]
    public static void RepairInvalidHashes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabsPath);
        if (list == null) throw new InvalidOperationException("Missing network prefabs list: " + PrefabsPath);

        var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (validate == null) throw new MissingMethodException("NetworkObject.OnValidate");

        int repaired = 0;
        var hashes = new HashSet<uint>();
        foreach (var entry in list.PrefabList)
        {
            if (entry.Prefab == null || !entry.Prefab.TryGetComponent(out NetworkObject networkObject))
                throw new InvalidOperationException("Network prefab entry has no NetworkObject.");

            var serialized = new SerializedObject(networkObject);
            var hash = serialized.FindProperty("GlobalObjectIdHash");
            if (hash == null) throw new InvalidOperationException("NetworkObject hash field is unavailable.");
            if (hash.uintValue == 0)
            {
                // Use Netcode's own hash calculation, including prefab-variant IDs.
                validate.Invoke(networkObject, null);
                serialized.Update();
                repaired++;
            }
            if (hash.uintValue == 0 || !hashes.Add(hash.uintValue))
                throw new InvalidOperationException("Invalid or duplicate network prefab hash: " + entry.Prefab.name);
        }

        if (repaired > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"NETWORK_PREFAB_HASH_REPAIR_PASS repaired={repaired} registered={hashes.Count}");
        }
    }
}
