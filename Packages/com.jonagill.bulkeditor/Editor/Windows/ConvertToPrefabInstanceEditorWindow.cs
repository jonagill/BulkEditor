#if UNITY_2022_2_OR_NEWER

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BulkEditor
{
    /// <summary>
    /// An EditorWindow that provides configurable access to Unity's PrefabUtility.ConvertToPrefabInstance() function
    /// </summary>
    [Serializable]
    public class ConvertToPrefabEditorWindow : EditorWindow
    {
        private struct GameObjectToRemove
        {
            public string hierarchyPath;
        }

        private struct ComponentToRemove
        {
            public string hierarchyPath;
            public Type componentType;
        }

        private struct NestedPrefabInstances
        {
            public GameObject instance;
            public GameObject prefab;
        }

        [SerializeField] private GameObject _sourcePrefab;
        [SerializeField] private GameObject[] _targetObjects;

        private ConvertToPrefabInstanceSettings _settings = new ConvertToPrefabInstanceSettings()
        {
            objectMatchMode = ObjectMatchMode.ByHierarchy,
            componentsNotMatchedBecomesOverride = true,
            gameObjectsNotMatchedBecomesOverride = true,
            recordPropertyOverridesOfMatches = true,
            changeRootNameToAssetName = false,
            logInfo = true
        };

        [SerializeField] private bool _missingComponentsBecomeOverride = true;
        [SerializeField] private bool _missingGameObjectsBecomeOverride = true;
        [SerializeField] private bool _nestedPrefabsNotMatchedRemainLinked = true;

        [MenuItem("Tools/Bulk Editing/Prefabs/Windows/Convert To Prefab Instance Window")]
        public static void ShowWindow()
        {
            ConvertToPrefabEditorWindow wnd = GetWindow<ConvertToPrefabEditorWindow>();
            wnd.titleContent = new GUIContent("Convert to Prefab Instance");
        }

        public void CreateGUI()
        {
            var sourcePrefabField = new ObjectField("Prefab");
            sourcePrefabField.value = _sourcePrefab;
            sourcePrefabField.objectType = typeof(GameObject);
            sourcePrefabField.allowSceneObjects = false;

            var advancedSettingsFoldout = EditorGUIHelpers.CreateSavedFoldout(nameof(ConvertToPrefabEditorWindow), "Advanced");

            var objectMatchModeField = EditorGUIHelpers.CreateEnumField(
                "Object Match Mode",
                "Use this property to control how GameObjects and Components are matched up or not when converting a plain GameObject to a Prefab instance.",
                () => _settings.objectMatchMode,
                v => _settings.objectMatchMode = (ObjectMatchMode)v);

            var componentsNotMatchedBecomesOverrideToggle = EditorGUIHelpers.CreateBoolToggle(
                "Unmatched Components Become Overrides",
                "If a Component is not matched up then it can become an added Component on the new Prefab instance. This property is only used when used together with ObjectMatchMode.ByHierarchy.",
                () => _settings.componentsNotMatchedBecomesOverride,
                v => _settings.componentsNotMatchedBecomesOverride = v);

            var gameObjectsNotMatchedBecomesOverrideToggle = EditorGUIHelpers.CreateBoolToggle(
                "Unmatched GameObjects Become Overrides",
                "If a GameObject is not matched up then it can become an added GameObject on the new Prefab instance. This property is only used when used together with ObjectMatchMode.ByHierarchy.",
                () => _settings.gameObjectsNotMatchedBecomesOverride,
                v => _settings.gameObjectsNotMatchedBecomesOverride = v);

            var nestedPrefabsNotMatchedRemainLinkedToggle = EditorGUIHelpers.CreateBoolToggle(
                "Nested Prefabs Remain Linked",
                "If a GameObject is not matched up and is an instance of another prefab, keep the links to that prefab.",
                () => _missingComponentsBecomeOverride,
                v => _missingComponentsBecomeOverride = v);

            var missingComponentsBecomeOverrideToggle = EditorGUIHelpers.CreateBoolToggle(
                "Missing Components Become Overrides",
                "If a Component is missing on the instance than it can become a removed Component on the new Prefab instance. This property is only used when used together with ObjectMatchMode.ByHierarchy.",
                () => _missingComponentsBecomeOverride,
                v => _missingComponentsBecomeOverride = v);

            var missingGameObjectsBecomeOverrideToggle = EditorGUIHelpers.CreateBoolToggle(
                "Missing GameObjects Become Overrides",
                "If a GameObjects is missing on the instance than it can become a removed GameObject on the new Prefab instance. This property is only used when used together with ObjectMatchMode.ByHierarchy.",
                () => _missingGameObjectsBecomeOverride,
                v => _missingGameObjectsBecomeOverride = v);

            var recordPropertyOverridesOfMatchesToggle = EditorGUIHelpers.CreateBoolToggle(
                "Record Property Overrides of Matches",
                "When a Component or GameObject is matched with objects in the Prefab Asset then existing values can be recorded as overrides on the new Prefab instance if this property is set to true.",
                () => _settings.recordPropertyOverridesOfMatches,
                v => _settings.recordPropertyOverridesOfMatches = v);

            var changeRootNameToAssetNameToggle = EditorGUIHelpers.CreateBoolToggle(
                "Change Root Name to Asset Name",
                "Change the name of the root GameObject to match the name of the Prefab Asset used when converting.",
                () => _settings.changeRootNameToAssetName,
                v => _settings.changeRootNameToAssetName = v);

            var logInfoToggle = EditorGUIHelpers.CreateBoolToggle(
                "Log Info",
                "Enables logging to the console with information about which objects were matched when converting a plain GameObject to a Prefab instance.",
                () => _settings.logInfo,
                v => _settings.logInfo = v);

            advancedSettingsFoldout.Add(objectMatchModeField);
            advancedSettingsFoldout.Add(componentsNotMatchedBecomesOverrideToggle);
            advancedSettingsFoldout.Add(gameObjectsNotMatchedBecomesOverrideToggle);
            advancedSettingsFoldout.Add(nestedPrefabsNotMatchedRemainLinkedToggle);
            advancedSettingsFoldout.Add(missingComponentsBecomeOverrideToggle);
            advancedSettingsFoldout.Add(missingGameObjectsBecomeOverrideToggle);
            advancedSettingsFoldout.Add(recordPropertyOverridesOfMatchesToggle);
            advancedSettingsFoldout.Add(changeRootNameToAssetNameToggle);
            advancedSettingsFoldout.Add(logInfoToggle);

            var convertButton = new Button(ConvertSelectedObjects);
            convertButton.text = "Convert Selected Objects";

            // Add callbacks
            void RefreshEnabledElements()
            {
                componentsNotMatchedBecomesOverrideToggle.SetEnabled(_settings.objectMatchMode == ObjectMatchMode.ByHierarchy);
                gameObjectsNotMatchedBecomesOverrideToggle.SetEnabled(_settings.objectMatchMode == ObjectMatchMode.ByHierarchy);
                missingComponentsBecomeOverrideToggle.SetEnabled(_settings.objectMatchMode == ObjectMatchMode.ByHierarchy);
                missingGameObjectsBecomeOverrideToggle.SetEnabled(_settings.objectMatchMode == ObjectMatchMode.ByHierarchy);
                nestedPrefabsNotMatchedRemainLinkedToggle.SetEnabled(_settings.objectMatchMode == ObjectMatchMode.ByHierarchy && _settings.gameObjectsNotMatchedBecomesOverride);

                convertButton.SetEnabled(_sourcePrefab != null);
            }

            sourcePrefabField.RegisterValueChangedCallback(evt =>
            {
                _sourcePrefab = (GameObject)evt.newValue;
                RefreshEnabledElements();
            });

            objectMatchModeField.RegisterValueChangedCallback(evt =>
            {
                RefreshEnabledElements();
            });

            gameObjectsNotMatchedBecomesOverrideToggle.RegisterValueChangedCallback(evt =>
            {
                RefreshEnabledElements();
            });

            RefreshEnabledElements();

            rootVisualElement.Add(sourcePrefabField);
            rootVisualElement.Add(advancedSettingsFoldout);
            rootVisualElement.Add(convertButton);
        }

        private void ConvertSelectedObjects()
        {
            if (_sourcePrefab == null)
            {
                return;
            }
            
            // Group together all our changes
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var targetObject in Selection.gameObjects)
            {
                ConvertObject(targetObject, _sourcePrefab);
            }
            
            Undo.CollapseUndoOperations(undoGroup);
            Undo.SetCurrentGroupName($"Convert to {_sourcePrefab.name}");
        }

        private void ConvertObject(GameObject targetObject, GameObject sourcePrefab)
        {
            var gameObjectsToRemove = new List<GameObjectToRemove>();
            var componentsToRemove = new List<ComponentToRemove>();
            var nestedPrefabInstances = new List<NestedPrefabInstances>();
            var scratchTransforms = new List<Transform>();
            var scratchFromComponents = new List<Component>();
            var scratchToComponents = new List<Component>();
            
            if (EditorUtility.IsPersistent(targetObject))
            {
                Debug.LogWarning($"Cannot convert {targetObject} to prefab instance as it is a persistent asset.");
                return;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(targetObject) &&
                !PrefabUtility.IsOutermostPrefabInstanceRoot(targetObject))
            {
                Debug.LogWarning($"Cannot convert {targetObject} as it is not at the root of its prefab.");
                return;
            }
            
            if (_nestedPrefabsNotMatchedRemainLinked &&
                _settings.objectMatchMode == ObjectMatchMode.ByHierarchy &&
                _settings.gameObjectsNotMatchedBecomesOverride)
            {
                targetObject.GetComponentsInChildren(true, scratchTransforms);

                foreach (var transform in scratchTransforms)
                {
                    if (transform == targetObject.transform)
                    {
                        continue;
                    }

                    if (PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject) &&
                        !BulkEditing.TryGetMatchingGameObject(
                            transform.gameObject,
                            targetObject,
                            sourcePrefab,
                            out _))
                    {
                        // There is no matching object in the prefab -- this will be unpacked entirely
                        // Preserve its own prefab link for us to hook up again later
                        
                        var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        
                        nestedPrefabInstances.Add(new NestedPrefabInstances()
                        {
                            instance = transform.gameObject,
                            prefab = prefab
                        });
                    }
                }
            }
            
            // Unpack everything fully so we can run the conversion
            // It would make things easier to only unpack the root, but that causes crashes when undoing
            if (PrefabUtility.IsPartOfNonAssetPrefabInstance(targetObject))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(targetObject);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            }
            
            if (_missingGameObjectsBecomeOverride && _settings.objectMatchMode == ObjectMatchMode.ByHierarchy)
            {
                sourcePrefab.GetComponentsInChildren(true, scratchTransforms);
                foreach (var sourceTransform in scratchTransforms)
                {
                    if (!BulkEditing.TryGetMatchingGameObject(
                            sourceTransform.gameObject,
                            sourcePrefab,
                            targetObject,
                            out _,
                            out var missingObjectPath))
                    {
                        gameObjectsToRemove.Add(new GameObjectToRemove()
                        {
                            hierarchyPath = missingObjectPath
                        });
                    }
                }
            }

            if (_missingComponentsBecomeOverride && _settings.objectMatchMode == ObjectMatchMode.ByHierarchy)
            {
                targetObject.GetComponentsInChildren(true, scratchTransforms);
                foreach (var transform in scratchTransforms)
                {
                    if (BulkEditing.TryGetMatchingGameObject(
                            transform.gameObject,
                            targetObject,
                            sourcePrefab,
                            out var matchingTransformObject,
                            out var matchingObjectPath))
                    {
                        transform.gameObject.GetComponents(scratchFromComponents);
                        matchingTransformObject.GetComponents(scratchToComponents);

                        foreach (var component in scratchToComponents)
                        {
                            var componentType = component.GetType();
                            if (!scratchFromComponents.Any(c => c.GetType() == componentType))
                            {
                                // There is a component on the prefab that doesn't exist on the instance
                                // Mark it for removal
                                componentsToRemove.Add(new ComponentToRemove()
                                {
                                    componentType = componentType,
                                    hierarchyPath = matchingObjectPath
                                });
                            }
                            else if (scratchToComponents.Count(c => c.GetType() == componentType) !=
                                      scratchFromComponents.Count(c => c.GetType() == componentType))
                            {
                                Debug.LogWarning($"{targetObject}: Differing numbers of {componentType.Name} component found on object {matchingTransformObject.GetPathName()}. " +
                                                  $"Cannot pend for automatic removal.");
                            }
                        }
                    }
                }
            }

            PrefabUtility.ConvertToPrefabInstance(
                targetObject,
                sourcePrefab,
                _settings,
                InteractionMode.UserAction);

            foreach (var gameObjectToRemove in gameObjectsToRemove)
            {
                var transformToRemove = targetObject.transform.Find(gameObjectToRemove.hierarchyPath);
                if (transformToRemove != null)
                {
                    DestroyImmediate(transformToRemove.gameObject);

                    if (_settings.logInfo)
                    {
                        Debug.Log($"{targetObject}: Removed GameObject {gameObjectToRemove.hierarchyPath} that was not present on the original object. ", targetObject);
                    }
                }

            }

            foreach (var componentToRemove in componentsToRemove)
            {
                var gameObject = targetObject.transform.Find(componentToRemove.hierarchyPath);
                var component = gameObject.GetComponent(componentToRemove.componentType);
                if (component == null)
                {
                    // Sometimes Unity will remove the unmatched components for us sometimes -- unclear what causes this,
                    // but we don't want to throw an exception in any case
                    continue;
                }

                DestroyImmediate(component);

                if (_settings.logInfo)
                {
                    Debug.Log($"{targetObject}: Removed {componentToRemove.componentType.Name} component that was not present on original object {gameObject}. ", gameObject);
                }
            }

            foreach (var nestedInstance in nestedPrefabInstances)
            {
                if (nestedInstance.instance == null) 
                {
                    // Our instance was destroyed during conversion
                    continue;
                }

                if (PrefabUtility.IsAnyPrefabInstanceRoot(nestedInstance.instance))
                {
                    // We remained hooked up successfully (or have already been hooked up again via a parent)
                    continue;
                }
                
                // Hook our prefab reference back up
                ConvertObject(nestedInstance.instance, nestedInstance.prefab);
            }
        }
    }
}

#endif