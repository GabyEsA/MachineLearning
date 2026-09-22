using TMPro;
using UnityEngine;

/// <summary>
/// HUD del juego: número de ronda y timer siempre visibles, más una métrica
/// opcional de debug (distancia entre el color promedio de los enemigos y el
/// color de fondo de la cámara) para evidenciar el camuflaje. La métrica está
/// deshabilitada por defecto y se activa desde el Inspector.
/// </summary>
public class HUD : MonoBehaviour
{
    [Header("Referencias de texto (TextMesh Pro)")]
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text colorDistanceText;

    [Header("Referencias de sistema")]
    [SerializeField] private AnimalManager animalManager;

    [Header("Métrica de debug: % de parecido al fondo")]
    [Tooltip("Actívalo para mostrar qué tan parecido es, en promedio, el color de los animales generados en la ronda actual al color de fondo de la cámara — evidencia medible del camuflaje.")]
    [SerializeField] private bool showColorDistanceMetric = false;

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStart += HandleRoundStart;
            GameManager.Instance.OnTimerTick += HandleTimerTick;
        }

        UpdateColorDistanceVisibility();
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStart -= HandleRoundStart;
            GameManager.Instance.OnTimerTick -= HandleTimerTick;
        }
    }

    private void Update()
    {
        // Se actualiza por frame (no solo por evento) porque el promedio de hue
        // de la ronda actual en AnimalManager cambia apenas se spawnea una
        // nueva tanda; así el HUD siempre refleja el valor más reciente.
        if (showColorDistanceMetric)
        {
            UpdateColorDistanceText();
        }
    }

    private void HandleRoundStart(int roundNumber)
    {
        if (roundText != null)
        {
            roundText.text = $"Ronda: {roundNumber}";
        }
    }

    private void HandleTimerTick(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = $"{timeRemaining:F1}s";
        }
    }

    private void UpdateColorDistanceText()
    {
        if (colorDistanceText == null || animalManager == null)
        {
            return;
        }

        float circularDistance = HueUtils.CircularDistance(animalManager.CurrentRoundMeanHue, GetBackgroundHue());
        float similarityPercent = HueUtils.DistanceToSimilarityPercent(circularDistance);
        colorDistanceText.text = $"Parecido al fondo: {similarityPercent:F1}%";
    }

    /// <summary>
    /// Lee el hue directamente del color de fondo de la Main Camera, así no hay
    /// que mantener un valor duplicado sincronizado a mano en el Inspector.
    /// </summary>
    private float GetBackgroundHue()
    {
        if (Camera.main == null)
        {
            return 0f;
        }

        Color.RGBToHSV(Camera.main.backgroundColor, out float hue, out _, out _);
        return hue;
    }

    /// <summary>
    /// Permite alternar la métrica en runtime (ej. desde un botón de debug),
    /// además del toggle inicial del Inspector.
    /// </summary>
    public void SetColorDistanceMetricVisible(bool visible)
    {
        showColorDistanceMetric = visible;
        UpdateColorDistanceVisibility();
    }

    private void UpdateColorDistanceVisibility()
    {
        if (colorDistanceText != null)
        {
            colorDistanceText.gameObject.SetActive(showColorDistanceMetric);
        }
    }

    private void OnValidate()
    {
        // Así el toggle del Inspector también funciona sin necesidad de entrar a Play mode.
        UpdateColorDistanceVisibility();
    }
}