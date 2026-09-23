using TMPro;
using UnityEngine;

/// <summary>
/// HUD del juego: muestra ronda, timer y puntaje en pantalla en todo momento,
/// más una métrica opcional de debug/evidencia — el % de parecido de color
/// entre los animales de la ronda actual y el fondo — pensada para demostrar
/// visualmente que el camuflaje está ocurriendo a lo largo de las rondas.
///
/// Este script es puramente de presentación: no calcula nada por su cuenta
/// (salvo el % de parecido, que combina datos que ya existen en otros
/// módulos), solo escucha eventos de GameManager y AnimalManager y actualiza
/// los textos de TextMesh Pro correspondientes.
/// </summary>
public class HUD : MonoBehaviour
{
    [Header("Referencias de texto (TextMesh Pro)")]
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text scoreText;
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
            GameManager.Instance.OnScoreChanged += HandleScoreChanged;
        }

        UpdateColorDistanceVisibility();
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStart -= HandleRoundStart;
            GameManager.Instance.OnTimerTick -= HandleTimerTick;
            GameManager.Instance.OnScoreChanged -= HandleScoreChanged;
        }
    }

    private void Update()
    {
        // Se actualiza por frame (no solo por evento) porque
        // AnimalManager.CurrentRoundMeanHue cambia apenas se spawnea una
        // nueva tanda de animales, y ese cambio no dispara ningún evento
        // propio; revisarlo cada frame es la forma más simple de que el HUD
        // siempre refleje el valor más reciente sin acoplar AnimalManager a la UI.
        if (showColorDistanceMetric)
        {
            UpdateColorDistanceText();
        }
    }

    /// <summary>Actualiza el texto de ronda. Suscrito a GameManager.OnRoundStart.</summary>
    private void HandleRoundStart(int roundNumber)
    {
        if (roundText != null)
        {
            roundText.text = $"Ronda: {roundNumber}";
        }
    }

    /// <summary>Actualiza el texto del timer. Suscrito a GameManager.OnTimerTick (se dispara cada frame durante la ronda).</summary>
    private void HandleTimerTick(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = $"{timeRemaining:F1}s";
        }
    }

    /// <summary>Actualiza el texto de puntaje. Suscrito a GameManager.OnScoreChanged.</summary>
    private void HandleScoreChanged(int score)
    {
        if (scoreText != null)
        {
            scoreText.text = $"Puntaje: {score}";
        }
    }

    /// <summary>
    /// Calcula y muestra el % de parecido entre el color promedio de los
    /// animales de la ronda actual (AnimalManager.CurrentRoundMeanHue) y el
    /// color de fondo de la cámara, usando distancia circular de hue (ver
    /// HueUtils) para que el resultado sea correcto incluso con fondos cerca
    /// del rojo puro (donde una resta simple de hue daría un valor erróneo).
    /// 100% = mismo color exacto, 0% = colores opuestos en la rueda de color.
    /// </summary>
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
    /// Lee el hue directamente del color de fondo de la Main Camera, en vez
    /// de pedir que se duplique ese valor a mano en el Inspector del HUD —
    /// así nunca puede desincronizarse del color real configurado en la cámara.
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
    /// Permite alternar la métrica de parecido al fondo en runtime (por
    /// ejemplo, desde un botón de debug en pantalla), además del toggle
    /// inicial disponible en el Inspector.
    /// </summary>
    public void SetColorDistanceMetricVisible(bool visible)
    {
        showColorDistanceMetric = visible;
        UpdateColorDistanceVisibility();
    }

    /// <summary>Muestra u oculta el GameObject del texto de % de parecido, según el toggle actual.</summary>
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