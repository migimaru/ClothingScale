#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public enum ScaleTargetKind
    {
        Bone,
        OutfitRoot
    }

    [Serializable]
    public sealed class BoneReference
    {
        public string relativePath;
        public string humanoidBone;
        public string pathFromArmature;
        public bool isArmatureRoot;
    }

    [Serializable]
    public sealed class ScaleSetting
    {
        public string id = Guid.NewGuid().ToString("N");
        public string displayName;
        public ScaleTargetKind targetKind;
        public Vector3 scale = Vector3.one;
        public bool linkAxes = true;
        public bool useBothSides;
        public BoneReference primaryBone;
        public BoneReference oppositeBone;
    }

    [Serializable]
    public sealed class ScaleProfile
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "新しいプロファイル";
        // Retained to read older profiles; these fields no longer control matching.
        public string avatarGlobalId;
        public string avatarScenePath;
        public string avatarHierarchyPath;
        public List<ScaleSetting> settings = new List<ScaleSetting>();
    }

    [Serializable]
    public sealed class ScaleProfileLibrary
    {
        public int schemaVersion = 1;
        public string selectedProfileId;
        public List<ScaleProfile> profiles = new List<ScaleProfile>();
    }
}
#endif
