using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Racoon.Feedback
{
    /// <summary>
    /// Hueco para un efecto de sonido. Todavía NO hay sonidos en el juego: cada script que necesita uno
    /// declara un SfxCue con un Tooltip que explica qué debe sonar, y mientras no tenga clips no hace nada.
    /// Cuando lleguen los audios basta con arrastrarlos al campo en el inspector (sin tocar código).
    /// </summary>
    [Serializable]
    public class SfxCue
    {
        [Tooltip("Uno o varios clips: cada vez se elige uno al azar. Vacío = no suena (aún no hay sonido).")]
        public AudioClip[] clips;

        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("Variación aleatoria del pitch (±). Evita que el mismo sonido repetido suene robótico.")]
        [Range(0f, 0.5f)] public float pitchVariation = 0.06f;

        [Tooltip("0 = 2D (se oye igual en cualquier sitio, para UI). 1 = 3D (sale de la posición del objeto).")]
        [Range(0f, 1f)] public float spatialBlend;

        public bool IsEmpty => clips == null || clips.Length == 0;

        /// <summary>Suena en <paramref name="position"/> (solo importa si spatialBlend &gt; 0). Sin clips no hace nada.</summary>
        public void Play(Vector3 position, float pitchMultiplier = 1f) => Play(position, pitchMultiplier, spatialBlend);

        /// <summary>Para UI: suena en 2D, sin posición, aunque el campo tenga spatialBlend.</summary>
        public void Play2D(float pitchMultiplier = 1f) => Play(Vector3.zero, pitchMultiplier, 0f);

        void Play(Vector3 position, float pitchMultiplier, float blend)
        {
            if (IsEmpty) return;

            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            float pitch = pitchMultiplier * (1f + Random.Range(-pitchVariation, pitchVariation));

            // Fuente de usar y tirar. Si algún día hay muchos sonidos a la vez, cambiar por un pool.
            var go = new GameObject("SFX " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = blend;
            source.Play();
            UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.05f, Mathf.Abs(pitch)) + 0.1f);
        }
    }
}
