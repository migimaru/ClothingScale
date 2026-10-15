#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PersonalAvatarTools.ScaleProfiles
{
    public sealed class AvatarBoneChoice
    {
        public Transform primary;
        public Transform opposite;
        public BoneReference primaryReference;
        public BoneReference oppositeReference;
        public string displayName;
    }

    public static class AvatarBoneCatalog
    {
        private static readonly Dictionary<string, GameObject> avatarCache = new Dictionary<string, GameObject>();
        public static string HierarchyPath(Transform transform, Transform stopAt = null)
        {
            var segments = new List<string>();
            for (var current = transform; current != null && current != stopAt; current = current.parent)
            {
                segments.Add(current.name);
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        public static void BindAvatar(ScaleProfile profile, GameObject avatar)
        {
            profile.avatarGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString();
            profile.avatarScenePath = avatar.scene.path;
            profile.avatarHierarchyPath = HierarchyPath(avatar.transform);
            avatarCache[BindingKey(profile)] = avatar;
        }

        private static string BindingKey(ScaleProfile profile)
        {
            return profile.id + "|" + profile.avatarGlobalId + "|" + profile.avatarScenePath + "|" + profile.avatarHierarchyPath;
        }

        public static GameObject ResolveAvatar(ScaleProfile profile)
        {
            if (avatarCache.TryGetValue(BindingKey(profile), out var cached) && cached != null &&
                TransformScaleUtility.IsEditable(cached.transform))
            {
                return cached;
            }
            if (!string.IsNullOrEmpty(profile.avatarGlobalId) && GlobalObjectId.TryParse(profile.avatarGlobalId, out var globalId))
            {
                var avatar = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as GameObject;
                if (avatar != null && TransformScaleUtility.IsEditable(avatar.transform))
                {
                    avatarCache[BindingKey(profile)] = avatar;
                    return avatar;
                }
            }

            var matches = new List<GameObject>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded || (!string.IsNullOrEmpty(profile.avatarScenePath) && scene.path != profile.avatarScenePath))
                {
                    continue;
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (TransformScaleUtility.IsEditable(transform) && HierarchyPath(transform) == profile.avatarHierarchyPath)
                        {
                            matches.Add(transform.gameObject);
                        }
                    }
                }
            }
            if (matches.Count != 1)
            {
                return null;
            }
            avatarCache[BindingKey(profile)] = matches[0];
            return matches[0];
        }

        public static Transform ResolveBone(GameObject avatar, BoneReference reference)
        {
            if (avatar == null || reference == null || string.IsNullOrEmpty(reference.relativePath))
            {
                return null;
            }

            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.isHuman &&
                Enum.TryParse(reference.humanoidBone, out HumanBodyBones humanoidBone) &&
                Enum.IsDefined(typeof(HumanBodyBones), humanoidBone) && humanoidBone != HumanBodyBones.LastBone)
            {
                return animator.GetBoneTransform(humanoidBone);
            }

            // Walk each segment explicitly: duplicate names must not silently resolve to the first child.
            var current = avatar.transform;
            foreach (string segment in reference.relativePath.Split('/'))
            {
                var matches = current.Cast<Transform>().Where(child => child.name == segment).ToArray();
                if (matches.Length != 1)
                {
                    return null;
                }
                current = matches[0];
            }
            return current;
        }

        public static List<AvatarBoneChoice> CreateChoices(GameObject avatar)
        {
            var humanoidBones = ReadHumanoidBones(avatar);
            Transform armature = FindArmature(avatar);
            if (armature == null)
            {
                return new List<AvatarBoneChoice>();
            }
            var bones = armature.GetComponentsInChildren<Transform>(true)
                .Where(bone => bone != avatar.transform && !IsInsideOutfit(bone, avatar.transform))
                .ToArray();
            var choices = new List<AvatarBoneChoice>();
            var included = new HashSet<Transform>();

            foreach (var bone in bones)
            {
                if (!included.Add(bone))
                {
                    continue;
                }
                Transform opposite = FindOpposite(bone, bones, humanoidBones, avatar.transform);
                if (opposite != null)
                {
                    included.Add(opposite);
                }
                humanoidBones.TryGetValue(bone, out string humanoidName);
                string name = string.IsNullOrEmpty(humanoidName) ? bone.name : humanoidName;
                if (opposite != null)
                {
                    name = name.Replace("Left", "").Replace("Right", "");
                }
                choices.Add(new AvatarBoneChoice
                {
                    primary = bone,
                    opposite = opposite,
                    displayName = name,
                    primaryReference = ReferenceFor(bone, avatar.transform, humanoidBones),
                    oppositeReference = opposite == null ? null : ReferenceFor(opposite, avatar.transform, humanoidBones)
                });
            }
            return choices.OrderBy(choice => HierarchyPath(choice.primary, avatar.transform), StringComparer.Ordinal).ToList();
        }

        private static Dictionary<Transform, string> ReadHumanoidBones(GameObject avatar)
        {
            var result = new Dictionary<Transform, string>();
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                return result;
            }
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }
                var transform = animator.GetBoneTransform(bone);
                if (transform != null)
                {
                    result[transform] = bone.ToString();
                }
            }
            return result;
        }

        public static Transform FindHips(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                return animator.GetBoneTransform(HumanBodyBones.Hips);
            }

            // Search the body before deeper outfit roots. The container's name is irrelevant.
            var children = avatar.transform.Cast<Transform>().ToArray();
            var level = children.SelectMany(child => child.Cast<Transform>()).ToArray();
            for (int depth = 2; depth <= 3; depth++)
            {
                var hips = level.Where(bone => IsHipsName(bone.name) &&
                    !IsInsideOutfit(bone, avatar.transform)).ToArray();
                if (hips.Length > 0)
                {
                    return hips.Length == 1 ? hips[0] : null;
                }
                level = level.SelectMany(child => child.Cast<Transform>()).ToArray();
            }
            return null;
        }

        public static Transform FindArmature(GameObject avatar)
        {
            var hips = FindHips(avatar);
            if (hips != null && hips.parent != null)
            {
                return hips.parent;
            }

            // Keep rigs without Hips selectable, including Blender's duplicate suffixes.
            var roots = avatar.transform.Cast<Transform>().Where(child =>
                (child.name.Equals("Armature", StringComparison.OrdinalIgnoreCase) ||
                 child.name.StartsWith("Armature.", StringComparison.OrdinalIgnoreCase)) &&
                !IsInsideOutfit(child, avatar.transform)).ToArray();
            return roots.Length == 1 ? roots[0] : null;
        }

        private static bool IsHipsName(string name)
        {
            return name.Equals("Hips", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Hip", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Pelvis", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsideOutfit(Transform bone, Transform avatarRoot)
        {
            for (var current = bone; current != null && current != avatarRoot; current = current.parent)
            {
                if (ModularAvatarBridge.IsMergeArmature(current.gameObject))
                {
                    return true;
                }
            }
            return false;
        }

        private static BoneReference ReferenceFor(Transform bone, Transform avatar, Dictionary<Transform, string> humanoidBones)
        {
            humanoidBones.TryGetValue(bone, out string humanoidName);
            var armature = FindArmature(avatar.gameObject);
            return new BoneReference
            {
                relativePath = HierarchyPath(bone, avatar),
                humanoidBone = humanoidName,
                pathFromArmature = armature == null ? null : HierarchyPath(bone, armature),
                isArmatureRoot = bone == armature
            };
        }

        private static Transform FindOpposite(Transform bone, Transform[] bones,
            Dictionary<Transform, string> humanoidBones, Transform avatar)
        {
            if (humanoidBones.TryGetValue(bone, out string humanoidName))
            {
                string oppositeName = null;
                if (humanoidName.StartsWith("Left", StringComparison.Ordinal))
                {
                    oppositeName = "Right" + humanoidName.Substring(4);
                }
                else if (humanoidName.StartsWith("Right", StringComparison.Ordinal))
                {
                    oppositeName = "Left" + humanoidName.Substring(5);
                }
                if (oppositeName != null)
                {
                    return humanoidBones.FirstOrDefault(pair => pair.Value == oppositeName).Key;
                }
            }

            string path = HierarchyPath(bone, avatar);
            string mirroredPath = MirrorSideNames(path);
            if (mirroredPath == path)
            {
                return null;
            }
            var mirrored = new BoneReference { relativePath = mirroredPath };
            var matches = bones.Where(candidate => BoneDefinitionResolver.SameDefinition(mirrored,
                new BoneReference { relativePath = HierarchyPath(candidate, avatar) })).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static string MirrorSideNames(string path)
        {
            return string.Join("/", path.Split('/').Select(MirrorSideSegment));
        }

        private static string MirrorSideSegment(string segment)
        {
            string mirrored = Regex.Replace(segment, @"^(Left|Right)(?=[A-Z])|(Left|Right)$",
                match => OppositeSideWord(match.Value), RegexOptions.IgnoreCase);
            if (mirrored != segment)
            {
                return mirrored;
            }
            return Regex.Replace(segment, @"(^|[_.\s-])(left|right|l|r)(?=$|[_.\s-])",
                match => match.Groups[1].Value + OppositeSideWord(match.Groups[2].Value), RegexOptions.IgnoreCase);
        }

        private static string OppositeSideWord(string side)
        {
            string opposite;
            switch (side.ToLowerInvariant())
            {
                case "left": opposite = "right"; break;
                case "right": opposite = "left"; break;
                case "l": opposite = "r"; break;
                case "r": opposite = "l"; break;
                default: return side;
            }
            if (side == side.ToUpperInvariant())
            {
                return opposite.ToUpperInvariant();
            }
            if (char.IsUpper(side[0]))
            {
                return char.ToUpperInvariant(opposite[0]) + opposite.Substring(1);
            }
            return opposite;
        }
    }
}
#endif
