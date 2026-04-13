using System.Collections.Generic;
using ChillAI.Core.Settings;
using ChillAI.Model.UserProfile;
using UnityEditor;
using UnityEngine;

namespace ChillAI.Editor
{
    [CustomPropertyDrawer(typeof(QuestRuleCondition))]
    public class QuestRuleConditionDrawer : PropertyDrawer
    {
        static readonly List<string> ProfileIds = new();
        static readonly List<string> ProfileLabels = new();
        static bool _profileCacheReady;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var lines = 1; // conditionType
            var type = GetConditionType(property);

            if (type == QuestRuleConditionType.ProfileFieldExists ||
                type == QuestRuleConditionType.ChatContainsAnyKeyword)
                lines += 1;

            if (type == QuestRuleConditionType.TaskCountReached)
                lines += 1;

            return lines * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeProp = property.FindPropertyRelative("conditionType");
            var stringArgProp = property.FindPropertyRelative("stringArg");
            var intArgProp = property.FindPropertyRelative("intArg");

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, lineHeight);

            EditorGUI.PropertyField(rect, typeProp);
            rect.y += lineHeight + spacing;

            var type = GetConditionType(property);
            switch (type)
            {
                case QuestRuleConditionType.ProfileFieldExists:
                    DrawProfileQuestionPopup(rect, stringArgProp);
                    break;
                case QuestRuleConditionType.ChatContainsAnyKeyword:
                    EditorGUI.PropertyField(rect, stringArgProp, new GUIContent("stringArg (keywords)"));
                    break;
                case QuestRuleConditionType.TaskCountReached:
                    EditorGUI.PropertyField(rect, intArgProp, new GUIContent("intArg (min task count)"));
                    break;
            }

            EditorGUI.EndProperty();
        }

        static QuestRuleConditionType GetConditionType(SerializedProperty property)
        {
            var typeProp = property.FindPropertyRelative("conditionType");
            return typeProp == null ? QuestRuleConditionType.None : (QuestRuleConditionType)typeProp.enumValueIndex;
        }

        static void DrawProfileQuestionPopup(Rect rect, SerializedProperty stringArgProp)
        {
            EnsureProfileCache();
            if (ProfileIds.Count == 0)
            {
                EditorGUI.PropertyField(rect, stringArgProp, new GUIContent("stringArg (profile question id)"));
                return;
            }

            var current = stringArgProp.stringValue;
            var currentIndex = ProfileIds.IndexOf(current);
            if (currentIndex < 0) currentIndex = 0;

            var newIndex = EditorGUI.Popup(rect, "stringArg (profile question id)", currentIndex, ProfileLabels.ToArray());
            if (newIndex >= 0 && newIndex < ProfileIds.Count)
                stringArgProp.stringValue = ProfileIds[newIndex];
        }

        static void EnsureProfileCache()
        {
            if (_profileCacheReady) return;
            _profileCacheReady = true;

            ProfileIds.Clear();
            ProfileLabels.Clear();

            foreach (var q in ProfileQuestions.All)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.id)) continue;
                ProfileIds.Add(q.id);
                ProfileLabels.Add($"{q.id}  ({q.label})");
            }
        }
    }
}
