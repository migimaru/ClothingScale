#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    internal static class PairedScaleSettings
    {
        internal static bool HasOpposite(ScaleSetting setting)
        {
            // Unity JSON can deserialize an absent inline class as an empty object.
            // An allocated BoneReference without a path is not a counterpart.
            return setting != null && setting.targetKind == ScaleTargetKind.Bone &&
                !string.IsNullOrEmpty(setting.primaryBone?.relativePath) &&
                !string.IsNullOrEmpty(setting.oppositeBone?.relativePath);
        }

        internal static string BoneName(BoneReference bone)
        {
            return !string.IsNullOrEmpty(bone?.humanoidBone) ? bone.humanoidBone :
                (bone?.relativePath ?? "").Split('/').Last();
        }

        internal static bool IsRight(BoneReference bone)
        {
            string name = BoneName(bone);
            return Regex.IsMatch(name, @"^Right(?=[A-Z])|Right$|(^|[_.\s-])(right|r)($|[_.\s-])",
                RegexOptions.IgnoreCase);
        }

        internal static ScaleSetting Opposite(ScaleProfile profile, ScaleSetting setting)
        {
            if (!HasOpposite(setting)) return null;
            return profile.settings.FirstOrDefault(other => other != setting &&
                other.targetKind == ScaleTargetKind.Bone &&
                BoneDefinitionResolver.SameDefinition(other.primaryBone, setting.oppositeBone));
        }

        internal static void Split(ScaleProfile profile, ScaleSetting setting)
        {
            if (!HasOpposite(setting)) return;
            string pairName = string.IsNullOrEmpty(setting.pairDisplayName) ? setting.displayName : setting.pairDisplayName;
            if (string.IsNullOrEmpty(pairName)) pairName = BoneName(setting.primaryBone);
            var other = Opposite(profile, setting);
            if (other == null)
            {
                other = new ScaleSetting
                {
                    targetKind = ScaleTargetKind.Bone,
                    primaryBone = setting.oppositeBone,
                    oppositeBone = setting.primaryBone,
                    pairDisplayName = pairName,
                    scale = setting.scale,
                    linkAxes = setting.linkAxes
                };
                profile.settings.Insert(profile.settings.IndexOf(setting) + 1, other);
            }
            setting.pairDisplayName = other.pairDisplayName = pairName;
            setting.useBothSides = other.useBothSides = false;
            setting.displayName = BoneName(setting.primaryBone);
            other.displayName = BoneName(other.primaryBone);
            // Keep the two rows adjacent, L before R even when the picker encountered R first.
            int index = profile.settings.IndexOf(setting);
            int otherIndex = profile.settings.IndexOf(other);
            if (IsRight(setting.primaryBone) && otherIndex == index + 1)
            {
                profile.settings[index] = other;
                profile.settings[otherIndex] = setting;
            }
        }

        internal static void Merge(ScaleProfile profile, ScaleSetting setting, Vector3 scale)
        {
            if (!HasOpposite(setting) || !TransformScaleUtility.IsValidScale(scale))
                throw new InvalidOperationException("左右セットのボーンまたはScaleが無効です。");
            var other = Opposite(profile, setting);
            if (other != null) profile.settings.Remove(other);
            if (IsRight(setting.primaryBone))
            {
                var bone = setting.primaryBone;
                setting.primaryBone = setting.oppositeBone;
                setting.oppositeBone = bone;
            }
            if (!string.IsNullOrEmpty(setting.pairDisplayName)) setting.displayName = setting.pairDisplayName;
            setting.scale = scale;
            setting.useBothSides = true;
        }

        internal static void ExpandLegacySingles(ScaleProfileLibrary library)
        {
            foreach (var profile in library.profiles)
                foreach (var setting in profile.settings.ToArray())
                    if (!setting.useBothSides && HasOpposite(setting) &&
                        string.IsNullOrEmpty(setting.pairDisplayName))
                        Split(profile, setting);
        }
    }
}
#endif
