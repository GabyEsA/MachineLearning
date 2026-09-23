using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parámetros de generación de una ronda: media y desviación estándar de
/// tamaño, media y desviación estándar de matiz de color (hue), límites de
/// tamaño, y distribución de probabilidad por tipo de animal.
///
/// EvolutionCore construye esta estructura al cierre de cada ronda (a partir
/// de la aptitud calculada) y se la entrega a AnimalManager vía
/// UpdateSpawnParameters(). En la Ronda 0, sin historial previo, AnimalManager
/// construye una versión por defecto (ver CreateDefaultParameters()).
/// </summary>
[Serializable]
public struct SpawnParameters
{
    /// <summary>Tamaño promedio a generar. El resultado real varía según stdDevSize (distribución gaussiana).</summary>
    public float meanSize;

    /// <summary>Qué tanta variedad de tamaño hay alrededor de meanSize. Tiende a 0 a medida que el sistema converge.</summary>
    public float stdDevSize;

    /// <summary>Límite inferior de tamaño (no evoluciona; es una restricción de diseño del juego).</summary>
    public float minSize;

    /// <summary>Límite superior de tamaño (no evoluciona; es una restricción de diseño del juego).</summary>
    public float maxSize;

    /// <summary>Matiz de color (hue, 0-1) promedio a generar.</summary>
    public float meanHue;

    /// <summary>Qué tanta variedad de color hay alrededor de meanHue. Tiende a 0 a medida que el sistema converge.</summary>
    public float stdDevHue;

    /// <summary>
    /// Probabilidad de generar cada tipo de animal. Debe tener exactamente 4
    /// elementos, en el mismo orden que el enum AnimalType (Conejo, Leon,
    /// Pato, Tortuga), y sumar 1 (o aproximadamente 1).
    /// </summary>
    public float[] typeProbabilities;
}

/// <summary>
/// Spawner de animales — el único módulo que efectivamente instancia el
/// prefab de Animal en escena. Escucha GameManager.OnRoundStart y genera
/// entre minAnimalsPerRound y maxAnimalsPerRound animales, usando los
/// parámetros actuales (SpawnParameters): por defecto en la Ronda 0, o los
/// que EvolutionCore haya calculado al cierre de la ronda anterior.
///
/// También se encarga de evitar que los animales aparezcan superpuestos,
/// mediante spawn con rechazo por distancia mínima (ver FindValidPosition).
/// </summary>
public class AnimalManager : MonoBehaviour
{
    [Header("Prefab y cantidad")]
    [Tooltip("Prefab de Animal a instanciar (debe tener Animal.cs, SpriteRenderer y PolygonCollider2D).")]
    [SerializeField] private Animal animalPrefab;
    [SerializeField] private int minAnimalsPerRound = 10;
    [SerializeField] private int maxAnimalsPerRound = 15;

    [Header("Área de spawn (coordenadas de mundo)")]
    [Tooltip("Esquina inferior izquierda del rectángulo donde pueden aparecer animales. Debe coincidir con lo visible por la Main Camera.")]
    [SerializeField] private Vector2 spawnAreaMin = new Vector2(-8f, -4f);
    [Tooltip("Esquina superior derecha del rectángulo donde pueden aparecer animales.")]
    [SerializeField] private Vector2 spawnAreaMax = new Vector2(8f, 4f);

    [Header("Prevención de overlap entre animales")]
    [Tooltip("Radio aproximado de un animal con tamaño=1, en unidades de Unity. Ajusta este valor según el tamaño real de tus sprites importados.")]
    [SerializeField] private float baseSpriteRadius = 0.5f;
    [Tooltip("Espacio extra entre animales, además de la suma de sus radios (1 = sin espacio extra, 1.2 = 20% más de separación).")]
    [SerializeField] private float spacingPadding = 1.15f;
    [Tooltip("Intentos máximos para encontrar una posición válida antes de colocar el animal igual, aunque quede algo de overlap.")]
    [SerializeField] private int maxPlacementAttempts = 30;

    [Header("Parámetros iniciales (Ronda 0, sin historial previo)")]
    [SerializeField] private float initialMeanSize = 1f;
    [SerializeField] private float initialStdDevSize = 0.4f;
    [Tooltip("Límite inferior de tamaño permitido durante toda la partida.")]
    [SerializeField] private float minSizeClamp = 0.3f;
    [Tooltip("Límite superior de tamaño permitido durante toda la partida.")]
    [SerializeField] private float maxSizeClamp = 2f;
    [Tooltip("Alta variedad inicial de color, para que haya suficiente diversidad de la que 'aprender' en las primeras rondas.")]
    [SerializeField, Range(0f, 1f)] private float initialStdDevHue = 0.5f;

    /// <summary>Parámetros de generación vigentes para la ronda actual/siguiente.</summary>
    private SpawnParameters currentParams;

    /// <summary>Contador incremental para asignar un AnimalData.ID único a cada animal generado en toda la partida.</summary>
    private int nextId = 0;

    /// <summary>Animales actualmente instanciados en la ronda en curso.</summary>
    private readonly List<Animal> activeAnimals = new List<Animal>();

    /// <summary>Posición y tamaño de cada animal ya colocado en la ronda actual, usado para el chequeo de overlap.</summary>
    private readonly List<(Vector2 position, float size)> placedThisRound = new List<(Vector2, float)>();

    /// <summary>Hue de cada animal generado en la ronda actual, usado para calcular CurrentRoundMeanHue.</summary>
    private readonly List<float> currentRoundHues = new List<float>();

    /// <summary>
    /// Promedio circular (no aritmético simple) del hue de todos los animales
    /// generados en la ronda actual. El HUD lee esto para mostrar el % de
    /// parecido al color de fondo — a diferencia de EvolutionCore.LastMeanHue
    /// (que está ponderado por aptitud y corresponde a la ronda anterior),
    /// este es el promedio real y sin ponderar de lo que el jugador ve ahora.
    /// </summary>
    public float CurrentRoundMeanHue => HueUtils.CircularMean(currentRoundHues);

    /// <summary>
    /// Parámetros de spawn vigentes. EvolutionCore lee esto para conservar
    /// minSize/maxSize (valores que no evolucionan) al construir los
    /// SpawnParameters de la siguiente ronda.
    /// </summary>
    public SpawnParameters CurrentParameters => currentParams;

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

    /// <summary>
    /// Construye los parámetros usados en la Ronda 0, cuando todavía no hay
    /// ningún historial de aptitud: tamaño y color centrados con alta
    /// variedad, y distribución de tipo uniforme (25% cada uno).
    /// </summary>
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
    /// Reemplaza los parámetros de generación vigentes. Llamado por
    /// EvolutionCore al cierre de cada ronda (durante GameState.Adapting),
    /// con los valores recalculados a partir de la aptitud de la ronda anterior.
    /// </summary>
    public void UpdateSpawnParameters(SpawnParameters newParams)
    {
        currentParams = newParams;
    }

    private void HandleRoundStart(int roundNumber)
    {
        SpawnRound();
    }

    /// <summary>
    /// Genera la tanda completa de animales de una ronda: limpia el estado de
    /// la ronda anterior y crea una cantidad aleatoria (entre
    /// minAnimalsPerRound y maxAnimalsPerRound) de animales nuevos.
    /// </summary>
    private void SpawnRound()
    {
        activeAnimals.Clear();
        placedThisRound.Clear();
        currentRoundHues.Clear();
        int count = UnityEngine.Random.Range(minAnimalsPerRound, maxAnimalsPerRound + 1);

        for (int i = 0; i < count; i++)
        {
            SpawnSingleAnimal();
        }
    }

    /// <summary>
    /// Genera un animal individual: samplea tamaño, color (hue) y tipo a
    /// partir de currentParams, busca una posición sin overlap, lo instancia
    /// y lo inicializa. El orden importa: el tamaño se samplea ANTES de
    /// buscar posición, porque el chequeo de overlap necesita saber qué tan
    /// grande va a ser el animal para calcular la distancia mínima requerida.
    /// </summary>
    private void SpawnSingleAnimal()
    {
        float size = SampleGaussianClamped(currentParams.meanSize, currentParams.stdDevSize, currentParams.minSize, currentParams.maxSize);
        float hue = SampleGaussianClamped(currentParams.meanHue, currentParams.stdDevHue, 0f, 1f);
        Color color = Color.HSVToRGB(hue, 0.8f, 0.9f);
        AnimalType type = SampleType();

        currentRoundHues.Add(hue);

        Vector2 position = FindValidPosition(size);

        Animal instance = Instantiate(animalPrefab, position, Quaternion.identity, transform);
        instance.Initialize(nextId, color, size, type);
        nextId++;

        activeAnimals.Add(instance);
        placedThisRound.Add((position, size));
    }

    /// <summary>
    /// Busca, por rechazo (rejection sampling), una posición aleatoria dentro del
    /// área de spawn que no se superponga con los animales ya colocados en esta
    /// misma ronda. Si no encuentra ninguna válida tras maxPlacementAttempts,
    /// devuelve el último candidato igual, para garantizar que siempre se
    /// complete la cantidad de animales de la ronda (con algo de overlap como
    /// último recurso, en vez de dejar animales sin spawnear).
    /// </summary>
    private Vector2 FindValidPosition(float size)
    {
        Vector2 candidate = Vector2.zero;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            candidate = new Vector2(
                UnityEngine.Random.Range(spawnAreaMin.x, spawnAreaMax.x),
                UnityEngine.Random.Range(spawnAreaMin.y, spawnAreaMax.y)
            );

            if (IsPositionValid(candidate, size))
            {
                return candidate;
            }
        }

        // No se encontró una posición completamente libre de overlap: se usa el
        // último candidato de todas formas, priorizando completar la ronda.
        return candidate;
    }

    /// <summary>
    /// Determina si una posición candidata respeta la distancia mínima
    /// (radio del nuevo animal + radio del existente, con margen
    /// spacingPadding) contra CADA animal ya colocado en esta ronda.
    /// </summary>
    private bool IsPositionValid(Vector2 candidate, float size)
    {
        float radiusNew = size * baseSpriteRadius;

        foreach (var (existingPosition, existingSize) in placedThisRound)
        {
            float radiusExisting = existingSize * baseSpriteRadius;
            float minDistance = (radiusNew + radiusExisting) * spacingPadding;

            if (Vector2.Distance(candidate, existingPosition) < minDistance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Elige un AnimalType al azar, ponderado por currentParams.typeProbabilities
    /// (selección por ruleta: se recorre el array acumulando probabilidades
    /// hasta que un número aleatorio [0,1) cae dentro del rango de un tipo).
    /// </summary>
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
    /// Genera un valor con distribución gaussiana (transformación Box-Muller,
    /// ya que Unity no trae un generador de números normales nativo) y lo
    /// recorta al rango [min, max].
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