#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using StellarNet.Lite.Client.Components.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// Custom inspector for NetAnimatorView.
    /// It auto-discovers animator states and float parameters, then exposes them as visual sync mappings.
    /// </summary>
    [CustomEditor(typeof(NetAnimatorView))]
    public sealed class NetAnimatorViewEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetAnimatorProp;
        private SerializedProperty _sourceControllerProp;
        private SerializedProperty _paramSmoothTimeProp;
        private SerializedProperty _animCrossFadeTimeProp;
        private SerializedProperty _enableAntiIceProp;
        private SerializedProperty _antiIceTargetProp;
        private SerializedProperty _antiIceSourceProp;
        private SerializedProperty _syncedStatesProp;
        private SerializedProperty _syncedFloatParamsProp;

        private readonly List<string> _detectedStateNames = new List<string>();
        private readonly List<string> _detectedFloatParamNames = new List<string>();
        private readonly HashSet<string> _detectedStateNameSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _detectedFloatParamNameSet = new HashSet<string>(StringComparer.Ordinal);

        private void OnEnable()
        {
            _targetAnimatorProp = serializedObject.FindProperty("TargetAnimator");
            _sourceControllerProp = serializedObject.FindProperty("SourceAnimatorController");
            _paramSmoothTimeProp = serializedObject.FindProperty("ParamSmoothTime");
            _animCrossFadeTimeProp = serializedObject.FindProperty("AnimCrossFadeTime");
            _enableAntiIceProp = serializedObject.FindProperty("EnableAntiIceSkating");
            _antiIceTargetProp = serializedObject.FindProperty("AntiIceTargetStates");
            _antiIceSourceProp = serializedObject.FindProperty("AntiIceSourceStates");
            _syncedStatesProp = serializedObject.FindProperty("SyncedStates");
            _syncedFloatParamsProp = serializedObject.FindProperty("SyncedFloatParams");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            RuntimeAnimatorController controller = ResolveAnimatorController();
            CollectAnimatorMetadata(controller);

            DrawBasicSection();
            DrawControllerInfo(controller);
            DrawStateMappingSection();
            DrawParamMappingSection();
            DrawAntiIceSection();
            DrawValidationSection(controller);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBasicSection()
        {
            EditorGUILayout.LabelField("Animator 设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_targetAnimatorProp);
            EditorGUILayout.PropertyField(_sourceControllerProp, new GUIContent("源 Animator Controller"));
            EditorGUILayout.PropertyField(_paramSmoothTimeProp);
            EditorGUILayout.PropertyField(_animCrossFadeTimeProp);
            EditorGUILayout.Space(6f);
        }

        private void DrawControllerInfo(RuntimeAnimatorController controller)
        {
            EditorGUILayout.LabelField("Controller 扫描", EditorStyles.boldLabel);

            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "请先指定源 Animator Controller 或 TargetAnimator，这样 Inspector 才能自动扫描状态和 Float 参数。",
                    MessageType.Info);
                return;
            }

            if (controller is AnimatorOverrideController)
            {
                EditorGUILayout.HelpBox(
                    "当前使用的是 AnimatorOverrideController。Inspector 会基于它的基础 AnimatorController 做状态和参数校验。",
                    MessageType.Info);
            }

            int enabledStateCount = CountEnabledEntries(_syncedStatesProp, "Enabled");
            int enabledParamCount = CountEnabledEntries(_syncedFloatParamsProp, "Enabled");
            EditorGUILayout.HelpBox(
                $"当前 Controller: {controller.name}\n扫描到的状态数量: {_detectedStateNames.Count}\n扫描到的 Float 参数数量: {_detectedFloatParamNames.Count}\n已启用的状态映射数量: {enabledStateCount}\n已启用的参数映射数量: {enabledParamCount}",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新状态映射"))
            {
                SyncStateMappingsFromController();
            }

            if (GUILayout.Button("刷新参数映射"))
            {
                SyncParamMappingsFromController();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全部刷新"))
            {
                SyncStateMappingsFromController();
                SyncParamMappingsFromController();
            }

            if (GUILayout.Button("清理无效映射"))
            {
                RemoveInvalidMappings();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
        }

        private void DrawStateMappingSection()
        {
            EditorGUILayout.LabelField("状态同步映射", EditorStyles.boldLabel);

            if (_detectedStateNames.Count == 0)
            {
                EditorGUILayout.HelpBox("当前选中的 Controller 中没有扫描到任何 Animator 状态。", MessageType.Warning);
            }

            for (int i = 0; i < _detectedStateNames.Count; i++)
            {
                string animatorStateName = _detectedStateNames[i];
                SerializedProperty entryProp = GetOrCreateStateEntry(animatorStateName);
                SerializedProperty enabledProp = entryProp.FindPropertyRelative("Enabled");
                SerializedProperty logicNameProp = entryProp.FindPropertyRelative("LogicStateName");
                SerializedProperty animatorNameProp = entryProp.FindPropertyRelative("AnimatorStateName");

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                enabledProp.boolValue = EditorGUILayout.Toggle(enabledProp.boolValue, GUILayout.Width(18f));
                EditorGUILayout.LabelField($"Animator 状态: {animatorStateName}", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                logicNameProp.stringValue = EditorGUILayout.TextField(
                    "逻辑状态名",
                    string.IsNullOrWhiteSpace(logicNameProp.stringValue) ? animatorStateName : logicNameProp.stringValue);
                animatorNameProp.stringValue = animatorStateName;

                DrawStateEntryHint(logicNameProp.stringValue, animatorStateName);
                EditorGUILayout.EndVertical();
            }

            DrawManualAddButtons(_syncedStatesProp, true);
            DrawExtraStateEntries();
            EditorGUILayout.Space(6f);
        }

        private void DrawParamMappingSection()
        {
            EditorGUILayout.LabelField("Float 参数同步映射", EditorStyles.boldLabel);

            if (_detectedFloatParamNames.Count == 0)
            {
                EditorGUILayout.HelpBox("当前选中的 Controller 中没有扫描到任何 Animator Float 参数。", MessageType.Info);
            }

            for (int i = 0; i < _detectedFloatParamNames.Count; i++)
            {
                string animatorParamName = _detectedFloatParamNames[i];
                SerializedProperty entryProp = GetOrCreateParamEntry(animatorParamName);
                SerializedProperty enabledProp = entryProp.FindPropertyRelative("Enabled");
                SerializedProperty logicNameProp = entryProp.FindPropertyRelative("LogicParamName");
                SerializedProperty animatorNameProp = entryProp.FindPropertyRelative("AnimatorParamName");

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                enabledProp.boolValue = EditorGUILayout.Toggle(enabledProp.boolValue, GUILayout.Width(18f));
                EditorGUILayout.LabelField($"Animator Float 参数: {animatorParamName}", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                logicNameProp.stringValue = EditorGUILayout.TextField(
                    "逻辑参数名",
                    string.IsNullOrWhiteSpace(logicNameProp.stringValue) ? animatorParamName : logicNameProp.stringValue);
                animatorNameProp.stringValue = animatorParamName;

                DrawParamEntryHint(logicNameProp.stringValue, animatorParamName);
                EditorGUILayout.EndVertical();
            }

            DrawManualAddButtons(_syncedFloatParamsProp, false);
            DrawExtraParamEntries();
            EditorGUILayout.Space(6f);
        }

        private void DrawManualAddButtons(SerializedProperty listProp, bool isState)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isState ? "手动添加状态映射" : "手动添加参数映射"))
            {
                int index = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(index);
                SerializedProperty newEntry = listProp.GetArrayElementAtIndex(index);
                newEntry.FindPropertyRelative("Enabled").boolValue = false;
                newEntry.FindPropertyRelative(isState ? "LogicStateName" : "LogicParamName").stringValue = string.Empty;
                newEntry.FindPropertyRelative(isState ? "AnimatorStateName" : "AnimatorParamName").stringValue = string.Empty;
            }

            if (GUILayout.Button(isState ? "全部禁用状态" : "全部禁用参数"))
            {
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    listProp.GetArrayElementAtIndex(i).FindPropertyRelative("Enabled").boolValue = false;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStateEntryHint(string logicStateName, string animatorStateName)
        {
            if (string.IsNullOrWhiteSpace(logicStateName))
            {
                EditorGUILayout.HelpBox("这个状态当前未启用，或缺少逻辑状态名。未配置完成前，服务端无法把动画状态正确分发到这里。", MessageType.Warning);
                return;
            }

            if (!string.Equals(logicStateName.Trim(), animatorStateName, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox($"服务端逻辑状态 “{logicStateName.Trim()}” 会驱动 Animator 状态 “{animatorStateName}”。", MessageType.None);
            }
        }

        private void DrawParamEntryHint(string logicParamName, string animatorParamName)
        {
            if (string.IsNullOrWhiteSpace(logicParamName))
            {
                EditorGUILayout.HelpBox("这个参数当前未启用，或缺少逻辑参数名。未配置完成前，服务端无法把参数正确分发到这里。", MessageType.Warning);
                return;
            }

            if (!string.Equals(logicParamName.Trim(), animatorParamName, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox($"服务端逻辑参数 “{logicParamName.Trim()}” 会驱动 Animator Float 参数 “{animatorParamName}”。", MessageType.None);
            }
        }

        private void DrawAntiIceSection()
        {
            EditorGUILayout.LabelField("防滑步设置", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableAntiIceProp);
            EditorGUILayout.PropertyField(_antiIceTargetProp, true);
            EditorGUILayout.PropertyField(_antiIceSourceProp, true);
            EditorGUILayout.HelpBox(
                "防滑步会把当前位移预测结果与即将切换的动画状态一起判断。如果物体看起来还在移动，它会暂时保留上一个移动状态，而不是立刻切到 Idle 一类的停步状态。",
                MessageType.None);
            EditorGUILayout.Space(6f);
        }

        private void DrawValidationSection(RuntimeAnimatorController controller)
        {
            EditorGUILayout.LabelField("配置校验", EditorStyles.boldLabel);

            if (controller == null)
            {
                EditorGUILayout.HelpBox("当前还没有可用的 Animator Controller，因此只能做有限校验。", MessageType.Warning);
                return;
            }

            bool hasIssue = false;

            if (HasDuplicateLogicNames(_syncedStatesProp, "Enabled", "LogicStateName"))
            {
                EditorGUILayout.HelpBox("检测到重复的逻辑状态名。同一个服务端逻辑状态名应只映射到一个 Animator 状态。", MessageType.Error);
                hasIssue = true;
            }

            if (HasDuplicateLogicNames(_syncedFloatParamsProp, "Enabled", "LogicParamName"))
            {
                EditorGUILayout.HelpBox("检测到重复的逻辑 Float 参数名。同一个服务端逻辑参数名应只映射到一个 Animator Float 参数。", MessageType.Error);
                hasIssue = true;
            }

            for (int i = 0; i < _syncedStatesProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedStatesProp.GetArrayElementAtIndex(i);
                if (!entryProp.FindPropertyRelative("Enabled").boolValue)
                {
                    continue;
                }

                string logicName = entryProp.FindPropertyRelative("LogicStateName").stringValue?.Trim();
                string animatorName = entryProp.FindPropertyRelative("AnimatorStateName").stringValue?.Trim();

                if (string.IsNullOrEmpty(logicName))
                {
                    EditorGUILayout.HelpBox($"第 {i + 1} 条状态映射已启用，但缺少逻辑状态名。", MessageType.Warning);
                    hasIssue = true;
                }

                if (string.IsNullOrEmpty(animatorName) || !_detectedStateNameSet.Contains(animatorName))
                {
                    EditorGUILayout.HelpBox($"当前 Controller 中不存在这个 Animator 状态: {animatorName}", MessageType.Error);
                    hasIssue = true;
                }
            }

            for (int i = 0; i < _syncedFloatParamsProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedFloatParamsProp.GetArrayElementAtIndex(i);
                if (!entryProp.FindPropertyRelative("Enabled").boolValue)
                {
                    continue;
                }

                string logicName = entryProp.FindPropertyRelative("LogicParamName").stringValue?.Trim();
                string animatorName = entryProp.FindPropertyRelative("AnimatorParamName").stringValue?.Trim();

                if (string.IsNullOrEmpty(logicName))
                {
                    EditorGUILayout.HelpBox($"第 {i + 1} 条参数映射已启用，但缺少逻辑参数名。", MessageType.Warning);
                    hasIssue = true;
                }

                if (string.IsNullOrEmpty(animatorName) || !_detectedFloatParamNameSet.Contains(animatorName))
                {
                    EditorGUILayout.HelpBox($"当前 Controller 中不存在这个 Animator Float 参数: {animatorName}", MessageType.Error);
                    hasIssue = true;
                }
            }

            ValidateAntiIceList(_antiIceTargetProp, "AntiIceTargetStates", ref hasIssue);
            ValidateAntiIceList(_antiIceSourceProp, "AntiIceSourceStates", ref hasIssue);

            int enabledStateCount = CountEnabledEntries(_syncedStatesProp, "Enabled");
            if (enabledStateCount <= 0 && _detectedStateNames.Count > 0)
            {
                EditorGUILayout.HelpBox("当前没有启用任何状态映射。这样服务端动画状态将无法分发到 Animator。", MessageType.Warning);
                hasIssue = true;
            }

            if (!hasIssue)
            {
                EditorGUILayout.HelpBox("NetAnimatorView 当前配置校验通过。", MessageType.Info);
            }
        }

        private void ValidateAntiIceList(SerializedProperty listProp, string listName, ref bool hasIssue)
        {
            for (int i = 0; i < listProp.arraySize; i++)
            {
                string value = listProp.GetArrayElementAtIndex(i).stringValue?.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (_detectedStateNameSet.Contains(value))
                {
                    continue;
                }

                bool matchedLogicName = false;
                for (int j = 0; j < _syncedStatesProp.arraySize; j++)
                {
                    SerializedProperty stateProp = _syncedStatesProp.GetArrayElementAtIndex(j);
                    if (!stateProp.FindPropertyRelative("Enabled").boolValue)
                    {
                        continue;
                    }

                    string logicName = stateProp.FindPropertyRelative("LogicStateName").stringValue?.Trim();
                    if (string.Equals(logicName, value, StringComparison.Ordinal))
                    {
                        matchedLogicName = true;
                        break;
                    }
                }

                if (!matchedLogicName)
                {
                    EditorGUILayout.HelpBox($"{listName} 中包含未知的状态名或逻辑状态名: {value}", MessageType.Warning);
                    hasIssue = true;
                }
            }
        }

        private void CollectAnimatorMetadata(RuntimeAnimatorController controller)
        {
            _detectedStateNames.Clear();
            _detectedFloatParamNames.Clear();
            _detectedStateNameSet.Clear();
            _detectedFloatParamNameSet.Clear();

            AnimatorController animatorController = ResolveAnimatorControllerAsset(controller);
            if (animatorController == null)
            {
                return;
            }

            for (int i = 0; i < animatorController.layers.Length; i++)
            {
                CollectStatesRecursive(animatorController.layers[i].stateMachine);
            }

            _detectedStateNames.Sort(StringComparer.Ordinal);

            for (int i = 0; i < animatorController.parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = animatorController.parameters[i];
                if (parameter.type != AnimatorControllerParameterType.Float)
                {
                    continue;
                }

                if (_detectedFloatParamNameSet.Add(parameter.name))
                {
                    _detectedFloatParamNames.Add(parameter.name);
                }
            }

            _detectedFloatParamNames.Sort(StringComparer.Ordinal);
        }

        private void CollectStatesRecursive(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null)
            {
                return;
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null)
                {
                    continue;
                }

                if (_detectedStateNameSet.Add(state.name))
                {
                    _detectedStateNames.Add(state.name);
                }
            }

            ChildAnimatorStateMachine[] childMachines = stateMachine.stateMachines;
            for (int i = 0; i < childMachines.Length; i++)
            {
                CollectStatesRecursive(childMachines[i].stateMachine);
            }
        }

        private RuntimeAnimatorController ResolveAnimatorController()
        {
            RuntimeAnimatorController controller = _sourceControllerProp.objectReferenceValue as RuntimeAnimatorController;
            if (controller != null)
            {
                return controller;
            }

            Animator animator = _targetAnimatorProp.objectReferenceValue as Animator;
            return animator != null ? animator.runtimeAnimatorController : null;
        }

        private static AnimatorController ResolveAnimatorControllerAsset(RuntimeAnimatorController runtimeController)
        {
            if (runtimeController == null)
            {
                return null;
            }

            if (runtimeController is AnimatorController animatorController)
            {
                return animatorController;
            }

            if (runtimeController is AnimatorOverrideController overrideController)
            {
                return overrideController.runtimeAnimatorController as AnimatorController;
            }

            return null;
        }

        private void SyncStateMappingsFromController()
        {
            for (int i = 0; i < _detectedStateNames.Count; i++)
            {
                GetOrCreateStateEntry(_detectedStateNames[i]);
            }
        }

        private void SyncParamMappingsFromController()
        {
            for (int i = 0; i < _detectedFloatParamNames.Count; i++)
            {
                GetOrCreateParamEntry(_detectedFloatParamNames[i]);
            }
        }

        private void RemoveInvalidMappings()
        {
            for (int i = _syncedStatesProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entryProp = _syncedStatesProp.GetArrayElementAtIndex(i);
                string animatorStateName = entryProp.FindPropertyRelative("AnimatorStateName").stringValue?.Trim();
                if (string.IsNullOrEmpty(animatorStateName) || !_detectedStateNameSet.Contains(animatorStateName))
                {
                    _syncedStatesProp.DeleteArrayElementAtIndex(i);
                }
            }

            for (int i = _syncedFloatParamsProp.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entryProp = _syncedFloatParamsProp.GetArrayElementAtIndex(i);
                string animatorParamName = entryProp.FindPropertyRelative("AnimatorParamName").stringValue?.Trim();
                if (string.IsNullOrEmpty(animatorParamName) || !_detectedFloatParamNameSet.Contains(animatorParamName))
                {
                    _syncedFloatParamsProp.DeleteArrayElementAtIndex(i);
                }
            }
        }

        private SerializedProperty GetOrCreateStateEntry(string animatorStateName)
        {
            for (int i = 0; i < _syncedStatesProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedStatesProp.GetArrayElementAtIndex(i);
                string value = entryProp.FindPropertyRelative("AnimatorStateName").stringValue?.Trim();
                if (string.Equals(value, animatorStateName, StringComparison.Ordinal))
                {
                    return entryProp;
                }
            }

            int index = _syncedStatesProp.arraySize;
            _syncedStatesProp.InsertArrayElementAtIndex(index);
            SerializedProperty newEntry = _syncedStatesProp.GetArrayElementAtIndex(index);
            newEntry.FindPropertyRelative("Enabled").boolValue = false;
            newEntry.FindPropertyRelative("LogicStateName").stringValue = animatorStateName;
            newEntry.FindPropertyRelative("AnimatorStateName").stringValue = animatorStateName;
            return newEntry;
        }

        private SerializedProperty GetOrCreateParamEntry(string animatorParamName)
        {
            for (int i = 0; i < _syncedFloatParamsProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedFloatParamsProp.GetArrayElementAtIndex(i);
                string value = entryProp.FindPropertyRelative("AnimatorParamName").stringValue?.Trim();
                if (string.Equals(value, animatorParamName, StringComparison.Ordinal))
                {
                    return entryProp;
                }
            }

            int index = _syncedFloatParamsProp.arraySize;
            _syncedFloatParamsProp.InsertArrayElementAtIndex(index);
            SerializedProperty newEntry = _syncedFloatParamsProp.GetArrayElementAtIndex(index);
            newEntry.FindPropertyRelative("Enabled").boolValue = false;
            newEntry.FindPropertyRelative("LogicParamName").stringValue = animatorParamName;
            newEntry.FindPropertyRelative("AnimatorParamName").stringValue = animatorParamName;
            return newEntry;
        }

        private void DrawExtraStateEntries()
        {
            for (int i = 0; i < _syncedStatesProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedStatesProp.GetArrayElementAtIndex(i);
                string animatorStateName = entryProp.FindPropertyRelative("AnimatorStateName").stringValue?.Trim();
                if (!string.IsNullOrEmpty(animatorStateName) && _detectedStateNameSet.Contains(animatorStateName))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("手动状态映射", EditorStyles.boldLabel);
                if (GUILayout.Button("删除此映射", GUILayout.Width(90f)))
                {
                    _syncedStatesProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("Enabled"));
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("LogicStateName"));
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("AnimatorStateName"));
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawExtraParamEntries()
        {
            for (int i = 0; i < _syncedFloatParamsProp.arraySize; i++)
            {
                SerializedProperty entryProp = _syncedFloatParamsProp.GetArrayElementAtIndex(i);
                string animatorParamName = entryProp.FindPropertyRelative("AnimatorParamName").stringValue?.Trim();
                if (!string.IsNullOrEmpty(animatorParamName) && _detectedFloatParamNameSet.Contains(animatorParamName))
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("手动参数映射", EditorStyles.boldLabel);
                if (GUILayout.Button("删除此映射", GUILayout.Width(90f)))
                {
                    _syncedFloatParamsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("Enabled"));
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("LogicParamName"));
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("AnimatorParamName"));
                EditorGUILayout.EndVertical();
            }
        }

        private static bool HasDuplicateLogicNames(SerializedProperty listProp, string enabledFieldName, string logicFieldName)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty entry = listProp.GetArrayElementAtIndex(i);
                if (!entry.FindPropertyRelative(enabledFieldName).boolValue)
                {
                    continue;
                }

                string value = entry.FindPropertyRelative(logicFieldName).stringValue?.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!values.Add(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountEnabledEntries(SerializedProperty listProp, string enabledFieldName)
        {
            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).FindPropertyRelative(enabledFieldName).boolValue)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
#endif
