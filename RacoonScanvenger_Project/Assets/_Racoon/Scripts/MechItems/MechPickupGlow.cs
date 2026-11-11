using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Capa de energía en VERDE sobre un objeto mecánico del suelo (extintor, despertador) hasta que alguien lo coge: es la
/// misma capa que se pone sobre el jugador con la bebida energética (shader CameraFXEnergyOverlay), con otros colores.
/// Cada malla del objeto se copia, un pelo hinchada, con el material de la capa; las copias son hijas de la malla
/// original, así que la siguen sin más y solo se ven mientras se ve ella.
/// No se monta a mano: cada pickup la añade sola (<see cref="Attach"/>).
/// </summary>
public sealed class MechPickupGlow : MonoBehaviour
{
    /// <summary>Nombre de los objetos copia (PlayerMechCarry los quita al copiar el modelo al jugador).</summary>
    public const string PieceName = "~MechGlow";

    const string ShaderPath = "CameraFX/CameraFXEnergyOverlay";
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int OriginId = Shader.PropertyToID("_Origin");
    static readonly int FXTimeId = Shader.PropertyToID("_FXTime");

    static readonly Color Deep = new(0.08f, 0.55f, 0.2f, 1f);
    static readonly Color Mid = new(0.25f, 0.85f, 0.32f, 1f);
    static readonly Color Light = new(0.82f, 1f, 0.78f, 1f);

    readonly List<(Renderer source, Renderer copy)> pieces = new();
    Material material;

    public static void Attach(GameObject pickup)
    {
        if (pickup.GetComponent<MechPickupGlow>() == null) pickup.AddComponent<MechPickupGlow>();
    }

    void Start()
    {
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            Debug.LogWarning($"MechPickupGlow: falta el shader Resources/{ShaderPath}.", this);
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "MechPickup Glow" };
        material.SetColor("_Deep", Deep);
        material.SetColor("_Mid", Mid);
        material.SetColor("_Light", Light);
        material.SetFloat("_Push", 0.008f);
        material.SetFloat("_Scale", 7f);

        foreach (Renderer source in GetComponentsInChildren<Renderer>(true))
        {
            if (source.name == PieceName) continue;
            Renderer copy = source switch
            {
                SkinnedMeshRenderer skinned => CopySkinned(skinned),
                MeshRenderer mesh => CopyMesh(mesh),
                _ => null,
            };
            if (copy == null) continue;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.renderingLayerMask = source.renderingLayerMask;
            copy.enabled = false;
            pieces.Add((source, copy));
        }
    }

    Renderer CopyMesh(MeshRenderer source)
    {
        if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return null;
        GameObject go = NewPiece(source);
        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var copy = go.AddComponent<MeshRenderer>();
        copy.sharedMaterials = Materials(filter.sharedMesh.subMeshCount);
        return copy;
    }

    Renderer CopySkinned(SkinnedMeshRenderer source)
    {
        if (source.sharedMesh == null) return null;
        GameObject go = NewPiece(source);
        var copy = go.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = source.sharedMesh;
        copy.bones = source.bones;
        copy.rootBone = source.rootBone;
        copy.localBounds = source.localBounds;
        copy.updateWhenOffscreen = source.updateWhenOffscreen;
        copy.sharedMaterials = Materials(source.sharedMesh.subMeshCount);
        return copy;
    }

    static GameObject NewPiece(Renderer source)
    {
        var go = new GameObject(PieceName) { layer = source.gameObject.layer };
        go.transform.SetParent(source.transform, false);
        return go;
    }

    Material[] Materials(int count)
    {
        var materials = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        return materials;
    }

    void Update()
    {
        if (material == null) return;
        // Latido suave para que se note desde lejos.
        material.SetFloat(IntensityId, 0.85f + 0.15f * Mathf.Sin(Time.time * 3f));
        material.SetFloat(FXTimeId, Time.time);
        material.SetVector(OriginId, transform.position);

        foreach (var (source, copy) in pieces)
            if (copy != null) copy.enabled = source != null && source.enabled && source.gameObject.activeInHierarchy;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
