using System;
using UnityEngine;

/// <summary>
/// Tipos de animal disponibles. Es un atributo evolutivo más (no solo estético):
/// EvolutionCore decide qué tipo generar según la aptitud acumulada por tipo.
/// </summary>
public enum AnimalType
{
    Conejo,
    Leon,
    Pato,
    Tortuga
}

/// <summary>
/// Datos de un animal ya resuelto (eliminado o sobreviviente), listos para que
/// DataCollector los guarde y EvolutionCore los procese al cierre de ronda.
/// Coincide con los campos definidos en el diagrama de Miro.
/// </summary>
[Serializable]
public class AnimalData
{
    public int ID;
    public Color Color;
    public float Tamaño;
    public AnimalType Tipo_Animal;
    public float Tiempo_Sobrevivido;
    public bool Fue_Eliminado;
}

/// <summary>
/// Representa un enemigo individual en escena. No sabe nada de aptitud ni de
/// generación de rondas: solo registra su propio ciclo de vida (spawn → click
/// o timeout) y notifica el resultado vía evento. DataCollector se suscribe
/// a OnAnimalResolved para recolectar los datos de la ronda.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class Animal : MonoBehaviour
{
    // DataCollector se suscribe a este evento estático para recibir el resultado
    // de CUALQUIER animal, sin necesidad de tener una referencia directa a cada uno.
    public static event Action<AnimalData> OnAnimalResolved;

    [SerializeField] private SpriteRenderer spriteRenderer;

    private int id;
    private Color colorValue;
    private float tamaño;
    private AnimalType tipoAnimal;
    private float spawnTime;
    private bool resolved; // evita registrar el mismo animal dos veces (click + timeout a la vez)

    private void Awake()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

    private void Start()
    {
        // Ver nota equivalente en AnimalManager.cs: se suscribe en Start(), no en
        // OnEnable(), para garantizar que GameManager.Instance ya exista.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundEnd += HandleRoundEnd;
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundEnd -= HandleRoundEnd;
        }
    }

    /// <summary>
    /// Llamado por AnimalManager justo después de instanciar el prefab, con los
    /// parámetros generados por EvolutionCore (o aleatorios en la Ronda 0).
    /// </summary>
    public void Initialize(int animalId, Color color, float size, AnimalType type)
    {
        id = animalId;
        colorValue = color;
        tamaño = size;
        tipoAnimal = type;
        spawnTime = Time.time;
        resolved = false;

        spriteRenderer.color = color;
        transform.localScale = Vector3.one * size;

        // TODO: cuando existan los sprites de conejo/león/pato/tortuga,
        // asignar aquí spriteRenderer.sprite según 'type'.
    }

    /// <summary>
    /// Detecta el click del jugador (requiere Collider2D en el mismo GameObject
    /// y una cámara con Physics2D Raycaster activo en la escena).
    /// </summary>
    private void OnMouseDown()
    {
        if (resolved) return;
        ResolveAsEliminated();
    }

    private void ResolveAsEliminated()
    {
        resolved = true;
        float survivalTime = Time.time - spawnTime;

        var data = new AnimalData
        {
            ID = id,
            Color = colorValue,
            Tamaño = tamaño,
            Tipo_Animal = tipoAnimal,
            Tiempo_Sobrevivido = survivalTime,
            Fue_Eliminado = true
        };

        OnAnimalResolved?.Invoke(data);
        Destroy(gameObject);
    }

    /// <summary>
    /// Si el animal sigue vivo cuando termina la ronda, se considera que sobrevivió
    /// toda la duración: Tiempo_Sobrevivido se fija en la duración de la ronda
    /// (no en el tiempo real transcurrido), tal como definiste.
    /// </summary>
    private void HandleRoundEnd(int roundNumber)
    {
        if (resolved) return;
        resolved = true;

        var data = new AnimalData
        {
            ID = id,
            Color = colorValue,
            Tamaño = tamaño,
            Tipo_Animal = tipoAnimal,
            Tiempo_Sobrevivido = GameManager.Instance != null ? GameManager.Instance.roundDuration : 10f,
            Fue_Eliminado = false
        };

        OnAnimalResolved?.Invoke(data);
        Destroy(gameObject);
    }
}