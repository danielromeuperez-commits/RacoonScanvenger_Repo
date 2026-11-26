using UnityEngine;

/// <summary>
/// Rareza de un punto de saqueo (estantería, puesto de fruta, panadería...). Agrupa todo lo que define su balance
/// para ajustar el gameplay desde un sitio: lo que da cada objeto, cuántos hay, cuánto tarda en reponerse y cómo
/// se comporta durante el evento «OFERTA x2». Se asigna en el campo «Preset» de <see cref="LootPoint"/>.
/// </summary>
[CreateAssetMenu(menuName = "Racoon/Loot Rarity Preset", fileName = "LOOT_New")]
public class LootRarityPreset : ScriptableObject
{
    [Tooltip("Nombre que sale en los logs y en el inspector.")]
    public string displayName = "Común";

    [Header("Loot por objeto")]
    [Tooltip("Puntos que da cada objeto recogido.")]
    [Min(0)] public int pointsPerItem = 10;

    [Tooltip("Peso que añade cada objeto recogido.")]
    [Min(0f)] public float weightPerItem = 1f;

    [Header("Cantidad de objetos")]
    [Min(1)] public int minItems = 4;
    [Min(1)] public int maxItems = 7;

    [Header("Reposición")]
    [Tooltip("Segundos mínimos hasta reponerse una vez vacía. 0 = no se repone.")]
    [Min(0f)] public float respawnTimeMin = 30f;

    [Tooltip("Segundos máximos hasta reponerse. Se elige un valor al azar entre mín. y máx. para que no se repongan todas a la vez.")]
    [Min(0f)] public float respawnTimeMax = 40f;

    [Header("Ritmo de saqueo")]
    [Tooltip("Espera entre recolecciones de un mismo jugador en estado normal (segundos).")]
    [Min(0f)] public float normalCooldown = 0f;

    [Header("Evento OFERTA x2")]
    [Tooltip("Recolecciones dobles antes de que se acabe la oferta en esta estantería.")]
    [Min(1)] public int maxDoublePointsCollections = 8;

    [Tooltip("Espera entre recolecciones de un mismo jugador mientras la oferta está activa (segundos).")]
    [Min(0f)] public float doublePointsCooldown = 1.5f;

    void OnValidate()
    {
        if (maxItems < minItems) maxItems = minItems;
        if (respawnTimeMax < respawnTimeMin) respawnTimeMax = respawnTimeMin;
    }

    /// <summary>Tiempo de reposición elegido al azar dentro del rango.</summary>
    public float RollRespawnTime() => Random.Range(respawnTimeMin, respawnTimeMax);
}
