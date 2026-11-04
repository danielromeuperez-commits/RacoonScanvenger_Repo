using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Escenas")]
    [SerializeField] string gameSceneName = "SCN_Level_Polish";

    void Start()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        Time.timeScale = 1f;
    }

    public void Play()
    {
        // Transición diagonal hacia la escena del juego.
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance
                .LoadSceneWithDiagonalWipe(gameSceneName);
        }
        else
        {
            // Por seguridad, si no existe el manager
            // cargamos la escena normalmente.
            Debug.LogWarning(
                "MainMenu: no se encontró SceneTransitionManager. " +
                "Cargando la escena sin transición."
            );

            SceneManager.LoadScene(gameSceneName);
        }
    }

    public void QuitGame()
    {
        Debug.Log("Cerrando juego...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}