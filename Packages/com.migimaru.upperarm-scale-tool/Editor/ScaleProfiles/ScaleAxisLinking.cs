#if UNITY_EDITOR
using UnityEngine;

namespace PersonalAvatarTools.ScaleProfiles
{
    public static class ScaleAxisLinking
    {
        public static Vector3 Update(Vector3 previous, Vector3 edited, bool linked)
        {
            if (!linked || !TransformScaleUtility.IsValidScale(previous))
            {
                return edited;
            }
            for (int axis = 0; axis < 3; axis++)
            {
                if (previous[axis].Equals(edited[axis]))
                {
                    continue;
                }
                if (previous[axis] <= 0f)
                {
                    return edited;
                }
                float ratio = edited[axis] / previous[axis];
                return previous * ratio;
            }
            return edited;
        }
    }
}
#endif
