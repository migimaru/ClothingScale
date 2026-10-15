#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PersonalAvatarTools
{
    public enum ScaleOperation { Set, Multiply }

    public static class TransformScaleUtility
    {
        public static bool IsValidScale(Vector3 value)
        {
            return IsPositiveFinite(value.x) && IsPositiveFinite(value.y) && IsPositiveFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static Vector3 Result(Vector3 current, Vector3 value, ScaleOperation operation)
        {
            switch (operation)
            {
                case ScaleOperation.Set: return value;
                case ScaleOperation.Multiply: return Vector3.Scale(current, value);
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        public static bool IsEditable(Transform target)
        {
            return target != null && !EditorUtility.IsPersistent(target) &&
                target.gameObject.scene.IsValid() && target.gameObject.scene.isLoaded &&
                !EditorSceneManager.IsPreviewScene(target.gameObject.scene);
        }

        public static bool MatchesName(string name, string query, bool partial)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;
            // Ignore namespace prefixes and case, but preserve outfit-specific suffixes.
            string candidate = name.Substring(name.LastIndexOf(':') + 1);
            foreach (string part in query.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string pattern = part.Trim();
                pattern = pattern.Substring(pattern.LastIndexOf(':') + 1);
                if (pattern.Length == 0) continue;
                if (partial ? candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0
                            : string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static Transform[] FindTargets(IEnumerable<GameObject> roots, string query, bool partial)
        {
            // Search descendants only; do not implicitly scale the outfit root.
            return roots.Where(root => root != null && IsEditable(root.transform))
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != root.transform && IsEditable(t) && MatchesName(t.name, query, partial)))
                .Distinct().ToArray();
        }

        public static int Apply(IEnumerable<Transform> targets, Vector3 value, ScaleOperation operation)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("再生中は変更できません。");
            if (!IsValidScale(value))
                throw new ArgumentException("Scaleの各軸には0より大きい有限の値を指定してください。", nameof(value));
            var unique = targets.Distinct().ToArray();
            // Validate the entire batch before recording Undo or changing objects.
            if (unique.Any(t => !IsEditable(t)))
                throw new InvalidOperationException("対象が削除されたか、シーン上の対象ではありません。読み込み直してください。");
            var results = unique.Select(t => Result(t.localScale, value, operation)).ToArray();
            if (results.Any(v => !IsValidScale(v)))
                throw new InvalidOperationException("適用後のScaleが無効になります。現在値と指定値を確認してください。");
            var changed = unique.Where((t, i) => !t.localScale.Equals(results[i])).ToArray();
            if (changed.Length == 0) return 0;

            const string label = "汎用 Scale";
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            Undo.RecordObjects(changed.Cast<UnityEngine.Object>().ToArray(), label);
            // Results use original local values, even when both a parent and child are selected.
            for (int i = 0; i < unique.Length; i++)
            {
                if (unique[i].localScale.Equals(results[i])) continue;
                unique[i].localScale = results[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(unique[i]);
                EditorUtility.SetDirty(unique[i]);
                EditorSceneManager.MarkSceneDirty(unique[i].gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);
            return changed.Length;
        }
    }
}
#endif
