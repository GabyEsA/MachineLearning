using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tipos de animal disponibles. No es solo una skin estética: EvolutionCore lo
/// trata como un atributo evolutivo más, decidiendo qué tipo generar según la
/// aptitud acumulada de cada uno en rondas anteriores (ver EvolutionCore.cs).
/// El orden de declaración importa: se usa como índice (0-3) al construir y
/// leer arrays de probabilidad por tipo (SpawnParameters.typeProbabilities).
/// </summary>
public enum AnimalType
{
    Conejo,
    Leon,
    Pato,
    Tortuga
}

/// <summary>
/// Snapshot de los datos de un animal ya resuelto (eliminado por click o
/// sobreviviente por timeout), listo para que DataCollector lo guarde y
/// EvolutionCore lo procese al cierre de la ronda. Los nombres de campo
/// coinciden a propósito con los definidos en el diagrama de datos de Miro.
/// </summary>
[Serializable]
public class AnimalData
{
    /// <summary>Identificador único del animal dentro de la partida (asignado por AnimalManager).</summary>
    public int ID;

    /// <summary>Color RGB con el que se generó el animal (su hue es el atributo evolutivo real; ver AnimalManager).</summary>
    public Color Color;

    /// <summary>Tamaño (factor de escala) con el que se generó el animal.</summary>
    public float Tamaño;

    /// <summary>Tipo de animal (conejo/león/pato/tortuga) con el que se generó.</summary>
    public AnimalType Tipo_Animal;

    /// <summary>
    /// Segundos que el animal estuvo "vivo" antes de resolverse. Si fue
    /// eliminado por click, es el tiempo real transcurrido desde el spawn.
    /// Si sobrevivió toda la ronda (timeout), se fija en la duración total
    /// de la ronda (GameManager.RoundDuration), no en un tiempo real distinto.
    /// </summary>
    public float Tiempo_Sobrevivido;

    /// <summary>true si el jugador lo eliminó con click; false si sobrevivió toda la ronda.</summary>
    public bool Fue_Eliminado;
}

/// <summary>
/// Representa un enemigo individual en escena — el objeto que el jugador ve y
/// clickea. No sabe nada de aptitud, estadísticas ni generación de rondas:
/// su única responsabilidad es registrar su propio ciclo de vida (spawn →
/// click o timeout) y notificar el resultado vía el evento estático
/// OnAnimalResolved. DataCollector se suscribe a ese evento para recolectar
/// los datos de todos los animales de la ronda.
///
/// Ciclo de vida típico:
///   1. AnimalManager lo instancia desde el prefab y llama a Initialize().
///   2. El jugador hace click (OnMouseDown → ResolveAsEliminated) O la ronda
///      termina antes de que lo clickeen (GameManager.OnRoundEnd → HandleRoundEnd).
///   3. En cualquiera de los dos casos, se dispara OnAnimalResolved con el
///      resultado, y el GameObject se destruye.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class Animal : MonoBehaviour
{
    /// <summary>
    /// Evento ESTÁTICO: se dispara cuando CUALQUIER animal (de cualquier
    /// instancia) se resuelve, sea por click o por timeout. Es estático a
    /// propósito, para que DataCollector pueda suscribirse una sola vez y
    /// recibir el resultado de todos los animales sin tener que guardar una
    /// referencia individual a cada uno.
    /// </summary>
    public static event Action<AnimalData> OnAnimalResolved;

    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private PolygonCollider2D polygonCollider;

    [Header("Sprites por tipo de animal")]
    [Tooltip("Se asigna automáticamente en Initialize() según el AnimalType recibido.")]
    [SerializeField] private Sprite conejoSprite;
    [SerializeField] private Sprite leonSprite;
    [SerializeField] private Sprite patoSprite;
    [SerializeField] private Sprite tortugaSprite;

    // --- Estado interno, fijado por Initialize() y usado al resolver el animal ---
    private int id;
    private Color colorValue;
    private float tamaño;
    private AnimalType tipoAnimal;
    private float spawnTime;

    /// <summary>
    /// Evita que un mismo animal se registre dos veces (por ejemplo, si el
    /// jugador hace click justo en el mismo frame en que la ronda termina:
    /// solo el primero de los dos caminos —click o timeout— debe contar).
    /// </summary>
    private bool resolved;

    private void Awake()
    {
        // Fallback de seguridad: si no se asignó manualmente en el Inspector,
        // se busca el componente en el mismo GameObject.
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
        // Se suscribe en Start() y no en OnEnable(): ver la nota detallada en
        // GameManager.Start() sobre por qué esto garantiza que
        // GameManager.Instance ya exista, sin importar el orden de la jerarquía.
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
    /// Configura este animal recién instanciado con los parámetros que le
    /// tocaron (generados por EvolutionCore, o aleatorios en la Ronda 0).
    /// Llamado por AnimalManager justo después de Instantiate().
    /// </summary>
    /// <param name="animalId">Identificador único dentro de la partida.</param>
    /// <param name="color">Color (tinte) a aplicar sobre el sprite.</param>
    /// <param name="size">Factor de escala del animal.</param>
    /// <param name="type">Tipo de animal, define qué sprite se usa.</param>
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
    /// sprite recién asignado, para que la hitbox coincida con la silueta
    /// visible del animal (y no con la del sprite anterior). Es necesario
    /// porque Unity NO actualiza el collider automáticamente cuando el sprite
    /// se cambia por código en runtime — hay que hacerlo a mano.
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
    /// Devuelve el sprite correspondiente al tipo de animal recibido. El
    /// contorno negro del sprite es fijo (parte del arte); solo el relleno
    /// blanco interior es el que recibe el tinte de spriteRenderer.color al
    /// aplicarse en Initialize() — así el color queda dentro de la silueta,
    /// pero el contorno siempre se distingue.
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
    /// Detecta el click del jugador sobre este animal. Requiere un Collider2D
    /// en el mismo GameObject (aquí, el PolygonCollider2D) — Unity dispara
    /// OnMouseDown automáticamente al hacer click sobre un collider 2D bajo
    /// la Main Camera, sin necesidad de EventSystem ni Physics2D Raycaster.
    /// </summary>
    private void OnMouseDown()
    {
        if (resolved) return;
        ResolveAsEliminated();
    }

    /// <summary>
    /// Cierra el ciclo de vida de este animal como "eliminado por el jugador":
    /// calcula el tiempo real de supervivencia, notifica el resultado vía
    /// OnAnimalResolved, y destruye el GameObject.
    /// </summary>
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
    /// Se ejecuta cuando GameManager avisa que la ronda terminó (OnRoundEnd).
    /// Si este animal seguía vivo (no fue clickeado a tiempo), se considera
    /// que sobrevivió toda la ronda: Tiempo_Sobrevivido se fija en la
    /// duración total de la ronda (GameManager.RoundDuration) — no en el
    /// tiempo real transcurrido — tal como se definió al diseñar el sistema.
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