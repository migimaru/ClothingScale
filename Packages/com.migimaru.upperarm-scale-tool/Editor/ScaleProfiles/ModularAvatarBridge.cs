#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    // Reads component configuration only. Bone matching does not depend on MA.
    public static class ModularAvatarBridge
    {
        private const string MergeTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature";

        private static Component Merger(GameObject gameObject)
        {
            return gameObject.GetComponents<Component>().FirstOrDefault(component =>
                component != null && component.GetType().FullName == MergeTypeName);
        }

        public static bool IsMergeArmature(GameObject gameObject) => Merger(gameObject) != null;

        public static bool HasMergeArmatureInHierarchy(GameObject outfit)
        {
            for (var parent = outfit.transform; parent != null; parent = parent.parent)
            {
                if (IsMergeArmature(parent.gameObject)) return true;
            }
            return outfit.GetComponentsInChildren<Transform>(true).Any(bone => IsMergeArmature(bone.gameObject));
        }

        public static bool HasBidirectionalLockInParents(Transform target, Transform stopAt = null)
        {
            for (var current = target; current != null; current = current.parent)
            {
                if (IsBidirectional(Merger(current.gameObject))) return true;
                if (current == stopAt) break;
            }
            return false;
        }

        public static bool HasBidirectionalLockInChildren(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true).Any(bone => IsBidirectional(Merger(bone.gameObject)));
        }

        private static bool IsBidirectional(Component component)
        {
            if (component == null || (component is Behaviour behaviour && !behaviour.enabled)) return false;
            // Legacy serialized profiles use a bool instead of an explicit lock mode.
            var mode = component.GetType().GetField("LockMode")?.GetValue(component)?.ToString();
            if (mode == "BidirectionalExact") return true;
            return mode == "Legacy" && component.GetType().GetField("legacyLocked")?.GetValue(component) is bool locked && locked;
        }
    }
}
#endif
