using UnityEngine;
using System.Collections.Generic;
using static Regions;

public class MaterialZoneConverter : MonoBehaviour
{
    [Header("Conversion Settings")]
    public Shader customStandardWithZones; // Inspector���� "Custom/Standard_WithZones" �Ҵ�
    public GameObject[] targetObjects;
    public bool autoConvertOnStart = true;
    public bool convertChildrenRecursively = true;

    [Header("Zone Settings")]
    public ZoneMode defaultZoneMode = ZoneMode.Off;
    public float defaultZoneRadius = 0.9f;

    private Dictionary<Material, Material> originalToConverted = new Dictionary<Material, Material>();

    void Start()
    {
        if (autoConvertOnStart)
        {
            ConvertAllStandardMaterials();

        }
    }

    /// <summary>
    /// ������ GameObject���� ��� Standard Material�� Zone Shader�� ��ȯ
    /// </summary>
    public void ConvertAllStandardMaterials()
    {
        if (customStandardWithZones == null)
        {
            Debug.LogError("Custom Standard With Zones shader not assigned!");
            return;
        }

        if (targetObjects == null || targetObjects.Length == 0)
        {
            Debug.LogWarning("No target objects assigned! Add GameObjects to 'Target Objects' array in Inspector.");
            return;
        }

        int totalConvertedCount = 0;

        // �� Ÿ�� ������Ʈ ó��
        foreach (GameObject targetObj in targetObjects)
        {
            if (targetObj == null)
            {
                Debug.LogWarning("Null object in target objects array, skipping...");
                continue;
            }

            Debug.Log($"Processing target object: {targetObj.name}");
            int convertedCount = ConvertObject(targetObj);
            totalConvertedCount += convertedCount;
        }

        Debug.Log($"Total converted {totalConvertedCount} Standard materials to Zone-enabled materials across {targetObjects.Length} objects");
    }

    /// <summary>
    /// Ư�� GameObject�� Standard Material ��ȯ
    /// </summary>
    int ConvertObject(GameObject targetObject)
    {
        // Deliberately includeInactive:false (default). The "Objects"/"Walls"
        // sub-containers under each house hold plain Cube primitives (Object_2_*/
        // wall_2_*) -- these are the "furniture and wall bounding boxes manually
        // placed in Unity" the paper describes (Sec 5) purely for computing the
        // 2D floor plan / freespace subtraction, not for rendering. They're
        // inactive by design; only the photorealistic scan meshes (*_floor/
        // *_part1/*_part2) are meant to be visible. A prior change here force-
        // activated and zone-converted them, which surfaced the bare gray Cubes
        // as a "wall stuck in the middle of freespace" -- reverted 2026-08-14.
        Renderer[] renderers = convertChildrenRecursively ?
            targetObject.GetComponentsInChildren<Renderer>() :
            targetObject.GetComponents<Renderer>();

        if (renderers.Length == 0)
        {
            Debug.LogWarning($"No renderers found on {targetObject.name}");
            return 0;
        }

        Debug.Log($"Found {renderers.Length} renderers in {targetObject.name}");

        int convertedCount = 0;

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            bool hasChanges = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];

                if (mat == null) continue;

                // Standard Shader �Ǵ� �� �������� Ȯ��
                if (IsStandardShader(mat.shader))
                {
                    // �̹� ��ȯ�� Material�� �ִ��� Ȯ��
                    if (originalToConverted.ContainsKey(mat))
                    {
                        materials[i] = originalToConverted[mat];
                        //Debug.Log($"Reusing converted material: {mat.name}");
                    }
                    else
                    {
                        // ���ο� Material ���� �� ��ȯ
                        Material convertedMat = ConvertMaterial(mat);
                        originalToConverted[mat] = convertedMat;
                        materials[i] = convertedMat;
                        convertedCount++;
                        //Debug.Log($"Converted: {mat.name} -> {convertedMat.name}");
                    }

                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                renderer.sharedMaterials = materials;
            }
        }

        return convertedCount;
    }

    /// <summary>
    /// Shader�� Standard Shader���� Ȯ��
    /// </summary>
    bool IsStandardShader(Shader shader)
    {
        string shaderName = shader.name;
        return shaderName == "Standard" ||
               shaderName == "Standard (Specular setup)" ||
               shaderName.Contains("Standard");
    }

    /// <summary>
    /// Material�� Zone Shader�� ��ȯ�ϸ鼭 ��� �Ӽ� ����
    /// </summary>
    Material ConvertMaterial(Material sourceMaterial)
    {
        // �� Material ����
        Material newMat = new Material(customStandardWithZones);
        newMat.name = sourceMaterial.name + "_WithZones";

        // ��� Standard Shader �Ӽ� ����
        CopyStandardProperties(sourceMaterial, newMat);

        // Zone �⺻�� ����
        InitializeZoneProperties(newMat);

        //Debug.Log($"Converted material: {sourceMaterial.name} -> {newMat.name}");

        return newMat;
    }

    /// <summary>
    /// Standard Shader�� ��� �Ӽ��� ����
    /// </summary>
    void CopyStandardProperties(Material source, Material target)
    {
        // Main Maps
        CopyTextureProperty(source, target, "_MainTex");
        CopyColorProperty(source, target, "_Color");

        // Metallic/Smoothness
        CopyFloatProperty(source, target, "_Metallic");
        CopyFloatProperty(source, target, "_Glossiness");
        CopyFloatProperty(source, target, "_GlossMapScale");
        CopyFloatProperty(source, target, "_SmoothnessTextureChannel");
        CopyTextureProperty(source, target, "_MetallicGlossMap");

        // Normal Map
        CopyTextureProperty(source, target, "_BumpMap");
        CopyFloatProperty(source, target, "_BumpScale");

        // Height Map
        CopyTextureProperty(source, target, "_ParallaxMap");
        CopyFloatProperty(source, target, "_Parallax");

        // Occlusion
        CopyTextureProperty(source, target, "_OcclusionMap");
        CopyFloatProperty(source, target, "_OcclusionStrength");

        // Emission
        CopyTextureProperty(source, target, "_EmissionMap");
        CopyColorProperty(source, target, "_EmissionColor");
        if (source.IsKeywordEnabled("_EMISSION"))
        {
            target.EnableKeyword("_EMISSION");
        }

        // Detail Maps
        CopyTextureProperty(source, target, "_DetailMask");
        CopyTextureProperty(source, target, "_DetailAlbedoMap");
        CopyTextureProperty(source, target, "_DetailNormalMap");
        CopyFloatProperty(source, target, "_DetailNormalMapScale");
        CopyFloatProperty(source, target, "_UVSec");

        // Rendering Mode
        CopyFloatProperty(source, target, "_Mode");
        CopyFloatProperty(source, target, "_SrcBlend");
        CopyFloatProperty(source, target, "_DstBlend");
        CopyFloatProperty(source, target, "_ZWrite");
        CopyFloatProperty(source, target, "_Cutoff");

        // Keywords
        if (source.IsKeywordEnabled("_NORMALMAP"))
            target.EnableKeyword("_NORMALMAP");
        if (source.IsKeywordEnabled("_METALLICGLOSSMAP"))
            target.EnableKeyword("_METALLICGLOSSMAP");
        if (source.IsKeywordEnabled("_DETAIL_MULX2"))
            target.EnableKeyword("_DETAIL_MULX2");
        if (source.IsKeywordEnabled("_PARALLAXMAP"))
            target.EnableKeyword("_PARALLAXMAP");
        if (source.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"))
            target.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        if (source.IsKeywordEnabled("_GLOSSYREFLECTIONS_OFF"))
            target.EnableKeyword("_GLOSSYREFLECTIONS_OFF");

        // Render Queue
        target.renderQueue = source.renderQueue;
    }

    void CopyTextureProperty(Material source, Material target, string propertyName)
    {
        if (source.HasProperty(propertyName) && target.HasProperty(propertyName))
        {
            Texture tex = source.GetTexture(propertyName);
            target.SetTexture(propertyName, tex);

            if (tex != null)
            {
                target.SetTextureOffset(propertyName, source.GetTextureOffset(propertyName));
                target.SetTextureScale(propertyName, source.GetTextureScale(propertyName));
            }
        }
    }

    void CopyColorProperty(Material source, Material target, string propertyName)
    {
        if (source.HasProperty(propertyName) && target.HasProperty(propertyName))
        {
            target.SetColor(propertyName, source.GetColor(propertyName));
        }
    }

    void CopyFloatProperty(Material source, Material target, string propertyName)
    {
        if (source.HasProperty(propertyName) && target.HasProperty(propertyName))
        {
            target.SetFloat(propertyName, source.GetFloat(propertyName));
        }
    }

    /// <summary>
    /// Zone �⺻ �Ӽ� �ʱ�ȭ
    /// </summary>
    void InitializeZoneProperties(Material material)
    {
        material.SetInt("_ZoneMode", (int)defaultZoneMode);
        material.SetFloat("_P", 2.0f);
        material.SetFloat("_ZoneRadius", defaultZoneRadius);
        material.SetFloat("_EnableZoneClipping", 1.0f);
        material.SetInt("_Length", 0);
        material.SetInt("_BaseRegion", 0);
        material.SetInt("_WhichRegion", 0);

        // _Users is intentionally NOT set per-material here (used to be initialized
        // to an all-zero array) -- a per-material property shadows a same-named
        // global property in Unity, so that override was silently defeating
        // Regions.feedZone()'s Shader.SetGlobalVectorArray("_Users", ...) every
        // frame, making every RemoteZone circle check distance-to-origin instead
        // of the real avatar position. Leave it unset so the global always applies.
        Color[] emptyColors = new Color[10];
        material.SetColorArray("_Colors", emptyColors);
    }

    /// <summary>
    /// ��ȯ�� Material�鿡 Zone ������ ����
    /// </summary>
    public void ApplyZoneDataToAllConvertedMaterials(Vector4[] userPositions, int baseRegion, int whichRegion, ZoneMode zoneMode)
    {
        foreach (var convertedMat in originalToConverted.Values)
        {
            ApplyZoneData(convertedMat, userPositions, baseRegion, whichRegion, zoneMode);
        }

        Debug.Log($"Applied zone data to {originalToConverted.Count} converted materials");
    }

    void ApplyZoneData(Material material, Vector4[] userPositions, int baseRegion, int whichRegion, ZoneMode zoneMode)
    {
        material.SetInt("_ZoneMode", (int)zoneMode);
        material.SetVectorArray("_Users", userPositions);
        material.SetInt("_Length", userPositions.Length);
        material.SetInt("_BaseRegion", baseRegion);
        material.SetInt("_WhichRegion", whichRegion);
    }

    /// <summary>
    /// ���� Material�� �ǵ�����
    /// </summary>
    public void RevertToOriginalMaterials()
    {
        Renderer[] renderers = convertChildrenRecursively ?
            GetComponentsInChildren<Renderer>() :
            GetComponents<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            bool hasChanges = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];

                // ��ȯ�� Material���� Ȯ��
                foreach (var kvp in originalToConverted)
                {
                    if (kvp.Value == mat)
                    {
                        materials[i] = kvp.Key;
                        hasChanges = true;
                        break;
                    }
                }
            }

            if (hasChanges)
            {
                renderer.sharedMaterials = materials;
            }
        }

        Debug.Log("Reverted to original materials");
    }
}

// ��� ���� ��ũ��Ʈ
public class ZoneSystemExample : MonoBehaviour
{
    public MaterialZoneConverter converter;

    void Start()
    {
        // Material ��ȯ�� MaterialZoneConverter�� �ڵ����� ó��
        // ��ȯ �� Zone ������ ����
        StartCoroutine(ApplyZoneDataAfterDelay());
    }

    System.Collections.IEnumerator ApplyZoneDataAfterDelay()
    {
        yield return new WaitForSeconds(0.5f); // ��ȯ �Ϸ� ���

        Vector4[] playerPositions = new Vector4[]
        {
            new Vector4(0, 0, 0, 0),
            new Vector4(3, 0, 0, 0),
            new Vector4(0, 0, 3, 0),
        };

        converter.ApplyZoneDataToAllConvertedMaterials(
            playerPositions,
            0,              // baseRegion
            0,              // whichRegion
            ZoneMode.Local  // Local ���
        );
    }
}