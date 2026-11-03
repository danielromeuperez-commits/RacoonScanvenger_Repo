using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class VibrationManager : MonoBehaviour
{
    public static VibrationManager Instance;
    private Coroutine currentRumbleCoroutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Rumble(float lowFrequency, float highFrequency, float duration)
    {
        if (currentRumbleCoroutine != null)
        {
            StopCoroutine(currentRumbleCoroutine);
        }
        currentRumbleCoroutine = StartCoroutine(RumbleRoutine(lowFrequency, highFrequency, duration));
    }

    private IEnumerator RumbleRoutine(float lowFrequency, float highFrequency, float duration)
    {
        if (Gamepad.all.Count == 0)
            yield break;

        Gamepad gamepad = Gamepad.current ?? Gamepad.all[0];
        gamepad.SetMotorSpeeds(lowFrequency, highFrequency);

        yield return new WaitForSeconds(duration);

        gamepad.SetMotorSpeeds(0f, 0f);
        currentRumbleCoroutine = null;
    }

    public void StopRumble()
    {
        if (currentRumbleCoroutine != null)
        {
            StopCoroutine(currentRumbleCoroutine);
            currentRumbleCoroutine = null;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
        }
        else if (Gamepad.all.Count > 0)
        {
            // Por si acaso hay varios mandos conectados
            foreach (var pad in Gamepad.all)
            {
                pad.SetMotorSpeeds(0f, 0f);
            }
        }
    }
}