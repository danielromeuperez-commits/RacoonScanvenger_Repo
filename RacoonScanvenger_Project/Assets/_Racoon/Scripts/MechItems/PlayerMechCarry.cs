using System.Collections;
using Racoon.Cameras;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racoon.Player
{
    /// <summary>Objeto mecánico que lleva un jugador hasta venderlo (ver PlayerController.CarriedMech).</summary>
    public enum MechKind : byte
    {
        None = 0,
        Extintor = 1,
        Clock = 2,
    }

    /// <summary>
    /// Lo que se ve y se siente mientras un jugador lleva un objeto mecánico, hasta que lo vende:
    ///  - El modelo: el extintor a la espalda; el despertador delante, temblando (lo ven todos los clientes).
    ///    Mientras tanto el objeto de las cajas no se ve ni se puede usar (PlayerController / PlayerItemHandler).
    ///  - Despertador (solo el dueño): el mando vibra fuerte y la pantalla tiembla todo el rato hasta venderlo.
    /// Lo añade PlayerController la primera vez que hace falta; el estado manda (CarriedMech), el modelo es una
    /// copia solo visual del objeto que se ha cogido.
    /// </summary>
    public class PlayerMechCarry : MonoBehaviour
    {
        // Despertador: «muy fuerte» y continuo.
        const float ClockLowMotor = 0.85f, ClockHighMotor = 1f;
        static readonly CameraShakeProfile ClockShake =
            new(1f, new Vector3(1.6f, 1.6f, 2.4f), new Vector3(0.05f, 0.05f, 0f), 26f);

        // Dónde va el modelo (local al jugador) y su tamaño máximo.
        static readonly Vector3 BackOffset = new(0f, 0.5f, -0.24f);
        static readonly Vector3 FrontOffset = new(0f, 0.42f, 0.34f);
        const float MaxSize = 0.42f;

        PlayerController controller;
        GameObject visual;
        Vector3 visualScale;
        MechKind visualKind;
        float attachTime;
        readonly object shakeKey = new();
        Gamepad rumblingPad;

        void Awake() => controller = GetComponent<PlayerController>();

        void OnDisable() => StopOwnerEffects();

        void OnDestroy()
        {
            StopOwnerEffects();
            if (visual != null) Destroy(visual);
        }

        /// <summary>
        /// En todos los clientes, al cogerlo: copia el aspecto de <paramref name="source"/> (el objeto del suelo, que va
        /// a desaparecer) y lo coloca en el jugador.
        /// </summary>
        public void AttachVisual(MechKind kind, GameObject source)
        {
            if (visual != null) Destroy(visual);
            visual = null;
            if (source == null || kind == MechKind.None) return;
            StartCoroutine(BuildVisual(kind, source));
        }

        // Copia solo visual (sin scripts, red ni física). Se limpia bajo un padre inactivo y se engancha al frame
        // siguiente: dentro de un RPC no se puede usar DestroyImmediate.
        IEnumerator BuildVisual(MechKind kind, GameObject source)
        {
            var holder = new GameObject("MechVisualHolder");
            holder.SetActive(false);
            GameObject copy = Instantiate(source, holder.transform);
            copy.transform.localScale = source.transform.lossyScale;
            MonoBehaviour[] behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (behaviours[i] is Unity.Netcode.NetworkBehaviour) Destroy(behaviours[i]);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (behaviours[i] != null && behaviours[i] is not Unity.Netcode.NetworkBehaviour) Destroy(behaviours[i]);
            foreach (Collider col in copy.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
                Destroy(col);
            }
            foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

            yield return null;

            if (copy == null || controller == null || controller.CarriedMech != kind)
            {
                Destroy(holder);
                yield break;
            }

            copy.transform.SetParent(transform, false);
            Destroy(holder);
            copy.SetActive(true);

            // Tamaño máximo (los objetos del suelo están pensados para verse desde lejos).
            var bounds = new Bounds(copy.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            float size = any ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) : MaxSize;
            if (size > MaxSize) copy.transform.localScale *= MaxSize / size;
            // Escala local respecto al jugador (por si el jugador no está a escala 1).
            copy.transform.localScale = Vector3.Scale(copy.transform.localScale,
                new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z));

            visual = copy;
            visualKind = kind;
            visualScale = copy.transform.localScale;
            attachTime = Time.time;
        }

        void LateUpdate()
        {
            MechKind carried = controller != null ? controller.CarriedMech : MechKind.None;

            if (carried != visualKind && visual != null)
            {
                // Vendido (o cambiado): se encoge y desaparece.
                StartCoroutine(ShrinkAway(visual));
                visual = null;
                visualKind = MechKind.None;
            }

            if (visual != null) Animate(carried);
            UpdateOwnerEffects(carried);
        }

        void Animate(MechKind kind)
        {
            float t = Time.time;
            float pop = Mathf.Clamp01((t - attachTime) / 0.25f);
            pop = 1f + 2.2f * Mathf.Pow(pop - 1f, 3f) + 1.2f * Mathf.Pow(pop - 1f, 2f);   // OutBack
            Transform v = visual.transform;
            v.localScale = visualScale * pop;
            if (kind == MechKind.Clock)
            {
                // Delante, sonando: tiembla rápido.
                v.localPosition = FrontOffset + new Vector3(Mathf.Sin(t * 61f), Mathf.Sin(t * 53f + 1f), 0f) * 0.012f;
                v.localRotation = Quaternion.Euler(0f, 180f, Mathf.Sin(t * 47f) * 9f);
            }
            else
            {
                // A la espalda, inclinado, con un bote suave.
                v.localPosition = BackOffset + Vector3.up * (Mathf.Sin(t * 3f) * 0.01f);
                v.localRotation = Quaternion.Euler(-12f, 0f, 8f);
            }
        }

        static IEnumerator ShrinkAway(GameObject go)
        {
            Vector3 scale = go.transform.localScale;
            for (float time = 0f; time < 0.18f && go != null; time += Time.deltaTime)
            {
                go.transform.localScale = scale * (1f - time / 0.18f);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // ---------------- Despertador: mando y pantalla (solo el dueño) ----------------

        // ---------------- Despertador: mando y pantalla (solo el dueño) ----------------

        void UpdateOwnerEffects(MechKind carried)
        {
            bool ringing = carried == MechKind.Clock && controller != null && controller.IsSpawned && controller.IsOwner;
            if (!ringing)
            {
                StopOwnerEffects();
                return;
            }

            // Reproducir el sonido solo si NO está sonando ya (evita que pete y sature cada frame)
            if (AudioManager.Instance != null && AudioManager.Instance.sfxSource != null)
            {
                AudioClip clockClip = AudioManager.Instance.sfxLibrary[13]; // Cambia 13 por tu índice de audio
                if (!AudioManager.Instance.sfxSource.isPlaying || AudioManager.Instance.sfxSource.clip != clockClip)
                {
                    AudioManager.Instance.sfxSource.clip = clockClip;
                    AudioManager.Instance.sfxSource.loop = true; // Lo ponemos en bucle mientras lo lleva
                    AudioManager.Instance.sfxSource.volume = AudioManager.Instance.GetMasterVolume() * AudioManager.Instance.GetSFXVolume();
                    AudioManager.Instance.sfxSource.Play();
                }
            }

            CameraShaker.SetContinuous(shakeKey, ClockShake, 1f, 6f);
            Gamepad pad = controller.InputHandler != null ? controller.InputHandler.PairedDevice as Gamepad : null;
            if (pad != rumblingPad && rumblingPad != null && rumblingPad.added) rumblingPad.SetMotorSpeeds(0f, 0f);
            rumblingPad = pad;
            if (pad != null && pad.added) pad.SetMotorSpeeds(ClockLowMotor, ClockHighMotor);
        }

        void StopOwnerEffects()
        {
            CameraShaker.SetContinuous(shakeKey, ClockShake, 0f, 6f);
            if (rumblingPad != null && rumblingPad.added) rumblingPad.SetMotorSpeeds(0f, 0f);
            rumblingPad = null;

            // Parar el sonido, limpiar el clip y quitar el loop para que no afecte a otros SFX normales
            if (controller != null && controller.IsOwner && AudioManager.Instance != null && AudioManager.Instance.sfxSource != null)
            {
                if (AudioManager.Instance.sfxSource.clip == AudioManager.Instance.sfxLibrary[13]) // Cambia 13 por tu índice
                {
                    AudioManager.Instance.sfxSource.Stop();
                    AudioManager.Instance.sfxSource.loop = false;
                    AudioManager.Instance.sfxSource.clip = null;
                }
            }
        }
    }
}