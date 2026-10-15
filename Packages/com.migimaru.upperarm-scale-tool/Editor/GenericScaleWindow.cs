#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PersonalAvatarTools
{
    public sealed class GenericScaleWindow : EditorWindow
    {
        [Serializable]
        private sealed class Settings
        {
            public Vector3 value = Vector3.one;
            public bool uniform = true;
            public ScaleOperation operation = ScaleOperation.Set;
        }

        private sealed class Target
        {
            public Transform transform;
            public Transform root;
            public bool enabled;
            public bool IsUsable => TransformScaleUtility.IsEditable(transform) && root != null &&
                (transform == root || transform.IsChildOf(root));
        }

        private readonly List<Target> targets = new List<Target>();
        private Settings settings = new Settings();
        private string preferenceKey;
        private bool hasSavedSettings;
        private string query = "";
        private bool partialMatch;
        private Vector2 scroll;
        private GUIStyle pathStyle;
        private string message = "Hierarchyで対象そのもの、または検索する衣装の一番上を選択してください。";

        public static void Open()
        {
            GetWindow<GenericScaleWindow>("汎用 Scale").minSize = new Vector2(380, 520);
        }

        private void OnEnable()
        {
            preferenceKey = "PersonalAvatarTools.GenericScale.Settings." + Application.dataPath;
            hasSavedSettings = EditorPrefs.HasKey(preferenceKey);
            if (hasSavedSettings)
            {
                try { settings = JsonUtility.FromJson<Settings>(EditorPrefs.GetString(preferenceKey)); }
                catch (ArgumentException) { settings = null; }
            }
            if (settings == null || !TransformScaleUtility.IsValidScale(settings.value) ||
                !Enum.IsDefined(typeof(ScaleOperation), settings.operation) ||
                (settings.uniform && (settings.value.x != settings.value.y || settings.value.x != settings.value.z)))
            {
                settings = new Settings();
                hasSavedSettings = false;
            }
            Undo.undoRedoPerformed += Repaint;
            EditorApplication.hierarchyChanged += Repaint;
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            EditorApplication.hierarchyChanged -= Repaint;
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("ボーン・オブジェクトのScaleをまとめて変更", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("変更するのはローカルScaleです。親のScaleは子の見た目にも影響します。\n" +
                "アバター本体も対象にできます。適用前に対象と変更後の値を確認してください。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("選択したオブジェクト自体を読み込む")) LoadSelection(false);
                query = EditorGUILayout.TextField("子階層の名前を検索", query);
                partialMatch = EditorGUILayout.Toggle("部分一致", partialMatch);
                EditorGUILayout.LabelField("完全一致が標準です。複数名はカンマで区切ります（例: Hand_L, Hand_R）。",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(query)))
                {
                    if (GUILayout.Button("選択したルートの子階層から検索")) LoadSelection(true);
                }
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                settings.operation = (ScaleOperation)EditorGUILayout.Popup("適用方法", (int)settings.operation,
                    new[] { "指定した値に設定", "現在の値に倍率を掛ける" });
                bool uniform = EditorGUILayout.Toggle("XYZを同じ値にする", settings.uniform);
                if (uniform != settings.uniform)
                {
                    settings.uniform = uniform;
                    if (uniform) settings.value = Vector3.one * settings.value.x;
                }
                if (settings.uniform)
                    settings.value = Vector3.one * EditorGUILayout.FloatField(
                        settings.operation == ScaleOperation.Set ? "Scale" : "倍率", settings.value.x);
                else
                    settings.value = EditorGUILayout.Vector3Field(
                        settings.operation == ScaleOperation.Set ? "Scale (XYZ)" : "倍率 (XYZ)", settings.value);
                if (EditorGUI.EndChangeCheck() && hasSavedSettings) RememberSettings();
                bool valid = TransformScaleUtility.IsValidScale(settings.value);
                if (!valid)
                    EditorGUILayout.HelpBox("各軸には0より大きい有限の値を入力してください。", MessageType.Error);
                if (!hasSavedSettings)
                {
                    using (new EditorGUI.DisabledScope(!valid))
                    {
                        if (GUILayout.Button("この設定を保存"))
                        {
                            RememberSettings();
                            GUI.FocusControl(null);
                        }
                    }
                    EditorGUILayout.LabelField("初回は設定を保存してください。以降の変更はプロジェクトごとに保持します。",
                        EditorStyles.wordWrappedMiniLabel);
                }
                if (settings.operation == ScaleOperation.Multiply)
                    EditorGUILayout.HelpBox("倍率は適用するたびに掛かります。0.9を2回適用すると元の0.81倍になります。",
                        MessageType.Info);
                EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("すべてチェック")) SetChecks(true);
                    if (GUILayout.Button("チェックを外す")) SetChecks(false);
                }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawTargets();
                EditorGUILayout.EndScrollView();
                var enabled = targets.Where(t => t.enabled).ToArray();
                bool stale = enabled.Any(t => !t.IsUsable);
                bool invalidResult = !stale && enabled.Any(t =>
                    !TransformScaleUtility.IsValidScale(TransformScaleUtility.Result(t.transform.localScale,
                        settings.value, settings.operation)));
                if (stale)
                    EditorGUILayout.HelpBox("対象が削除されたか、読み込んだ階層から移動しています。読み込み直してください。",
                        MessageType.Warning);
                else if (invalidResult)
                    EditorGUILayout.HelpBox("変更後のScaleが無効になる対象があります。チェックまたは指定値を見直してください。",
                        MessageType.Error);
                if (enabled.Any(t => t.IsUsable && enabled.Any(other => other != t && other.IsUsable &&
                    t.transform.IsChildOf(other.transform))))
                    EditorGUILayout.HelpBox("親と子の両方がチェックされています。両方のScaleが見た目に重なって作用します。",
                        MessageType.Warning);
                using (new EditorGUI.DisabledScope(enabled.Length == 0 || stale || invalidResult || !valid || !hasSavedSettings))
                {
                    if (GUILayout.Button("チェックした " + enabled.Length + " 件に適用"))
                    {
                        try
                        {
                            int changed = TransformScaleUtility.Apply(enabled.Select(t => t.transform),
                                settings.value, settings.operation);
                            message = changed == 0 ? "対象はすでに指定したScaleです。" : changed + " 件のScaleを変更しました。";
                        }
                        catch (Exception ex) { message = ex.Message; Debug.LogException(ex); }
                        Repaint();
                    }
                }
            }
            EditorGUILayout.HelpBox("Ctrl+Zで一括取り消しできます。結果を残すにはシーンを保存してください。", MessageType.None);
        }

        private void RememberSettings()
        {
            if (!TransformScaleUtility.IsValidScale(settings.value)) return;
            EditorPrefs.SetString(preferenceKey, JsonUtility.ToJson(settings));
            hasSavedSettings = true;
        }

        private void SetChecks(bool enabled)
        {
            foreach (var target in targets) target.enabled = enabled && target.IsUsable;
        }

        private void LoadSelection(bool search)
        {
            targets.Clear();
            var roots = Selection.gameObjects.Where(root => root != null &&
                TransformScaleUtility.IsEditable(root.transform)).ToArray();
            var found = search ? TransformScaleUtility.FindTargets(roots, query, partialMatch) :
                roots.Select(root => root.transform).Distinct().ToArray();
            foreach (var transform in found)
            {
                // Prefer the nearest selected root when selected ranges overlap.
                var root = search ? roots.Select(r => r.transform)
                    .Where(r => transform != r && transform.IsChildOf(r))
                    .OrderByDescending(Depth).First() : transform;
                targets.Add(new Target { transform = transform, root = root, enabled = !search });
            }
            targets.Sort((a, b) => string.Compare(FullPath(a.transform), FullPath(b.transform), StringComparison.Ordinal));
            message = found.Length + " 件を読み込みました。" + (search ? "検索結果は未チェックです。変更する対象にチェックしてください。" : "");
            int skipped = Selection.gameObjects.Length - roots.Length;
            if (skipped > 0) message += " Project内の素材など " + skipped + " 件を除外しました。";
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;
            for (var node = transform; node != null; node = node.parent) depth++;
            return depth;
        }

        private static string FullPath(Transform transform)
        {
            var parts = new List<string>();
            for (var node = transform; node != null; node = node.parent) parts.Add(node.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private void DrawTargets()
        {
            if (pathStyle == null)
                pathStyle = new GUIStyle(EditorStyles.miniButton) {
                    wordWrap = true, alignment = TextAnchor.MiddleLeft, fixedHeight = 0,
                    padding = new RectOffset(6, 6, 4, 4)
                };
            foreach (var target in targets)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    target.enabled = EditorGUILayout.Toggle(target.enabled, GUILayout.Width(20));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        if (target.transform == null)
                        {
                            EditorGUILayout.LabelField("削除された対象", EditorStyles.wordWrappedLabel);
                            continue;
                        }
                        string path = FullPath(target.transform);
                        if (GUILayout.Button(new GUIContent(path.Replace("/", "/\n"), path), pathStyle,
                            GUILayout.MinWidth(0), GUILayout.ExpandWidth(true)))
                            EditorGUIUtility.PingObject(target.transform.gameObject);
                        Vector3 current = target.transform.localScale;
                        Vector3 next = TransformScaleUtility.Result(current, settings.value, settings.operation);
                        GUILayout.Label("現在 " + current.ToString("G5") + " → 変更後 " + next.ToString("G5"),
                            EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                    }
                }
            }
        }
    }
}
#endif
