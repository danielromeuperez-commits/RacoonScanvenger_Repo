using System.Collections;
using Racoon.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racoon.Feedback
{
    /// <summary>
    /// Vibración del mando de UN jugador concreto (el dispositivo que tiene emparejado su PlayerInputHandler),
    /// no del "último mando usado" como VibrationManager. Solo hace algo en la máquina de ese jugador
    /// (IsOwner) y si juega con mando.
    /// </summary>
    public static class PlayerRumble
    {
        static Runner runner;

        /// <summary>low = motor grave, high = motor agudo (0-1).</summary>
        public static void Rumble(PlayerController player, float low, float high, float duration)
        {
            if (player == null || !player.IsOwner || duration <= 0f) return;
            if (player.InputHandler == null || player.InputHandler.PairedDevice is not Gamepad gamepad) return;

            if (runner == null)
            {
                var go = new GameObject("PlayerRumble") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<Runner>();
            }

            runner.Play(gamepad, low, high, duration);
        }

        class Runner : MonoBehaviour
        {
            Coroutine routine;
            Gamepad current;

            public void Play(Gamepad gamepad, float low, float high, float duration)
            {
                if (routine != null) StopCoroutine(routine);
                routine = StartCoroutine(Routine(gamepad, low, high, duration));
            }

            IEnumerator Routine(Gamepad gamepad, float low, float high, float duration)
            {
                current = gamepad;
                gamepad.SetMotorSpeeds(low, high);
                yield return new WaitForSecondsRealtime(duration);
                Stop();
            }

            void Stop()
            {
                if (current != null && current.added) current.SetMotorSpeeds(0f, 0f);
                current = null;
                routine = null;
            }

            void OnDisable() => Stop();
        }
    }
}
