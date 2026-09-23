using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EvolutionCore — el algoritmo de aprendizaje del juego.
///
/// Es un algoritmo de estimación de distribución (EDA, Estimation of
/// Distribution Algorithm): en vez de mutar individuos al azar como un
/// algoritmo genético clásico, en cada ronda se re-estima la distribución de
/// probabilidad de la que se generan los siguientes animales, sesgada hacia
/// los atributos que mejor "sobrevivieron" (tardaron más en ser detectados).
/// La convergencia hacia un tipo de enemigo dominante ocurre sola, a medida
/// que la desviación estándar de esa distribución se reduce ronda a ronda —
/// sin necesidad de reglas de mutación forzada.
///
/// Flujo de trabajo (se ejecuta una vez por ronda, al recibir
/// DataCollector.OnRoundDataCollected):
///   1. Aptitud(animal) = Tiempo_Sobrevivido / Tiempo_Ronda            (0 a 1)
///   2. Media y desviación estándar de Tamaño y de Hue, PONDERADAS por
///      la aptitud de cada animal (attributes de animales "más difíciles
///      de detectar" pesan más en el promedio).
///   3. Distribución de probabilidad por Tipo de animal, proporcional a
///      la aptitud acumulada de cada tipo.
///   4. Los tres resultados se empaquetan en un SpawnParameters y se
///      entregan a AnimalManager para la siguiente ronda.
/// </summary>
public class EvolutionCore : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private DataCollector dataCollector;
    [SerializeField] private AnimalManager animalManager;

    [Header("Piso mínimo de desviación estándar")]
    [Tooltip("Evita que la desviación llegue exactamente a 0 por redondeo antes de tiempo, lo cual congelaría la variedad de golpe en vez de converger gradualmente.")]
    [SerializeField] private float minStdDevFloor = 0.01f;

    /// <summary>
    /// Último promedio ponderado de hue calculado (de la ronda anterior).
    /// Distinto de AnimalManager.CurrentRoundMeanHue: este está ponderado por
    /// aptitud y representa "hacia dónde está convergiendo" el sistema, no el
    /// promedio real de lo que hay en pantalla ahora mismo.
    /// </summary>
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

    /// <summary>
    /// Punto de entrada del algoritmo: procesa el lote completo de datos de
    /// la ronda que acaba de cerrar y actualiza los parámetros de spawn para
    /// la siguiente ronda. Ver el resumen de la clase para el flujo completo.
    /// </summary>
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

        // minSize/maxSize se conservan sin cambios: son límites de diseño del
        // juego, no atributos que deban evolucionar por aptitud.
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

    /// <summary>
    /// Paso 1 del algoritmo — Aptitud(animal) = Tiempo_Sobrevivido / Tiempo_Ronda.
    /// Un animal que sobrevivió toda la ronda (timeout) tiene aptitud 1.0
    /// (máxima); uno eliminado casi al spawnear tiene aptitud cercana a 0.
    /// </summary>
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
    /// Paso 2 del algoritmo — media y desviación estándar PONDERADAS por
    /// aptitud, para cualquier atributo numérico extraído con el selector
    /// dado (se reutiliza esta misma función para Tamaño y para el matiz de
    /// Color, pasando un selector distinto cada vez).
    ///
    /// Fórmulas (con wـi = aptitud del animal i, x_i = su atributo):
    ///   media = Σ(w_i · x_i) / Σ(w_i)
    ///   varianza = Σ(w_i · (x_i - media)²) / Σ(w_i)
    ///   desviación estándar = √varianza
    ///
    /// A medida que los animales "ganadores" (mayor aptitud) comparten
    /// valores parecidos de este atributo, la desviación estándar ponderada
    /// se reduce sola — esto es lo que produce la convergencia gradual del
    /// sistema, sin necesidad de reglas de mutación o reducción forzada.
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
            // el instante del spawn). Se cae a un promedio simple, sin ponderar,
            // para no dividir por 0.
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
    /// Paso 3 del algoritmo — distribución de probabilidad por tipo de
    /// animal, proporcional a la aptitud acumulada de cada tipo:
    ///   Prob(tipo) = Σ(aptitud de animales de ese tipo) / Σ(aptitud de todos)
    /// Si un tipo domina consistentemente en aptitud, su probabilidad tiende
    /// a 1 y las demás a 0 — el mismo efecto de convergencia gradual que en
    /// CalculateWeightedStats, pero aplicado a un atributo categórico en vez
    /// de numérico. El orden del array resultante coincide con el orden de
    /// declaración del enum AnimalType (el mismo que usa AnimalManager para
    /// interpretarlo).
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