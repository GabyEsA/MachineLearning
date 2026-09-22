using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parámetros de generación para una ronda: media y desviación estándar de tamaño,
/// media y desviación estándar de matiz de color (hue), y distribución de
/// probabilidad por tipo de animal. EvolutionCore construye esta estructura al
/// cierre de cada ronda y se la pasa a AnimalManager vía UpdateSpawnParameters().
/// </summary>
[Serializable]
public struct SpawnParameters
{
    public float meanSize;
    public float stdDevSize;
    public float minSize;
    public float maxSize;

    public float meanHue; // 0-1
    public float stdDevHue;

    // Debe tener 4 elementos, en el mismo orden que el enum AnimalType,
    // y sumar 1 (o aproximadamente 1).
    public float[] typeProbabilities;
}

/// <summary>
/// Spawner de animales. Escucha OnRoundStart del GameManager y genera los animales
/// de la ronda usando los parámetros actuales (por defecto en Ronda 0, o los que
/// EvolutionCore haya calculado al cierre de la ronda anterior).
/// </summary>
public class AnimalManager : MonoBehaviour
{
    [Header("Prefab y cantidad")]
    [SerializeField] private Animal animalPrefab;
    [SerializeField] private int minAnimalsPerRound = 10;
    [SerializeField] private int maxAnimalsPerRound = 15;

    [Header("Área de spawn (coordenadas de mundo)")]
    [SerializeField] private Vector2 spawnAreaMin = new Vector2(-8f, -4f);
    [SerializeField] private Vector2 spawnAreaMax = new Vector2(8f, 4f);

    [Header("Parámetros iniciales (Ronda 0, sin historial previo)")]
    [SerializeField] private float initialMeanSize = 1f;
    [SerializeField] private float initialStdDevSize = 0.4f;
    [SerializeField] private float minSizeClamp = 0.3f;
    [SerializeField] private float maxSizeClamp = 2f;
    [SerializeField, Range(0f, 1f)] private float initialStdDevHue = 0.5f; // alta variedad inicial

    private SpawnParameters currentParams;
    private int nextId = 0;
    private readonly List<Animal> activeAnimals = new List<Animal>();

    private void Awake()
    {
        currentParams = CreateDefaultParameters();
    }

    private void Start()
    {
        // Se suscribe en Start() (no en OnEnable()) porque Unity garantiza que TODOS
        // los Awake() de la escena terminan antes de que se llame a CUALQUIER Start(),
        // así GameManager.Instance ya existe sin importar el orden en la jerarquía.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStart += HandleRoundStart;
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundStart -= HandleRoundStart;
        }
    }

    private SpawnParameters CreateDefaultParameters()
    {
        return new SpawnParameters
        {
            meanSize = initialMeanSize,
            stdDevSize = initialStdDevSize,
            minSize = minSizeClamp,
            maxSize = maxSizeClamp,
            meanHue = 0.5f,
            stdDevHue = initialStdDevHue,
            typeProbabilities = new float[] { 0.25f, 0.25f, 0.25f, 0.25f } // uniforme entre los 4 tipos
        };
    }

    /// <summary>
    /// Llamado por EvolutionCore al cierre de cada ronda (durante GameState.Adapting),
    /// con los parámetros recalculados a partir de la aptitud de la ronda anterior.
    /// </summary>
    public void UpdateSpawnParameters(SpawnParameters newParams)
    {
        currentParams = newParams;
    }

    private void HandleRoundStart(int roundNumber)
    {
        SpawnRound();
    }

    private void SpawnRound()
    {
        activeAnimals.Clear();
        int count = UnityEngine.Random.Range(minAnimalsPerRound, maxAnimalsPerRound + 1);

        for (int i = 0; i < count; i++)
        {
            SpawnSingleAnimal();
        }
    }

    private void SpawnSingleAnimal()
    {
        // NOTA: posición completamente aleatoria por ahora, sin chequeo de overlap.
        // Si en testing se ven muchos animales superpuestos, se puede refinar
        // con un sistema simple de grilla o de reintento por distancia mínima.
        Vector2 position = new Vector2(
            UnityEngine.Random.Range(spawnAreaMin.x, spawnAreaMax.x),
            UnityEngine.Random.Range(spawnAreaMin.y, spawnAreaMax.y)
        );

        Animal instance = Instantiate(animalPrefab, position, Quaternion.identity, transform);

        float size = SampleGaussianClamped(currentParams.meanSize, currentParams.stdDevSize, currentParams.minSize, currentParams.maxSize);
        Color color = SampleColor();
        AnimalType type = SampleType();

        instance.Initialize(nextId, color, size, type);
        nextId++;

        activeAnimals.Add(instance);
    }

    private Color SampleColor()
    {
        float hue = SampleGaussianClamped(currentParams.meanHue, currentParams.stdDevHue, 0f, 1f);
        return Color.HSVToRGB(hue, 0.8f, 0.9f);
    }

    private AnimalType SampleType()
    {
        var values = (AnimalType[])Enum.GetValues(typeof(AnimalType));

        if (currentParams.typeProbabilities == null || currentParams.typeProbabilities.Length != values.Length)
        {
            // Fallback de seguridad: si los parámetros vienen mal formados, tipo uniforme aleatorio.
            return values[UnityEngine.Random.Range(0, values.Length)];
        }

        float roll = UnityEngine.Random.value;
        float cumulative = 0f;

        for (int i = 0; i < currentParams.typeProbabilities.Length; i++)
        {
            cumulative += currentParams.typeProbabilities[i];
            if (roll <= cumulative)
            {
                return values[i];
            }
        }

        return values[values.Length - 1]; // fallback por redondeo flotante acumulado
    }

    /// <summary>
    /// Genera un valor con distribución gaussiana (transformación Box-Muller)
    /// y lo recorta al rango [min, max].
    /// </summary>
    private float SampleGaussianClamped(float mean, float stdDev, float min, float max)
    {
        float u1 = 1f - UnityEngine.Random.value;
        float u2 = 1f - UnityEngine.Random.value;
        float randStdNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        float value = mean + stdDev * randStdNormal;
        return Mathf.Clamp(value, min, max);
    }
}