#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    internal sealed class PairScaleChoiceWindow : EditorWindow
    {
        private const string ScaleFormat = "G4";
        private Vector3? left;
        private Vector3? right;
        private bool adding;
        private Action<bool, Vector3> selected;
        private GameObject source;
        private BoneReference leftBone;
        private BoneReference rightBone;
        private Vector3 hierarchyLeft;
        private Vector3 hierarchyRight;
        private bool hierarchyRead;
        private string issue;
        private Vector2 scroll;

        private void OnEnable() { EditorApplication.delayCall += CloseIfUninitialized; }
        private void OnDisable() { EditorApplication.delayCall -= CloseIfUninitialized; }
        private void CloseIfUninitialized() { if (selected == null) Close(); }

        internal sealed class Candidate
        {
            internal Vector3 value;
            internal string label;
            internal bool preserveInput;
        }

        internal static List<Candidate> Candidates(bool adding, Vector3? left, Vector3? right,
            Vector3? hierarchyLeft, Vector3? hierarchyRight)
        {
            var result = new List<Candidate>();
            if (!adding) AddCandidate(result, Vector3.one, "すべて1");
            AddCandidate(result, left, adding ? "L" : "設定L", !adding);
            AddCandidate(result, right, adding ? "R" : "設定R", !adding);
            if (!adding)
            {
                AddCandidate(result, hierarchyLeft, "Hierarchy L");
                AddCandidate(result, hierarchyRight, "Hierarchy R");
            }
            return result;
        }

        private static void AddCandidate(List<Candidate> candidates, Vector3? value, string label, bool preserveInput = false)
        {
            if (!value.HasValue) return;
            Vector3 selectedValue = preserveInput ? value.Value : DisplayedScale(value.Value);
            var existing = candidates.FirstOrDefault(candidate => candidate.value.Equals(selectedValue));
            if (existing != null)
            {
                existing.label += " / " + label;
                existing.preserveInput |= preserveInput;
            }
            else candidates.Add(new Candidate { value = selectedValue, label = label, preserveInput = preserveInput });
        }

        internal static PairScaleChoiceWindow Choose(Vector3? left, Vector3? right, bool adding, GameObject source,
            BoneReference leftBone, BoneReference rightBone, Action<bool, Vector3> selected)
        {
            var window = CreateInstance<PairScaleChoiceWindow>();
            window.titleContent = new GUIContent(adding ? "左右のScaleを確認" : "左右セットのScaleを選択");
            window.minSize = new Vector2(480, adding ? 250 : 390);
            window.maxSize = new Vector2(720, 600);
            window.position = new Rect(200, 200, 540, adding ? 260 : 430);
            window.left = left;
            window.right = right;
            window.adding = adding;
            window.source = source;
            window.leftBone = leftBone;
            window.rightBone = rightBone;
            window.selected = selected;
            if (!adding) window.ReadHierarchy();
            window.ShowUtility();
            return window;
        }

        internal static string Values(Vector3 value)
        {
            return "X " + value.x.ToString(ScaleFormat) + "   Y " + value.y.ToString(ScaleFormat) + "   Z " + value.z.ToString(ScaleFormat);
        }

        internal static string SettingValues(Vector3 value)
        {
            return "X " + SettingAxis(value.x) + "   Y " + SettingAxis(value.y) + "   Z " + SettingAxis(value.z);
        }

        private static string SettingAxis(float value)
        {
            // Use the usual concise float display, extending it only when needed to distinguish entered values.
            foreach (string format in new[] { "G7", "G8" })
                if (float.TryParse(value.ToString(format, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float restored) && restored.Equals(value))
                    return value.ToString(format);
            return value.ToString("G9");
        }

        internal static Vector3 DisplayedScale(Vector3 value)
        {
            return new Vector3(DisplayedAxis(value.x), DisplayedAxis(value.y), DisplayedAxis(value.z));
        }

        private static float DisplayedAxis(float value)
        {
            // Parse the displayed decimal back to a float, rather than retaining hidden digits.
            return (float)double.Parse(value.ToString(ScaleFormat, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Label(adding ? "左右のScaleが異なります。左右別に追加しますか？" :
                "左右セットに使うXYZの値を選んでください。", EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);
            GUILayout.Label("L：" + (left.HasValue ? (adding ? Values(left.Value) : SettingValues(left.Value)) : "未登録"), EditorStyles.wordWrappedLabel);
            GUILayout.Label("R：" + (right.HasValue ? (adding ? Values(right.Value) : SettingValues(right.Value)) : "未登録"), EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);
            if (adding && GUILayout.Button("左右別に追加", GUILayout.Height(26))) Finish(true, left.Value);
            if (!adding)
            {
                GUILayout.Space(10);
                EditorGUI.BeginChangeCheck();
                source = (GameObject)EditorGUILayout.ObjectField("Hierarchyの読み取り元", source, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) ReadHierarchy();
                if (GUILayout.Button("Hierarchyの数値を再取得")) ReadHierarchy();
                if (!string.IsNullOrEmpty(issue)) EditorGUILayout.HelpBox(issue, MessageType.Warning);
                if (hierarchyRead)
                {
                    GUILayout.Label("Hierarchy L：" + Values(hierarchyLeft), EditorStyles.wordWrappedLabel);
                    GUILayout.Label("Hierarchy R：" + Values(hierarchyRight), EditorStyles.wordWrappedLabel);
                }
            }
            GUILayout.Space(8);
            foreach (var candidate in Candidates(adding, left, right,
                hierarchyRead ? (Vector3?)hierarchyLeft : null, hierarchyRead ? (Vector3?)hierarchyRight : null))
                ValueButton(candidate.label, candidate.value, candidate.preserveInput);
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("キャンセル")) Close();
        }

        private void ValueButton(string label, Vector3 value, bool preserveInput)
        {
            var style = new GUIStyle(GUI.skin.button) { wordWrap = true };
            using (new EditorGUI.DisabledScope(!TransformScaleUtility.IsValidScale(value)))
                if (GUILayout.Button(label + "：" + (preserveInput ? SettingValues(value) : Values(value)), style,
                    GUILayout.MinHeight(24))) Finish(false, value);
        }

        private void ReadHierarchy()
        {
            hierarchyRead = false;
            issue = null;
            if (!TryReadHierarchy(source, leftBone, rightBone, out hierarchyLeft, out hierarchyRight))
            {
                issue = "読み取り元のHierarchyオブジェクトと、対応するL/Rボーンを確認してください。";
                return;
            }
            hierarchyRead = true;
        }

        internal static bool TryReadHierarchy(GameObject source, BoneReference leftBone, BoneReference rightBone,
            out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.one;
            if (source == null || !TransformScaleUtility.IsEditable(source.transform)) return false;
            var l = AvatarBoneCatalog.ResolveBone(source, leftBone);
            var r = AvatarBoneCatalog.ResolveBone(source, rightBone);
            if (l == null || r == null || l == r) return false;
            left = l.localScale;
            right = r.localScale;
            return true;
        }

        private void Finish(bool separate, Vector3 value)
        {
            var callback = selected;
            selected = null;
            Close();
            callback?.Invoke(separate, adding ? DisplayedScale(value) : value);
            GUIUtility.ExitGUI();
        }
    }
}
#endif
