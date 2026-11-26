using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Luz de alarma (PF_Alarm): gira el pivote de los focos como un rotativo y hace latir la luz puntual al mismo
    /// ritmo (un pico cada vez que un foco pasa por delante: dos por vuelta con dos focos opuestos). Solo visual: cada cliente lo gira por su cuenta.
    /// </summary>
    public class AlarmLightSpinner : MonoBehaviour
    {
        [Tooltip("Pivote que gira (padre de los focos).")]
        [SerializeField] Transform spinPivot;
        [Tooltip("Vueltas por segundo del rotativo.")]
        [SerializeField, Min(0f)] float turnsPerSecond = 0.75f;
        [Tooltip("Luz puntual que late con el giro (opcional).")]
        [SerializeField] Light pulseLight;
        [Tooltip("Intensidad de la luz puntual: mínimo y máximo del latido.")]
        [SerializeField] Vector2 pulseIntensity = new(0.6f, 2f);
        [Tooltip("Focos repartidos en la vuelta (para el número de latidos por vuelta).")]
        [SerializeField, Min(1)] int beams = 2;

        float angle;

        void Reset() => spinPivot = transform;

        void OnEnable() => angle = Random.Range(0f, 360f);   // varias alarmas no van sincronizadas

        void Update()
        {
            angle = Mathf.Repeat(angle + turnsPerSecond * 360f * Time.deltaTime, 360f);
            if (spinPivot != null) spinPivot.localRotation = Quaternion.Euler(0f, angle, 0f);

            if (pulseLight != null)
            {
                // |cos| con tantos picos por vuelta como focos: brilla cuando un foco mira hacia delante.
                float wave = Mathf.Abs(Mathf.Cos(angle * Mathf.Deg2Rad * beams * 0.5f));
                pulseLight.intensity = Mathf.Lerp(pulseIntensity.x, pulseIntensity.y, wave * wave);
            }
        }
    }
}
