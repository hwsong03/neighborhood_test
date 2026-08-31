using UnityEngine;

/// <summary>
/// Helper class to find avatar joints by name instead of hardcoded child indices.
/// This is necessary because Meta Avatar child order can vary between different avatar instances.
/// </summary>
public static class AvatarJointHelper
{
    // Joint names used in Meta Avatar hierarchy
    private const string JOINT_CHEST_NAME = "Joint Chest";
    private const string JOINT_HEAD_NAME = "Joint Head";

    /// <summary>
    /// Find the Joint Chest transform in an avatar hierarchy.
    /// This is typically used for tracking avatar body position.
    /// </summary>
    /// <param name="avatarRoot">The root transform of the avatar (e.g., LocalAvatar, RemoteAvatar)</param>
    /// <returns>The Joint Chest transform, or null if not found</returns>
    public static Transform FindJointChest(Transform avatarRoot)
    {
        if (avatarRoot == null) return null;
        // Meta Avatar SDK builds its runtime skeleton with "Joint Chest" nested several
        // levels below the entity root, not as a direct child -- FindChildByName (direct
        // children only) was silently returning null here on every real avatar, which
        // made every caller fall back to the avatar's raw root position instead (see
        // e.g. LocalOptimizationRunner.BuildOptimizationInputs's own comment on why that
        // fallback is wrong for ROI/boundary centering).
        return FindChildByNameRecursive(avatarRoot, JOINT_CHEST_NAME);
    }

    /// <summary>
    /// Find the Joint Head transform in an avatar hierarchy.
    /// This is typically used for camera/neck tracking.
    /// </summary>
    /// <param name="avatarRoot">The root transform of the avatar (e.g., LocalAvatar, RemoteAvatar)</param>
    /// <returns>The Joint Head transform, or null if not found</returns>
    public static Transform FindJointHead(Transform avatarRoot)
    {
        if (avatarRoot == null) return null;
        // Same reasoning as FindJointChest above -- "Joint Head" isn't a direct child either.
        return FindChildByNameRecursive(avatarRoot, JOINT_HEAD_NAME);
    }

    /// <summary>
    /// Find a child transform by name (searches only direct children, not recursive).
    /// </summary>
    /// <param name="parent">The parent transform to search in</param>
    /// <param name="childName">The name of the child to find</param>
    /// <returns>The found transform, or null if not found</returns>
    public static Transform FindChildByName(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }
        }

        // Not found in direct children
        return null;
    }

    /// <summary>
    /// Find a child transform by name recursively (searches all descendants).
    /// Use this if the joint might be nested deeper in the hierarchy.
    /// </summary>
    /// <param name="parent">The parent transform to search in</param>
    /// <param name="childName">The name of the child to find</param>
    /// <returns>The found transform, or null if not found</returns>
    public static Transform FindChildByNameRecursive(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;

        // Check direct children first
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }
        }

        // Search recursively
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildByNameRecursive(parent.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
