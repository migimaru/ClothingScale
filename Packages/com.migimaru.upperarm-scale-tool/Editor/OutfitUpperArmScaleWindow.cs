// UpperArm Scale Tool — Editor-only package.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PersonalAvatarTools
{
    public sealed class OutfitUpperArmScaleWindow : EditorWindow
    {
        private sealed class Target
        {
            public Transform Bone;
            public GameObject Root;
            public bool Enabled;
            public string Path;
        }

        private readonly List<Target> targets = new List<Target>();
        private float scale = 1f;
        private bool hasSavedScale;
        private string preferenceKey;
        private Vector2 scroll;
        private GUIStyle wrappedButtonStyle;
        private GUIStyle wrappedHeadingStyle;
        private string message = "Hierarchyで衣装の一番上を選び、読み込んでください。";

        private static void Open()
        {
            // Keep the existing menu entry while opening the unified profile-based window.
            if (!EditorApplication.ExecuteMenuItem("Tools/Clothing Scale"))
            {
                OpenLegacyWindow();
            }
        }

        private static void OpenLegacyWindow()
        {
            GetWindow<OutfitUpperArmScaleWindow>("衣装 UpperArm").minSize = new Vector2(360, 340);
        }

        private void OnEnable()
        {
            // The path only separates user preferences between projects.
            // No project name is inspected and no avatar-specific default is selected.
            preferenceKey = "PersonalAvatarTools.UpperArmScale.UserValue." + Application.dataPath;
            hasSavedScale = EditorPrefs.HasKey(preferenceKey);
            scale = hasSavedScale ? EditorPrefs.GetFloat(preferenceKey) : 1f;
            if (!IsValidScale(scale))
            {
                scale = 1f;
                hasSavedScale = false;
            }
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("衣装の UpperArm をまとめて設定", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Hierarchyで衣装の一番上を選択します（複数可）。非表示の衣装も対象です。\n" +
                "X・Y・ZのScaleを同じ値にします。チェックしたボーンだけが変更されます。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("選択中の衣装を読み込む"))
                    LoadSelection();

                EditorGUI.BeginChangeCheck();
                scale = EditorGUILayout.FloatField("Scale（X・Y・Z共通）", scale);
                if (EditorGUI.EndChangeCheck() && hasSavedScale)
                    RememberScale();

                if (!hasSavedScale)
                {
                    EditorGUILayout.HelpBox(
                        "初回は使用するScaleを入力して「この値を保存」を押してください。次回から前回の値を表示します。",
                        MessageType.Info);
                    using (new EditorGUI.DisabledScope(!IsValidScale(scale)))
                    {
                        if (GUILayout.Button("この値を保存"))
                        {
                            RememberScale();
                            GUI.FocusControl(null);
                        }
                    }
                }
                else
                    EditorGUILayout.LabelField("設定済み：値を変更すると、次回用の値も更新されます。",
                        EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);

                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawTargets();
                EditorGUILayout.EndScrollView();

                int count = targets.Count(t => t.Enabled && t.Bone != null && IsUsable(t));
                bool valid = IsValidScale(scale);
                using (new EditorGUI.DisabledScope(count == 0 || !valid || !hasSavedScale))
                {
                    if (GUILayout.Button("チェックした " + count + " 本に適用"))
                        Apply();
                }
            }

            EditorGUILayout.HelpBox("適用後はCtrl+Zで取り消せます。結果を残すにはシーンを保存してください。",
                MessageType.None);
        }

        private void DrawTargets()
        {
            if (wrappedButtonStyle == null)
            {
                wrappedButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 0,
                    padding = new RectOffset(6, 6, 4, 4)
                };
                wrappedHeadingStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontStyle = FontStyle.Bold
                };
            }

            var groups = targets.Where(t => t.Bone != null && t.Root != null)
                .GroupBy(t => t.Root).OrderBy(g => g.Key.name, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Allow long outfit names to wrap instead of widening the scroll view.
                    GUILayout.Label(group.Key.name, wrappedHeadingStyle,
                        GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                    foreach (var target in group)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            target.Enabled = EditorGUILayout.Toggle(target.Enabled,
                                GUILayout.Width(20));
                            using (new EditorGUILayout.VerticalScope())
                            {
                                var label = new GUIContent(CompactPath(target), target.Path);
                                if (GUILayout.Button(label, wrappedButtonStyle,
                                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
                                    EditorGUIUtility.PingObject(target.Bone.gameObject);
                                GUILayout.Label("現在のScale: " + target.Bone.localScale.ToString("F3"),
                                    EditorStyles.wordWrappedMiniLabel,
                                    GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                            }
                        }
                    }
                }
                GUILayout.Space(4);
            }
        }

        private static string CompactPath(Target target)
        {
            var parts = new List<string>();
            bool foundChest = false;
            for (var node = target.Bone; node != null && node != target.Root.transform; node = node.parent)
            {
                // Keep the exact child names, including outfit-specific suffixes.
                string name = node.name;
                int colon = name.LastIndexOf(':');
                if (colon >= 0)
                    name = name.Substring(colon + 1);
                if (Regex.IsMatch(name, @"^(?:upper[_.\s-]*)?chest(?:$|[^a-z])", RegexOptions.IgnoreCase))
                {
                    foundChest = true;
                    break;
                }
                parts.Add(node.name);
            }
            if (!foundChest)
                return target.Bone.name;
            parts.Reverse();
            return string.Join("\n", parts);
        }

        private static bool IsValidScale(float value)
        {
            return value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void RememberScale()
        {
            if (IsValidScale(scale))
            {
                EditorPrefs.SetFloat(preferenceKey, scale);
                hasSavedScale = true;
            }
        }

        private void LoadSelection()
        {
            targets.Clear();
            var seen = new HashSet<Transform>();
            int skipped = 0;
            foreach (var root in Selection.gameObjects)
            {
                // Work only on scene objects. Do not write directly to imported prefab assets.
                if (root == null || EditorUtility.IsPersistent(root) || !root.scene.IsValid() ||
                    HasAvatarDescriptor(root))
                {
                    skipped++;
                    continue;
                }

                foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!seen.Add(bone) || !IsCandidateName(bone.name) ||
                        HasNestedAvatar(bone, root.transform))
                        continue;
                    targets.Add(new Target
                    {
                        Bone = bone,
                        Root = root,
                        Enabled = IsStandardName(bone.name),
                        Path = root.name + "/" + RelativePath(bone, root.transform)
                    });
                }
            }
            targets.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));
            message = "候補 " + targets.Count + " 本。名前に衣装固有の文字が付く候補はチェックを外しています。";
            if (skipped > 0)
                message += " アバター本体・Project内の素材など " + skipped + " 件を除外しました。";
            if (targets.Count == 0)
                message += " 衣装をHierarchyに配置し、衣装の一番上を選び直してください。";
        }

        private static string NormalizeName(string name)
        {
            int colon = name.LastIndexOf(':');
            if (colon >= 0)
                name = name.Substring(colon + 1);
            return Regex.Replace(name, @"[\s_.\-]", "").ToLowerInvariant();
        }

        private static bool IsCandidateName(string name)
        {
            string n = NormalizeName(name);
            return n.Contains("upperarm") &&
                !n.Contains("support") && !n.Contains("twist") && !n.Contains("helper") &&
                !n.Contains("pbc") && !n.Contains("collider") && !n.EndsWith("end", StringComparison.Ordinal);
        }

        private static bool IsStandardName(string name)
        {
            return Regex.IsMatch(NormalizeName(name),
                @"^(?:(?:left|right|l|r)?upperarm|upperarm(?:left|right|l|r))$");
        }

        private static bool HasAvatarDescriptor(GameObject obj)
        {
            // No dependency on the VRChat SDK assembly.
            return obj.GetComponents<Component>().Any(c => c != null &&
                c.GetType().Name == "VRCAvatarDescriptor");
        }

        private static bool HasNestedAvatar(Transform bone, Transform root)
        {
            for (var node = bone; node != null; node = node.parent)
            {
                if (HasAvatarDescriptor(node.gameObject))
                    return true;
                if (node == root)
                    return false;
            }
            return true;
        }

        private static string RelativePath(Transform bone, Transform root)
        {
            var parts = new List<string>();
            for (var node = bone; node != null && node != root; node = node.parent)
                parts.Add(node.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool IsUsable(Target target)
        {
            return target.Bone != null && target.Root != null &&
                !EditorUtility.IsPersistent(target.Bone) && target.Bone.gameObject.scene.IsValid() &&
                (target.Bone == target.Root.transform || target.Bone.IsChildOf(target.Root.transform)) &&
                !HasNestedAvatar(target.Bone, target.Root.transform);
        }

        private void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !IsValidScale(scale) || !hasSavedScale)
                return;

            Vector3 value = Vector3.one * scale;
            var bones = targets.Where(t => t.Enabled && IsUsable(t)).Select(t => t.Bone)
                .Distinct().Where(t => t.localScale != value).ToArray();
            if (bones.Length == 0)
            {
                message = "対象はすでに指定したScaleです。";
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("衣装 UpperArm Scale");
            Undo.RecordObjects(bones.Cast<UnityEngine.Object>().ToArray(), "衣装 UpperArm Scale");
            foreach (var bone in bones)
            {
                bone.localScale = value;
                PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                EditorUtility.SetDirty(bone);
                EditorSceneManager.MarkSceneDirty(bone.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);
            message = bones.Length + " 本をScale " + scale.ToString("0.###") + " にしました。";
            Repaint();
        }
    }
}
#endif
