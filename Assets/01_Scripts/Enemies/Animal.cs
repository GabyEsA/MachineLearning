using System;
using System.Collections.Generic;
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
[RequireComponent(typeof(PolygonCollider2D))]
public class Animal : MonoBehaviour
{
    // DataCollector se suscribe a este evento estático para recibir el resultado
    // de CUALQUIER animal, sin necesidad de tener una referencia directa a cada uno.
    public static event Action<AnimalData> OnAnimalResolved;

    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private PolygonCollider2D polygonCollider;

    [Header("Sprites por tipo de animal")]
    [SerializeField] private Sprite conejoSprite;
    [SerializeField] private Sprite leonSprite;
    [SerializeField] private Sprite patoSprite;
    [SerializeField] private Sprite tortugaSprite;

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

        if (polygonCollider == null)
        {
            polygonCollider = GetComponent<PolygonCollider2D>();
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
        spriteRenderer.sprite = GetSpriteForType(type);
        UpdateColliderToMatchSprite();
    }

    /// <summary>
    /// Regenera la forma del PolygonCollider2D a partir del contorno real del
    /// sprite asignado. Es necesario porque Unity NO actualiza el collider
    /// automáticamente cuando el sprite se cambia por código en runtime.
    /// Requiere que el sprite tenga "Generate Physics Shape" activado en su
    /// configuración de importación (Inspector del archivo .png en Unity).
    /// </summary>
    private void UpdateColliderToMatchSprite()
    {
        Sprite sprite = spriteRenderer.sprite;
        if (polygonCollider == null || sprite == null)
        {
            return;
        }

        int shapeCount = sprite.GetPhysicsShapeCount();
        if (shapeCount == 0)
        {
            Debug.LogWarning($"Animal: el sprite '{sprite.name}' no tiene Physics Shape generado. " +
                              "Actívalo en su configuración de importación (Generate Physics Shape) para que la hitbox funcione.");
            return;
        }

        var path = new List<Vector2>();
        polygonCollider.pathCount = shapeCount;

        for (int i = 0; i < shapeCount; i++)
        {
            path.Clear();
            sprite.GetPhysicsShape(i, path);
            polygonCollider.SetPath(i, path);
        }
    }

    /// <summary>
    /// Devuelve el sprite correspondiente al tipo de animal. El color y el
    /// contorno del sprite se mantienen fijos en el arte; solo el relleno
    /// blanco es el que recibe el tinte de spriteRenderer.color.
    /// </summary>
    private Sprite GetSpriteForType(AnimalType type)
    {
        Sprite result = type switch
        {
            AnimalType.Conejo => conejoSprite,
            AnimalType.Leon => leonSprite,
            AnimalType.Pato => patoSprite,
            AnimalType.Tortuga => tortugaSprite,
            _ => null
        };

        if (result == null)
        {
            Debug.LogWarning($"Animal: no hay sprite asignado para el tipo {type} en el prefab.");
        }

        return result;
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
            Tiempo_Sobrevivido = GameManager.Instance != null ? GameManager.Instance.RoundDuration : 10f,
            Fue_Eliminado = false
        };

        OnAnimalResolved?.Invoke(data);
        Destroy(gameObject);
    }
}