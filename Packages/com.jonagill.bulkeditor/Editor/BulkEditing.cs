using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Assertions;
using UnityInternalAccess.Editor;

#if UNITY_ADDRESSABLES
using UnityEditor.AddressableAssets;
#endif

using Object = UnityEngine.Object;

namespace BulkEditor
{
    /// <summary>
    /// Helper APIs for running functions across large numbers of scenes, prefabs, and other assets across the project
    /// </summary>
    public static class BulkEditing
    {
        #region Bulk Processing

        /// <summary>
        /// Runs the given function across all instances of the given component in the project. Processes all prefabs
        /// containing the component, then processes all scenes.
        /// </summary>
        public static void RunFunctionAcrossAllInstancesOfComponent<T>(string label, Func<T, bool> function, bool onlyScenesInBuild = true)
        {
            var rootObjects = new List<GameObject>();
            
            bool ProcessPrefab(string path, GameObject prefab)
            {
                var isDirty = false;
                if (prefab.GetComponentInChildren<T>(true) != null)
                {
                    using (var editingScope = new EditPrefabContentsScope(path))
                    {
                        var componentsToModify = editingScope.prefabContentsRoot.GetComponentsInChildren<T>(true);
                        foreach (var componentToModify in componentsToModify)
                        {
                            isDirty |= function(componentToModify);
                        }

                        if (isDirty)
                        {
                            editingScope.QueueSave();
                        }
                    }
                }

                return isDirty;
            }

            bool ProcessScene()
            {
                var isDirty = false;

                GetRootObjectsOfLoadedScenes(ref rootObjects);
                foreach (var root in rootObjects)
                {
                    var components = root.GetComponentsInChildren<T>(true);
                    foreach (var component in components)
                    {
                        var baseComponent = component as Component;
                        var gameObject = baseComponent.gameObject;

                        if (function(component))
                        {
                            if (baseComponent != null)
                            {
                                EditorUtility.SetDirty(baseComponent);
                            }

                            EditorUtility.SetDirty(gameObject);

                            isDirty = true;
                        }
                    }
                }

                return isDirty;
            }

            RunFunctionOnAllPrefabs($"{label} (Prefabs)", ProcessPrefab);
            RunFunctionInAllScenes($"{label} (Scenes)", ProcessScene, onlyScenesInBuild: onlyScenesInBuild);
        }

        /// <summary>
        /// Run the given function across all specified Asset paths.
        /// Note that this will not automatically dirty the asset modified — the function provided should be sure to
        /// mark the asset as dirty to ensure that it gets saved properly.
        /// </summary>
        /// <param name="function">Should return true if it has dirtied the asset.</param>
        public static void RunFunctionOnAssetPaths(string label, IReadOnlyList<string> assetPaths, Func<string, bool> function)
        {
            bool isDirty = false;

            for (int i = 0; i < assetPaths.Count; ++i)
            {
                var path = assetPaths[i];
                if (path == null)
                    continue;

                if (i % 10 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"{label} -- {path}",
                            System.IO.Path.GetFileNameWithoutExtension(path),
                            i / (float)assetPaths.Count))
                    {
                        break;
                    }
                }

                try
                {
                    isDirty = function(path) | isDirty;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (isDirty)
            {
                // Save any prefabs that got changed
                AssetDatabase.SaveAssets();
            }

            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Run the given function across all scenes
        /// </summary>
        /// <param name="function">Should return true if it has dirtied the scene</param>
        /// <param name="sceneFilter">Optional. Given the scene path, return true to load the scene and false to skip it.</param>
        public static void RunFunctionInAllScenes(
            string label, 
            Func<bool> function, 
            Func<string, bool> sceneFilter = null, 
            bool onlyScenesInBuild = true)
        {
            var currentScenePath = SceneManager.GetActiveScene().path;
            var scenePaths = new List<string>();
            var editorSceneGuids = new List<string>();

            if (onlyScenesInBuild)
            {
                var sceneCount = EditorBuildSettings.scenes.Length;
                for (int i = 0; i < sceneCount; i++)
                {
                    var editorScene = EditorBuildSettings.scenes[i];
                    editorSceneGuids.Add(AssetDatabase.AssetPathToGUID(editorScene.path));
                }
            }
            
            bool ShouldIncludeScene(string sceneGuid, bool onlyScenesInBuild)
            {
                // Include all scenes if we're not limiting to the build
                if (!onlyScenesInBuild) return true;
                
                // This scene is included in our editor build settings
                if (editorSceneGuids.Contains(sceneGuid)) return true;
                
#if UNITY_ADDRESSABLES
                var addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
                if (addressableSettings != null)
                {
                    if (addressableSettings.FindAssetEntry(sceneGuid) != null)
                    {
                        // This is an Addressable scene and thus is considered included in the build as well
                        return true;
                    }
                }
#endif

                return false;
            }

            var sceneGuids = AssetDatabase.FindAssets("t:scene");
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                if (ShouldIncludeScene(sceneGuids[i], onlyScenesInBuild))
                {
                    scenePaths.Add(AssetDatabase.GUIDToAssetPath(sceneGuids[i]));
                }
            }

            for (var i = 0; i < scenePaths.Count; i++)
            {
                var path = scenePaths[i];
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                if (string.IsNullOrEmpty(path))
                {
                    // Deleted scenes might leave empty entries in the build settings
                    continue;
                }

                if (!PathBelongsToProject(path))
                {
                    // This isn't a scene we should be generally modifying
                    continue;
                }

                if (EditorUtility.DisplayCancelableProgressBar(
                        $"{label} -- {sceneName}",
                        path,
                        i / (float)scenePaths.Count))
                {
                    break;
                }

                // Filters can take some time to run, so run them after displaying the progress bar
                if (sceneFilter != null && !sceneFilter(path))
                {
                    // This scene is intentionally filtered
                    continue;
                }

                try
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var isDirty = function();

                    if (isDirty)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // Save any prefabs that got changed
            AssetDatabase.SaveAssets();

            EditorUtility.ClearProgressBar();

            // Load our previous scene
            if (!string.IsNullOrEmpty(currentScenePath))
            {
                EditorSceneManager.OpenScene(currentScenePath);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            }
        }

        /// <summary>
        /// Runs the given function on all prefabs. Does NOT automatically load
        /// the prefab onto the editing stage, so you need to do that manually
        /// if you want to edit the structure of the prefab (add / remove
        /// GameObjects or components, etc.)
        ///
        /// This is guaranteed to run on source prefabs before their variants.
        /// </summary>
        public static void RunFunctionOnAllPrefabs(string label, Func<string, GameObject, bool> function)
        {
            var isDirty = false;

            var prefabsByDepth = new Dictionary<int, List<(string, GameObject)>>();

            // Gather prefabs and organize them into buckets based on how many variants we have
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            var prefabsToProcess = 0;
            for (var i = 0; i < prefabGuids.Length; i++)
            {
                var guid = prefabGuids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (!PathBelongsToProject(path))
                {
                    // This isn't an asset we should be modifying
                    continue;
                }

                // Don't update the progress bar for every element because it is very slow to update
                if (i % 10 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"Gathering prefabs: {label}",
                            path,
                            i / (float) prefabGuids.Length))
                    {
                        break;
                    }
                }

                var gameObject = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
                var variantDepth = GetPrefabVariantDepth(gameObject);

                // Get the list of prefabs with this variant depth
                if (!prefabsByDepth.TryGetValue(variantDepth, out var prefabList))
                {
                    prefabList = new List<(string, GameObject)>();
                    prefabsByDepth[variantDepth] = prefabList;
                }

                prefabList.Add((path, gameObject));
                prefabsToProcess++;
            }

            // Order our variant depths least to greatest so we process parent prefabs before their variants
            var orderedKeys = prefabsByDepth.Keys.OrderBy(k => k);
            var prefabsProcessed = 0;
            foreach (var key in orderedKeys)
            {
                using (new AssetEditingScope())
                {
                    var pathsAndPrefabs = prefabsByDepth[key];
                    for (var i = 0; i < pathsAndPrefabs.Count; i++)
                    {
                        var path = pathsAndPrefabs[i].Item1;
                        var prefab = pathsAndPrefabs[i].Item2;

                        if (prefabsProcessed % 10 == 0)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    $"Running function across all prefabs: {label}",
                                    path,
                                    prefabsProcessed / (float) prefabsToProcess))
                            {
                                break;
                            }
                        }

                        try
                        {
                            isDirty |= function(path, prefab);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }

                        prefabsProcessed++;
                    }
                }

            }

            if (isDirty)
            {
                EditorUtility.DisplayProgressBar("Saving prefab changes...", "Saving prefabs modified by bulk operations", 1f);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.ClearProgressBar();
        }

        #endregion

        #region Serialized Enums
        
        /// <summary>
        /// Crawls through all our serialized assets and counts how many assets reference each value of the provided enum.
        /// </summary>
        public static Dictionary<T, int> GetSerializedEnumValueReferenceCounts<T>(bool logCounts) where T : Enum
        {
            var enumValueCounts = new Dictionary<T, int>();
            Type enumType = typeof(T);

            void IncrementEnumValueCount(T enumValue)
            {
                if (enumValueCounts.TryGetValue(enumValue, out var count))
                {
                    enumValueCounts[enumValue] = count + 1;
                }
                else
                {
                    enumValueCounts[enumValue] = 1;
                }
            }

            RunFunctionOnSerializedEnumValueReferences<T>((value, obj) =>
            {
                IncrementEnumValueCount(value);
            });

            if (logCounts)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine($"Counting serialized {enumType.Name} enum value usages...");

                var enumValues = Enum.GetValues(enumType);
                foreach (var value in enumValues)
                {
                    var enumValue = (T)value;
                    if (enumValueCounts.TryGetValue(enumValue, out var count))
                    {
                        stringBuilder.AppendLine($"{enumValue}: {count}");
                    }
                    else
                    {
                        stringBuilder.AppendLine($"{enumValue}: No references found");
                    }
                }

                Debug.Log(stringBuilder);
            }

            return enumValueCounts;
        }

        /// <summary>
        /// Crawls through all prefabs and ScriptableObjects in the project and logs the assets that use the given enum value.
        /// </summary>
        public static void LogSerializedEnumValueUses<T>(T value) where T : Enum
        {
            StringBuilder stringBuilder = new StringBuilder();
            RunFunctionOnSerializedEnumValueReferences<T>((serializedValue, obj) =>
            {
                if (serializedValue.Equals(value))
                {
                    if (obj == null)
                    {
                        stringBuilder.AppendLine($"Reference on null object?");
                    }
                    else if (obj is Component component)
                    {
                        stringBuilder.AppendLine($"GameObject: {component.gameObject.GetPathName(includeScene: true)}");
                    }
                    else
                    {
                        stringBuilder.AppendLine($"Asset: {obj.name}");
                    }
                }
            });

            Debug.Log(stringBuilder.ToString());
        }

        private static void RunFunctionOnSerializedEnumValueReferences<T>(Action<T, Object> processReferenceCallback) where T : Enum
        {
            var behaviorList = new List<MonoBehaviour> ();
            Type enumType = typeof(T);

            void SearchObjectForEnumValues(Object obj)
            {
                var serializedObject = new SerializedObject(obj);
                var iterator = serializedObject.GetIterator();
                while (iterator.Next(true))
                {
                    if (!iterator.isArray && iterator.GetEnumType() == enumType && iterator.TryGetValue(out T enumValue))
                    {
                        processReferenceCallback?.Invoke(enumValue, obj);
                    }
                }
            }

            void SearchGameObjectForEnumValuesRecursive(GameObject go)
            {
                behaviorList.Clear();
                go.GetComponents(behaviorList);

                foreach (var component in behaviorList)
                {
                    SearchObjectForEnumValues(component);
                }

                foreach (Transform child in go.transform)
                {
                    SearchGameObjectForEnumValuesRecursive(child.gameObject);
                }
            }

            var prefabGuids = AssetDatabase.FindAssets("t:prefab");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(prefabGuids[i]));
                if (EditorUtility.DisplayCancelableProgressBar("Searching prefabs...", prefab.name, i / (float) prefabGuids.Length))
                {
                    break;
                }

                SearchGameObjectForEnumValuesRecursive(prefab);
            }

            EditorUtility.ClearProgressBar();

            var scriptableObjectGuids = AssetDatabase.FindAssets("t:scriptableobject");
            for (int i = 0; i < scriptableObjectGuids.Length; i++ )
            {
                var scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(scriptableObjectGuids[i]));
                if (EditorUtility.DisplayCancelableProgressBar("Searching scriptable objects...", scriptableObject.name, i / (float) prefabGuids.Length))
                {
                    break;
                }

                SearchObjectForEnumValues(scriptableObject);
            }

            EditorUtility.ClearProgressBar();

            var rootObjects = new List<GameObject>();
            RunFunctionInAllScenes("Searching scenes...", () =>
            {
                GetRootObjectsOfLoadedScenes(ref rootObjects);
                foreach (var root in rootObjects)
                {
                    SearchGameObjectForEnumValuesRecursive(root);
                }

                return false;
            });
        }


        #endregion

        #region Helpers

        /// <summary>
        /// Returns the root GameObjects of all the currently loaded scenes
        /// </summary>
        public static void GetRootObjectsOfLoadedScenes(ref List<GameObject> roots)
        {
            if (roots == null)
            {
                roots = new List<GameObject>();
            }
            else
            {
                roots.Clear();
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                roots.AddRange(scene.GetRootGameObjects());
            }
        }

        /// <summary>
        /// Returns how many prefabs this variant asset is a child of
        /// </summary>
        public static int GetPrefabVariantDepth(GameObject gameObject)
        {
            var depth = 0;

            if (!EditorUtility.IsPersistent(gameObject))
            {
                // If this is an instance of a prefab, get the source asset it corresponds to
                gameObject = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            }

            bool ObjectIsVariant(GameObject go)
            {
                return go != null && PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.Variant;
            }

            while (ObjectIsVariant(gameObject))
            {
                gameObject = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                depth++;
            }

            return depth;
        }

        /// <summary>
        /// Returns whether the target object is or is a child of the provided root
        /// </summary>
        public static bool IsGameObjectPartOfHierarchy(GameObject targetObject, GameObject root)
        {
            return targetObject == root || targetObject.transform.IsChildOf(root.transform);
        }

        /// <summary>
        /// Given a target object and the root of its hierarchy, tries to find and return the corresponding object
        /// with the same path inside the target hierarchy.
        /// </summary>
        public static bool TryGetMatchingGameObject(
            GameObject targetObject,
            GameObject fromHierarchyRoot,
            GameObject toHierarchyRoot,
            out GameObject matchingObject)
        {
            return TryGetMatchingGameObject(
                targetObject,
                fromHierarchyRoot,
                toHierarchyRoot,
                out matchingObject,
                out _);
        }

        /// <summary>
        /// Given a target object and the root of its hierarchy, tries to find and return the corresponding object
        /// with the same path inside the target hierarchy.
        /// </summary>
        public static bool TryGetMatchingGameObject(
            GameObject targetObject,
            GameObject fromHierarchyRoot,
            GameObject toHierarchyRoot,
            out GameObject matchingObject,
            out string path)
        {
            if (!IsGameObjectPartOfHierarchy(targetObject, fromHierarchyRoot))
            {
                Debug.LogError($"GameObject {targetObject} is not part of hierarchy {fromHierarchyRoot} and cannot be matched with.");
                matchingObject = null;
                path = default;
                return false;
            }

            if (targetObject == fromHierarchyRoot)
            {
                matchingObject = toHierarchyRoot;
                path = string.Empty;
                return true;
            }

            // Get the relative path from the root to the target
            path = targetObject.GetPathName(
                includeScene: false,
                includeRoot: false,
                customRoot: fromHierarchyRoot.transform);


            // Validate that GetPathName() actually gave us a compatible path
            Assert.IsNotNull(fromHierarchyRoot.transform.Find(path));

            var matchingTransform = toHierarchyRoot.transform.Find(path);

            if (matchingTransform != null)
            {
                matchingObject = matchingTransform.gameObject;
                return true;
            }

            matchingObject = null;
            return false;
        }

        /// <summary>
        /// Given a target component and the root of its hierarchy, tries to find and return the corresponding component
        /// with the same path inside the target hierarchy.
        /// </summary>
        public static bool TryGetMatchingComponent(
            Component targetComponent,
            Type componentType,
            GameObject fromHierarchyRoot,
            GameObject toHierarchyRoot,
            out Component matchingComponent)
        {
            if (TryGetMatchingGameObject(
                    targetComponent.gameObject,
                    fromHierarchyRoot, toHierarchyRoot,
                    out var matchingGameObject))
            {
                var validComponents = matchingGameObject.GetComponents(componentType);
                if (validComponents.Length == 1)
                {
                    matchingComponent = validComponents[0];
                    return true;
                }

                if (validComponents.Length == 0)
                {
                    Debug.LogWarning($"Found no {componentType.Namespace} components that match path {targetComponent.gameObject.GetPathName()} on prefab {toHierarchyRoot}");
                }

                if (validComponents.Length > 1)
                {
                    Debug.LogWarning($"Found multiple {componentType.Namespace} components that match path {targetComponent.gameObject.GetPathName()} on prefab {toHierarchyRoot} -- cannot pick one!");
                }
            }
            else
            {
                Debug.LogWarning($"Unable to find GameObject that matches path {targetComponent.gameObject.GetPathName()} on prefab {toHierarchyRoot}");
            }

            matchingComponent = null;
            return false;
        }

        private static bool PathBelongsToProject(string path)
        {
            if (!path.StartsWith(@"assets", StringComparison.InvariantCultureIgnoreCase))
            {
                // This asset lives outside the project folder, likely in Packages
                return false;
            }

            if (path.StartsWith(@"assets/addons", StringComparison.InvariantCultureIgnoreCase) ||
                path.StartsWith(@"assets/plugins", StringComparison.InvariantCultureIgnoreCase))
            {
                // This asset lives in an external addon or plugin
                return false;
            }

            return true;
        }


        #endregion
    }
}

