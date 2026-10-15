#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    // Independent matching: Unity's humanoid roles first, then names and complete bone paths.
    // MA's matching code, alias tables and APIs are not used here.
    public sealed class BoneDefinitionResolver
    {
        private readonly GameObject outfit;
        private Transform armature;
        private string prefix = "";
        private string suffix = "";
        public string Issue { get; private set; }

        public BoneDefinitionResolver(GameObject outfit, IEnumerable<BoneReference> definitions)
        {
            this.outfit = outfit;
            FindArmature(definitions.Where(definition => definition != null).ToArray());
        }

        public static GameObject FindAvatarInParents(Transform start)
        {
            for (var current = start; current != null; current = current.parent)
            {
                if (OutfitScaleService.IsAvatarRoot(current.gameObject)) return current.gameObject;
            }
            return null;
        }

        // Keep numbers: Finger1 and Finger2 must not silently become the same custom bone.
        private static string Normalize(string name)
        {
            return new string((name ?? "").Where(character => character != '.' && character != '_' &&
                character != '-' && !char.IsWhiteSpace(character)).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool TryRole(BoneReference definition, out HumanBodyBones role)
        {
            return Enum.TryParse(definition.humanoidBone, out role) && Enum.IsDefined(typeof(HumanBodyBones), role) &&
                role != HumanBodyBones.LastBone;
        }

        private static bool IsRoleName(string name, HumanBodyBones role)
        {
            string actual = Normalize(name);
            string expected = Normalize(role.ToString());
            if (actual == expected) return true;
            if (role == HumanBodyBones.Hips) return actual == "hip" || actual == "pelvis";
            if (expected.StartsWith("left") || expected.StartsWith("right"))
            {
                bool left = expected.StartsWith("left");
                string side = left ? "left" : "right";
                string letter = left ? "l" : "r";
                string part = expected.Substring(side.Length);
                return actual == part + side || actual == part + letter || actual == letter + part;
            }
            return false;
        }

        private static string PathFor(BoneReference definition)
        {
            if (definition.isArmatureRoot) return "";
            if (!string.IsNullOrEmpty(definition.pathFromArmature)) return definition.pathFromArmature;
            // Older profiles start with the avatar's armature name; it is not an identity.
            string[] segments = (definition.relativePath ?? "").Split('/');
            int hips = Array.FindIndex(segments, segment => IsRoleName(segment, HumanBodyBones.Hips));
            if (hips >= 0) return string.Join("/", segments.Skip(hips));
            return segments.Length > 1 ? string.Join("/", segments.Skip(1)) : "";
        }

        public static bool SameDefinition(BoneReference first, BoneReference second)
        {
            if (first == null || second == null) return false;
            if (TryRole(first, out var firstRole) && TryRole(second, out var secondRole)) return firstRole == secondRole;
            return NormalizePath(PathFor(first)) == NormalizePath(PathFor(second));
        }

        private static string NormalizePath(string path)
        {
            return string.Join("/", path.Split('/').Select(Normalize));
        }

        public List<Transform> Find(BoneReference definition)
        {
            if (armature == null || definition == null) return new List<Transform>();
            if (TryRole(definition, out var role))
            {
                // The selected outfit's CURRENT parent supplies an optional role-to-path hint.
                // Nothing is resolved through the profile's original avatar or prefab.
                var avatar = FindAvatarInParents(outfit.transform.parent);
                var animator = avatar == null ? null : avatar.GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    var bodyBone = animator.GetBoneTransform(role);
                    var bodyRig = AvatarBoneCatalog.FindArmature(avatar);
                    if (bodyBone != null && bodyRig != null && bodyBone.IsChildOf(bodyRig))
                    {
                        string currentBodyPath = AvatarBoneCatalog.HierarchyPath(bodyBone, bodyRig);
                        var pathMatches = MatchPath(currentBodyPath);
                        if (pathMatches.Count > 0) return pathMatches;
                    }
                }
                // Standard role names allow the same setting across different humanoid hierarchies.
                return armature.GetComponentsInChildren<Transform>(true).Where(bone => bone != armature &&
                    IsRoleName(WithoutAffixes(bone.name), role)).ToList();
            }
            return MatchPath(PathFor(definition));
        }

        private List<Transform> MatchPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return new List<Transform> { armature };
            var level = new List<Transform> { armature };
            foreach (string segment in path.Split('/'))
            {
                // Inspect ALL matching children, never Transform.Find's first duplicate.
                level = level.SelectMany(parent => parent.Cast<Transform>()).Where(child =>
                    SameBoneName(WithoutAffixes(child.name), segment)).ToList();
                if (level.Count != 1) break;
            }
            return level;
        }

        private static bool SameBoneName(string first, string second)
        {
            if (Normalize(first) == Normalize(second)) return true;
            // Hips/Hip/Pelvis are structural names, not a copied alias dictionary.
            return IsRoleName(first, HumanBodyBones.Hips) && IsRoleName(second, HumanBodyBones.Hips);
        }

        private string WithoutAffixes(string name)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal) ||
                name.Length < prefix.Length + suffix.Length) return name;
            return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        }

        private void FindArmature(BoneReference[] definitions)
        {
            var shallow = new List<Transform>();
            var level = outfit.transform.Cast<Transform>().ToArray();
            for (int depth = 1; depth <= 3; depth++)
            {
                shallow.AddRange(level);
                level = level.SelectMany(child => child.Cast<Transform>()).ToArray();
            }
            var rigs = new List<Tuple<Transform, string, string>>();
            foreach (var candidate in shallow)
            {
                if (TryHipsAffixes(candidate, out string candidatePrefix, out string candidateSuffix))
                {
                    rigs.Add(Tuple.Create(candidate.parent, candidatePrefix, candidateSuffix));
                }
            }
            if (rigs.Count == 1)
            {
                armature = rigs[0].Item1;
                prefix = rigs[0].Item2;
                suffix = rigs[0].Item3;
                return;
            }
            if (rigs.Count > 1)
            {
                Issue = "衣装のアーマチュア候補が複数あり、判別できません。";
                return;
            }
            // Some accessories contain only upper-body bones, without Hips.
            // Require a complete saved path beginning directly beneath this container.
            var paths = definitions.Select(PathFor).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            var matchingContainers = new List<Transform>();
            prefix = suffix = "";
            foreach (var candidate in shallow.Prepend(outfit.transform))
            {
                armature = candidate;
                if (paths.Any(path => MatchPath(path).Count > 0)) matchingContainers.Add(candidate);
            }
            armature = matchingContainers.Count == 1 ? matchingContainers[0] : null;
            if (armature == null)
            {
                Issue = matchingContainers.Count > 1 ? "衣装のアーマチュア候補が複数あり、判別できません。" :
                    "衣装のアーマチュアを特定できません。Hipsとボーンの階層を確認してください。";
            }
        }

        private static bool TryHipsAffixes(Transform candidate, out string prefix, out string suffix)
        {
            prefix = suffix = "";
            foreach (string name in new[] { "Hips", "Pelvis", "Hip" })
            {
                int index = candidate.name.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                string before = candidate.name.Substring(0, index);
                string after = candidate.name.Substring(index + name.Length);
                bool skeletonChild = candidate.Cast<Transform>().Any(child =>
                {
                    if (!child.name.StartsWith(before, StringComparison.Ordinal) || !child.name.EndsWith(after, StringComparison.Ordinal) ||
                        child.name.Length <= before.Length + after.Length) return false;
                    string core = child.name.Substring(before.Length, child.name.Length - before.Length - after.Length);
                    return IsRoleName(core, HumanBodyBones.Spine) || IsRoleName(core, HumanBodyBones.LeftUpperLeg) ||
                        IsRoleName(core, HumanBodyBones.RightUpperLeg);
                });
                if (!skeletonChild) continue;
                prefix = before; suffix = after;
                return true;
            }
            return false;
        }
    }
}
#endif
