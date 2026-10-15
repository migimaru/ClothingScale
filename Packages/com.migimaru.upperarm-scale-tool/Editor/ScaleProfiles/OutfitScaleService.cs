#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public sealed class ResolvedScaleSetting
    {
        public ScaleSetting setting;
        public List<Transform> targets = new List<Transform>();
        public string issue;
        public bool CanApply => string.IsNullOrEmpty(issue) && targets.Count > 0;
    }

    public sealed class OutfitScaleTargets
    {
        public GameObject outfit;
        public List<ResolvedScaleSetting> settings = new List<ResolvedScaleSetting>();
    }

    public sealed class ScaleAssignment
    {
        public Transform target;
        public Vector3 value;
    }

    public static class OutfitScaleService
    {
        public static bool IsAvatarRoot(GameObject gameObject)
        {
            if (gameObject.GetComponents<Component>().Any(component => component != null &&
                (component.GetType().Name == "VRCAvatarDescriptor" || component.GetType().Name == "NDMFAvatarRoot")))
            {
                return true;
            }
            var animator = gameObject.GetComponent<Animator>();
            return animator != null && animator.isHuman;
        }

        public static List<GameObject> FilterOutfitRoots(IEnumerable<GameObject> selection, ScaleProfile profile)
        {
            var roots = selection.Where(root => root != null && TransformScaleUtility.IsEditable(root.transform) &&
                !root.GetComponentsInChildren<Transform>(true).Any(bone => IsAvatarRoot(bone.gameObject))).Distinct().ToList();
            roots.RemoveAll(root =>
            {
                var avatar = BoneDefinitionResolver.FindAvatarInParents(root.transform.parent);
                var bodyRig = avatar == null ? null : AvatarBoneCatalog.FindArmature(avatar);
                return bodyRig != null && (root.transform == bodyRig || root.transform.IsChildOf(bodyRig));
            });
            // An outer outfit selection already includes its children; do not add a second root scale.
            return roots.Where(root => !roots.Any(other => other != root && root.transform.IsChildOf(other.transform)))
                .ToList();
        }

        public static List<OutfitScaleTargets> Resolve(ScaleProfile profile, IEnumerable<GameObject> outfits)
        {
            var groups = new List<OutfitScaleTargets>();
            foreach (var outfit in FilterOutfitRoots(outfits, profile))
            {
                if (outfit == null || !TransformScaleUtility.IsEditable(outfit.transform))
                {
                    continue;
                }
                var group = new OutfitScaleTargets { outfit = outfit };
                var definitions = profile.settings.Where(setting => setting.targetKind == ScaleTargetKind.Bone)
                    .SelectMany(setting => new[] { setting.primaryBone, setting.oppositeBone });
                var bones = new BoneDefinitionResolver(outfit, definitions);
                foreach (var setting in profile.settings)
                {
                    var resolved = new ResolvedScaleSetting { setting = setting };
                    group.settings.Add(resolved);
                    if (setting.targetKind == ScaleTargetKind.OutfitRoot)
                    {
                        if (ModularAvatarBridge.HasBidirectionalLockInChildren(outfit) ||
                            ModularAvatarBridge.HasBidirectionalLockInParents(outfit.transform))
                        {
                            resolved.issue = "MAの双方向固定が有効です。素体への同期を避けるため、一方向固定にしてください。";
                        }
                        else
                        {
                            resolved.targets.Add(outfit.transform);
                        }
                        continue;
                    }
                    var primaryMatches = bones.Find(setting.primaryBone);
                    var oppositeMatches = setting.useBothSides ? bones.Find(setting.oppositeBone) : new List<Transform>();
                    if (primaryMatches.Count != 1 || (setting.useBothSides && oppositeMatches.Count != 1))
                    {
                        bool ambiguous = primaryMatches.Count > 1 || oppositeMatches.Count > 1;
                        resolved.issue = ambiguous ? "対応する衣装ボーンが複数あり、判別できません。" :
                            "対応する衣装ボーンが見つかりません。";
                        if (!string.IsNullOrEmpty(bones.Issue)) resolved.issue += " " + bones.Issue;
                        continue;
                    }
                    resolved.targets.Add(primaryMatches[0]);
                    if (setting.useBothSides)
                    {
                        resolved.targets.Add(oppositeMatches[0]);
                    }
                    if (resolved.targets.Any(target => ModularAvatarBridge.HasBidirectionalLockInParents(target, outfit.transform)))
                    {
                        resolved.targets.Clear();
                        resolved.issue = "MAの双方向固定が有効です。素体への同期を避けるため、衣装側を一方向固定にしてください。";
                    }
                }
                groups.Add(group);
            }
            return groups;
        }

        public static int Apply(IEnumerable<ScaleAssignment> assignments)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("再生中は適用できません。");
            }
            var unique = new Dictionary<Transform, Vector3>();
            foreach (var assignment in assignments)
            {
                if (!TransformScaleUtility.IsEditable(assignment.target) ||
                    !TransformScaleUtility.IsValidScale(assignment.value))
                {
                    throw new InvalidOperationException("対象またはScaleが無効です。何も変更していません。");
                }
                if (unique.TryGetValue(assignment.target, out Vector3 previous) && !previous.Equals(assignment.value))
                {
                    throw new InvalidOperationException("同じボーンに異なる設定があります。何も変更していません。");
                }
                unique[assignment.target] = assignment.value;
            }
            var changes = unique.Where(pair => !pair.Key.localScale.Equals(pair.Value))
                .OrderBy(pair => GetDepth(pair.Key)).ToArray();
            if (changes.Length == 0)
            {
                return 0;
            }

            const string operationName = "衣装のScaleをまとめて設定";
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(operationName);
            Undo.RecordObjects(changes.Select(pair => (UnityEngine.Object)pair.Key).ToArray(), operationName);
            try
            {
                foreach (var change in changes)
                {
                    change.Key.localScale = change.Value;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(change.Key);
                    EditorUtility.SetDirty(change.Key);
                    EditorSceneManager.MarkSceneDirty(change.Key.gameObject.scene);
                }
                Undo.CollapseUndoOperations(undoGroup);
                return changes.Length;
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            for (var current = transform; current != null; current = current.parent)
            {
                depth++;
            }
            return depth;
        }
    }
}
#endif
