using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BulkEditor
{
    internal static class MenuItems
    {
        [MenuItem("Tools/Bulk Editing/Components/Remove Missing Components From Prefabs")]
        public static void RemoveMissingComponentsFromPrefabs()
        {
            var scratchComponents = new List<Component>();
            var scratchTransforms = new List<Transform>();
            BulkEditing.RunFunctionOnAllPrefabs("Removing missing components...", (path, obj) =>
            {
                obj.GetComponentsInChildren(true, scratchComponents);
                if (scratchComponents.Any(c => c == null))
                {
                    // Load a mutable copy of the prefab for editing
                    using (var editingScope = new EditPrefabContentsScope(path))
                    {
                        // Get the component we're interested in modifying
                        editingScope.prefabContentsRoot.GetComponentsInChildren(true, scratchTransforms);
                        foreach (var transform in scratchTransforms)
                        {
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                        }

                        editingScope.QueueSave();
                        return true;
                    }
                }

                return false;
            });
        }

        [MenuItem("Tools/Bulk Editing/Components/Remove Missing Components From Scenes")]
        public static void RemoveMissingComponentsFromScenes()
        {
            var rootObjects = new List<GameObject>();
            var scratchComponents = new List<Component>();
            var scratchTransforms = new List<Transform>();
            BulkEditing.RunFunctionInAllScenes("Removing missing components...", () =>
            {
                var isDirty = false;
                BulkEditing.GetRootObjectsOfLoadedScenes(ref rootObjects);
                foreach (var root in rootObjects)
                {
                    root.GetComponentsInChildren(true, scratchComponents);
                    if (scratchComponents.Any(c => c == null))
                    {
                        root.GetComponentsInChildren(true, scratchTransforms);
                        foreach (var transform in scratchTransforms)
                        {
                            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                        }

                        isDirty = true;
                    }
                }

                return isDirty;
            }, onlyScenesInBuild: false);
        }
        
        [MenuItem("Tools/Bulk Editing/Prefabs/Reimport All Prefabs")]
        public static void ReimportAllPrefabs()
        {
            BulkEditing.RunFunctionOnAllPrefabs("Reimporting...", (path, prefab) =>
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                return false;
            });
        }

        [MenuItem("Tools/Bulk Editing/Prefabs/Remove Missing Prefab Instances From Prefabs")]
        public static void RemoveMissingPrefabInstancesFromPrefabs()
        {
            var scratchTransforms = new List<Transform>();
            BulkEditing.RunFunctionOnAllPrefabs("Removing missing prefab instances...", (path, obj) =>
            {
                var isDirty = false;
                obj.GetComponentsInChildren(true, scratchTransforms);
                foreach (var transform in scratchTransforms)
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    if (PrefabUtility.IsPrefabAssetMissing(transform))
                    {
                        Debug.Log($"Destroying prefab instance with missing asset: {transform.gameObject.GetPathName(includeScene: true)}");
                        UnityEngine.Object.DestroyImmediate(transform.gameObject);
                        isDirty = true;
                    }
                }

                return isDirty;
            });
        }

        [MenuItem("Tools/Bulk Editing/Prefabs/Remove Missing Prefab Instances From Scenes")]
        public static void RemoveMissingPrefabInstancesFromScenes()
        {
            var rootObjects = new List<GameObject>();
            var scratchTransforms = new List<Transform>();
            BulkEditing.RunFunctionInAllScenes("Removing missing prefab instances...", () =>
            {
                var isDirty = false;
                BulkEditing.GetRootObjectsOfLoadedScenes(ref rootObjects);
                foreach (var root in rootObjects)
                {
                    root.GetComponentsInChildren(true, scratchTransforms);
                    foreach (var transform in scratchTransforms)
                    {
                        if (transform == null)
                        {
                            continue;
                        }

                        if (PrefabUtility.IsPrefabAssetMissing(transform))
                        {
                            Debug.Log($"Destroying prefab instance with missing asset: {transform.gameObject.GetPathName(includeScene: true)}");
                            UnityEngine.Object.DestroyImmediate(transform.gameObject);
                            isDirty = true;
                        }
                    }
                }

                return isDirty;
            }, onlyScenesInBuild: false);
        }
        
        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All Prefabs", isValidateFunction: false, priority: 100)]
        public static void ReserializeAllPrefabs()
        {
            var assetPaths = AssetDatabase.FindAssets("t:prefab")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid));

            Debug.Log($"Reserializing {assetPaths.Count()} prefabs in project.");

            AssetDatabase.StartAssetEditing();
            AssetDatabase.ForceReserializeAssets(assetPaths);
            AssetDatabase.StopAssetEditing();
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All Prefabs", isValidateFunction: true, priority: 100)]
        private static bool ReserializeAllPrefabsValidate()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All ScriptableObjects", isValidateFunction: false, priority: 101)]
        public static void ReserializeAllScriptableObjects()
        {
            var assetPaths = AssetDatabase.FindAssets("t:scriptableobject")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid));

            Debug.Log($"Reserializing {assetPaths.Count()} ScriptableObjects in project.");

            AssetDatabase.StartAssetEditing();
            AssetDatabase.ForceReserializeAssets(assetPaths);
            AssetDatabase.StopAssetEditing();
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All ScriptableObjects", isValidateFunction: true, priority: 101)]
        private static bool ReserializeAllScriptableObjectsValidate()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All Materials", isValidateFunction: false, priority: 101)]
        public static void ReserializeAllMaterials()
        {
            var assetPaths = AssetDatabase.FindAssets("t:material")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid));

            Debug.Log($"Reserializing {assetPaths.Count()} Materials in project.");

            AssetDatabase.StartAssetEditing();
            AssetDatabase.ForceReserializeAssets(assetPaths);
            AssetDatabase.StopAssetEditing();
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize All Materials", isValidateFunction: true, priority: 101)]
        private static bool ReserializeAllMaterialsValidate()
        {
            return !Application.isPlaying;
        }


        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize Selected Assets", isValidateFunction: false, priority: 102)]
        public static void ReserializeSelectedAssets()
        {
            var assetPaths = Selection.assetGUIDs.Select(guid => AssetDatabase.GUIDToAssetPath(guid));

            Debug.Log($"Reserializing {assetPaths.Count()} selected assets.");

            AssetDatabase.StartAssetEditing();
            AssetDatabase.ForceReserializeAssets(assetPaths);
            AssetDatabase.StopAssetEditing();
        }

        [MenuItem("Tools/Bulk Editing/Serialization/Reserialize Selected Assets", isValidateFunction: true, priority: 102)]
        private static bool ReserializeSelectedAssetsValidate()
        {
            return !Application.isPlaying && Selection.assetGUIDs.Length > 0;
        }
    }
}