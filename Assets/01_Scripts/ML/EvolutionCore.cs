using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procesa los AnimalData de la ronda que acaba de cerrar (recibidos vía
/// DataCollector.OnRoundDataCollected), calcula la Aptitud de cada animal,
/// y a partir de ella genera los SpawnParameters de la siguiente ronda:
/// media/desviación ponderada de tamaño y color, y distribución de
/// probabilidad por tipo de animal. Se los entrega a AnimalManager.
/// </summary>
public class EvolutionCore : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private DataCollector dataCollector;
    [SerializeField] private AnimalManager animalManager;

    [Header("Piso mínimo de desviación estándar")]
    [Tooltip("Evita que la desviación llegue exactamente a 0 por redondeo antes de tiempo, lo cual congelaría la variedad de golpe en vez de converger gradualmente.")]
    [SerializeField] private float minStdDevFloor = 0.01f;

    // El HUD lee esto para mostrar la métrica de distancia de color al fondo,
    // sin que EvolutionCore necesite saber nada sobre UI.
    public float LastMeanHue { get; private set; }

    private void Start()
    {
        // Se suscribe en Start() por la misma razón que el resto de los módulos:
        // garantiza que las referencias ya estén completamente inicializadas.
        if (dataCollector != null)
        {
            dataCollector.OnRoundDataCollected += HandleRoundDataCollected;
        }
    }

    private void OnDisable()
    {
        if (dataCollector != null)
        {
            dataCollector.OnRoundDataCollected -= HandleRoundDataCollected;
        }
    }

    private void HandleRoundDataCollected(int roundNumber, List<AnimalData> roundData)
    {
        if (roundData == null || roundData.Count == 0)
        {
            // Caso borde: ronda sin ningún animal registrado (no debería ocurrir
            // en condiciones normales). Se conservan los parámetros actuales.
            return;
        }

        float roundDuration = GameManager.Instance != null ? GameManager.Instance.RoundDuration : 10f;
        float[] aptitudes = CalculateAptitudes(roundData, roundDuration);

        (float meanSize, float stdDevSize) = CalculateWeightedStats(roundData, aptitudes, a => a.Tamaño);
        (float meanHue, float stdDevHue) = CalculateWeightedStats(roundData, aptitudes, a => GetHue(a.Color));
        float[] typeProbabilities = CalculateTypeDistribution(roundData, aptitudes);

        LastMeanHue = meanHue;

        // minSize/maxSize se conservan sin cambios: son límites de diseño, no
        // atributos que deban evolucionar por aptitud.
        SpawnParameters currentBounds = animalManager.CurrentParameters;

        var newParams = new SpawnParameters
        {
            meanSize = meanSize,
            stdDevSize = Mathf.Max(stdDevSize, minStdDevFloor),
            minSize = currentBounds.minSize,
            maxSize = currentBounds.maxSize,
            meanHue = meanHue,
            stdDevHue = Mathf.Max(stdDevHue, minStdDevFloor),
            typeProbabilities = typeProbabilities
        };

        animalManager.UpdateSpawnParameters(newParams);
    }

    /// <summary>Aptitud(enemigo) = Tiempo_Sobrevivido / Tiempo_Ronda.</summary>
    private float[] CalculateAptitudes(List<AnimalData> roundData, float roundDuration)
    {
        float[] aptitudes = new float[roundData.Count];
        for (int i = 0; i < roundData.Count; i++)
        {
            aptitudes[i] = roundDuration > 0f ? roundData[i].Tiempo_Sobrevivido / roundDuration : 0f;
        }
        return aptitudes;
    }

    /// <summary>
    /// Media y desviación estándar ponderadas por aptitud, para cualquier
    /// atributo numérico extraído con el selector dado (reutilizable para
    /// Tamaño y para el matiz de Color).
    /// </summary>
    private (float mean, float stdDev) CalculateWeightedStats(List<AnimalData> roundData, float[] aptitudes, Func<AnimalData, float> selector)
    {
        float sumWeights = 0f;
        float weightedSum = 0f;

        for (int i = 0; i < roundData.Count; i++)
        {
            sumWeights += aptitudes[i];
            weightedSum += aptitudes[i] * selector(roundData[i]);
        }

        if (sumWeights <= 0f)
        {
            // Caso extremo: todas las aptitudes fueron 0 (todos eliminados en
            // el instante del spawn). Se cae a un promedio simple, sin ponderar.
            float simpleMean = 0f;
            for (int i = 0; i < roundData.Count; i++)
            {
                simpleMean += selector(roundData[i]);
            }
            simpleMean /= roundData.Count;
            return (simpleMean, 0f);
        }

        float mean = weightedSum / sumWeights;

        float weightedVarianceSum = 0f;
        for (int i = 0; i < roundData.Count; i++)
        {
            float diff = selector(roundData[i]) - mean;
            weightedVarianceSum += aptitudes[i] * diff * diff;
        }

        float variance = weightedVarianceSum / sumWeights;
        float stdDev = Mathf.Sqrt(variance);

        return (mean, stdDev);
    }

    /// <summary>
    /// Distribución de probabilidad por tipo de animal, proporcional a la
    /// aptitud acumulada de cada tipo. El orden del array coincide con el
    /// orden de declaración del enum AnimalType (el mismo que usa AnimalManager).
    /// </summary>
    private float[] CalculateTypeDistribution(List<AnimalData> roundData, float[] aptitudes)
    {
        var typeValues = (AnimalType[])Enum.GetValues(typeof(AnimalType));
        float[] accumulatedByType = new float[typeValues.Length];

        for (int i = 0; i < roundData.Count; i++)
        {
            int typeIndex = (int)roundData[i].Tipo_Animal;
            accumulatedByType[typeIndex] += aptitudes[i];
        }

        float total = 0f;
        foreach (float value in accumulatedByType)
        {
            total += value;
        }

        float[] probabilities = new float[typeValues.Length];

        if (total <= 0f)
        {
            // Caso extremo: aptitud acumulada 0 para todos los tipos.
            // Se cae a distribución uniforme en vez de dejar el array en ceros.
            float uniform = 1f / typeValues.Length;
            for (int i = 0; i < probabilities.Length; i++)
            {
                probabilities[i] = uniform;
            }
            return probabilities;
        }

        for (int i = 0; i < probabilities.Length; i++)
        {
            probabilities[i] = accumulatedByType[i] / total;
        }

        return probabilities;
    }

    /// <summary>Extrae el matiz (hue, 0-1) de un color RGB — el único valor numérico que AnimalManager usa al generar color.</summary>
    private float GetHue(Color color)
    {
        Color.RGBToHSV(color, out float hue, out _, out _);
        return hue;
    }
}