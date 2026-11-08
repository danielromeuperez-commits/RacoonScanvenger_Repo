using System.Collections.Generic;
using Racoon.VFX;
using Unity.Netcode;
using Unity.Netcode.Components; // Necesario para acceder al NetworkTransform si lo usas
using UnityEngine;

/// <summary>
/// Hace aparecer un objeto mecánico (extintor, despertador...) en este punto cuando el CONTADOR DE LA PARTIDA
/// (MatchTimer) llega al segundo indicado. Si el contador va más rápido (evento de acelerar el tiempo), el objeto
/// también sale antes.
///
/// Los segundos previos se avisa en el suelo con un círculo que se va rellenando en el sentido de las agujas del
/// reloj. El aviso lo calcula cada cliente con el contador replicado (no hace falta red). El servidor decide cuál sale
/// y lo avisa por RPC: cada máquina mueve y activa su copia (los objetos no llevan NetworkTransform).
/// </summary>
public class SpawnerMechItems : NetworkBehaviour
{
    [Header("Configuración de Objetos")]
    [Tooltip("Lista de GameObjects (deben tener NetworkObject y NetworkTransform).")]
    public List<GameObject> targetObjects;

    [Header("Configuración de Posición")]
    [Tooltip("(Opcional) El punto de la escena desde el cual se moverán.")]
    public Transform sourcePoint;

    [Header("Tiempo (contador de la partida)")]
    [Tooltip("Segundos de partida transcurridos (según el contador, MatchTimer) en los que aparece el objeto.")]
    public float interval = 3f;

    [Header("Aviso en el suelo")]
    [Tooltip("Segundos antes de aparecer en los que se ve el círculo llenándose.")]
    [SerializeField, Min(0f)] float warningTime = 5f;
    [Tooltip("Radio del círculo (metros).")]
    [SerializeField, Min(0.1f)] float warningRadius = 0.75f;
    [Tooltip("M_VFX_ToonFlat. Si está vacío se crea uno con el shader SG_VFX_ToonFlat.")]
    [SerializeField] Material flatMaterial;
    [SerializeField] Color fillColor = new(1f, 0.78f, 0.25f);
    [SerializeField] Color fillCenterColor = new(1f, 0.93f, 0.6f);
    [SerializeField] Color backColor = new(0.2f, 0.17f, 0.27f);
    [SerializeField] Color rimColor = new(0.12f, 0.1f, 0.18f);
    [Tooltip("Capas del suelo sobre las que se apoya el círculo (raycast hacia abajo desde este punto).")]
    [SerializeField] LayerMask groundMask = Physics.DefaultRaycastLayers;

    const float PopOutTime = 0.35f;

    MatchTimer matchTimer;
    bool hasSpawned;
    float lastFill = -1f;

    // Aviso (todos los clientes)
    Transform warningRoot;
    FlatPiece backDisk, fillDisk, rim;
    float spawnedAt = -1f;

    MatchTimer Timer => matchTimer != null ? matchTimer : matchTimer = FindFirstObjectByType<MatchTimer>();

    public override void OnDestroy()
    {
        if (warningRoot != null) Destroy(warningRoot.gameObject);
        base.OnDestroy();
    }

    void Update()
    {
        MatchTimer timer = Timer;
        bool running = timer != null && timer.IsSpawned && timer.IsRunning && !timer.IsFinished;
        float elapsed = running ? timer.Duration - timer.TimeRemaining : 0f;

        if (IsServer && !hasSpawned && running && elapsed >= interval)
        {
            TeleportAndShowRandomObject();
            hasSpawned = true;
        }

        UpdateWarning(running, elapsed);
    }

    // ---------------- Aviso en el suelo ----------------

    void UpdateWarning(bool running, float elapsed)
    {
        if (!running || warningTime <= 0f || Application.isBatchMode)
        {
            SetWarningVisible(false);
            return;
        }

        float start = interval - warningTime;
        if (elapsed < start)
        {
            SetWarningVisible(false);
            spawnedAt = -1f;
            return;
        }

        EnsureWarning();
        float scale = 1f;

        if (elapsed >= interval)
        {
            // Ya ha salido: el círculo lleno se encoge y desaparece.
            if (spawnedAt < 0f) spawnedAt = Time.time;
            float k = (Time.time - spawnedAt) / PopOutTime;
            if (k >= 1f)
            {
                SetWarningVisible(false);
                return;
            }
            SetFill(1f);
            scale = 1f + 0.25f * FlatFx.OutCubic(k);
            scale *= 1f - FlatFx.InCubic(k);
        }
        else
        {
            float fill = Mathf.Clamp01((elapsed - start) / warningTime);
            SetFill(fill);
            // Entra con un pequeño rebote y late cada vez más deprisa al acercarse.
            float appear = FlatFx.OutBack(Mathf.Clamp01((elapsed - start) / 0.3f), 2.2f);
            float beat = 1f + 0.04f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 18f, fill));
            scale = appear * beat;
        }

        SetWarningVisible(true);
        warningRoot.localScale = Vector3.one * (warningRadius * scale);
    }

    void SetFill(float fill)
    {
        if (Mathf.Approximately(fill, lastFill)) return;
        lastFill = fill;
        // El disco de relleno es un arco de 0° (+Z) a 360°: revelar el recorrido = horario visto desde arriba.
        fillDisk.Window(fill <= 0f ? -1f : fill, 0f);
    }

    void SetWarningVisible(bool visible)
    {
        if (warningRoot == null) return;
        backDisk.Visible = visible;
        fillDisk.Visible = visible;
        rim.Visible = visible;
    }

    void EnsureWarning()
    {
        if (warningRoot != null) return;

        Material material = flatMaterial;
        if (material == null)
        {
            Shader shader = Shader.Find("Racoon/VFX/SG_VFX_ToonFlat");
            if (shader == null) shader = Shader.Find("Shader Graphs/SG_VFX_ToonFlat");
            if (shader == null) return;
            material = new Material(shader);
        }

        warningRoot = new GameObject(name + " (Aviso)").transform;
        warningRoot.SetPositionAndRotation(GroundPoint(), Quaternion.identity);

        // Disco = arco de radio 0,5 y medio ancho 0,5 (de 0 a 1). Por encima del suelo para que no parpadee.
        backDisk = new FlatPiece("Fondo", warningRoot, FlatMeshes.Arc(0.5f, 0f, 360f, 0.5f, 0f, 64), material, 0);
        backDisk.Transform.localPosition = Vector3.up * 0.02f;
        backDisk.Colors(backColor, backColor, rimColor);

        fillDisk = new FlatPiece("Relleno", warningRoot, FlatMeshes.Arc(0.46f, 0f, 360f, 0.46f, 0f, 64), material, 1);
        fillDisk.Transform.localPosition = Vector3.up * 0.03f;
        fillDisk.Colors(fillColor, fillCenterColor, rimColor);

        rim = new FlatPiece("Borde", warningRoot, FlatMeshes.Arc(1f, 0f, 360f, 0.05f, 0f, 64), material, 2);
        rim.Transform.localPosition = Vector3.up * 0.04f;
        rim.Colors(rimColor, rimColor, rimColor);

        lastFill = -1f;
    }

    Vector3 GroundPoint()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 6f, groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 point = transform.position;
        foreach (RaycastHit hit in hits)
        {
            // Ignora los propios objetos mecánicos (pueden estar aparcados aquí).
            if (targetObjects != null && targetObjects.Exists(o => o != null && hit.collider.transform.IsChildOf(o.transform))) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; }
        }
        return point;
    }

    // ---------------- Spawn ----------------

    // Servidor: elige cuál sale y se lo dice a todos. Los objetos mecánicos no llevan NetworkTransform, así que
    // moverlos solo en el servidor no se veía en el otro jugador: cada máquina mueve y activa su copia de la escena.
    void TeleportAndShowRandomObject()
    {
        if (targetObjects == null || targetObjects.Count == 0)
        {
            Debug.LogWarning("La lista de GameObjects está vacía o no asignada.", this);
            return;
        }

        int randomIndex = Random.Range(0, targetObjects.Count);
        if (targetObjects[randomIndex] == null)
        {
            Debug.LogWarning("El objeto seleccionado de la lista es nulo.", this);
            return;
        }

        ShowObjectRpc(randomIndex, transform.position);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void ShowObjectRpc(int index, Vector3 position)
    {
        GameObject selectedObj = targetObjects != null && index >= 0 && index < targetObjects.Count ? targetObjects[index] : null;
        if (selectedObj == null)
        {
            Debug.LogWarning($"SpawnerMechItems: no encuentro el objeto {index} en esta máquina.", this);
            return;
        }

        // Si algún día llevan NetworkTransform, el servidor lo teletransporta (sin interpolar el viaje).
        NetworkTransform netTransform = selectedObj.GetComponent<NetworkTransform>();
        if (netTransform != null && netTransform.IsSpawned && netTransform.CanCommitToTransform)
            netTransform.Teleport(position, selectedObj.transform.rotation, selectedObj.transform.localScale);
        else
            selectedObj.transform.position = position;

        if (selectedObj.TryGetComponent(out Rigidbody body))
        {
            body.position = position;
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
        }

        if (!selectedObj.activeSelf)
            selectedObj.SetActive(true);

        Debug.Log($"[{(IsServer ? "Servidor" : "Cliente")}] Objeto mecánico en su punto: {selectedObj.name}", selectedObj);
    }
}
