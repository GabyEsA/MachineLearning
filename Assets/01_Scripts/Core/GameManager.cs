using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// GameManager — Dirige el ciclo del juego.
///
/// Responsabilidades:
///   - Llevar el timer y el número de ronda actual.
///   - Ejecutar el loop infinito de rondas (spawn → juego → cierre → adaptación → repetir).
///   - Llevar el puntaje de animales eliminados por click del jugador.
///
/// Patrón de comunicación: GameManager NO conoce a AnimalManager, DataCollector,
/// EvolutionCore ni HUD directamente. En su lugar, expone eventos públicos
/// (OnRoundStart, OnRoundEnd, OnRoundDataReady, OnTimerTick, OnScoreChanged) a
/// los que esos módulos se suscriben. Esto mantiene la arquitectura desacoplada:
/// se puede agregar o quitar un módulo sin tener que modificar GameManager.
///
/// La única dependencia "hacia adentro" es Animal.OnAnimalResolved, que este
/// script escucha para poder llevar el conteo de puntaje.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>Referencia global de acceso (patrón Singleton). Se asigna en Awake().</summary>
    public static GameManager Instance { get; private set; }

    [Header("Configuración de rondas")]
    [Tooltip("Duración de cada ronda.")]
    [SerializeField] private float roundDuration = 10f;

    [Tooltip("Tiempo de pausa entre las rondas.")]
    [SerializeField] private float delayBetweenRounds = 2f;

    /// <summary>Estados posibles del ciclo de una ronda.</summary>
    public enum GameState
    {
        /// <summary>Estado inicial, antes de que arranque la primera ronda.</summary>
        WaitingToStart,

        /// <summary>Ronda en curso: el timer corre y el jugador puede hacer click en los animales.</summary>
        RoundActive,

        /// <summary>El timer llegó a 0. Los animales sobrevivientes se resuelven como no-eliminados.</summary>
        RoundEnd,

        /// <summary>EvolutionCore está calculando aptitud y los nuevos parámetros de spawn para la siguiente ronda.</summary>
        Adapting
    }

    /// <summary>Estado actual del ciclo de juego. Ver <see cref="GameState"/>.</summary>
    public GameState CurrentState { get; private set; } = GameState.WaitingToStart;

    /// <summary>Número de la ronda actual. Empieza en 1 (se incrementa antes de correr cada ronda).</summary>
    public int CurrentRound { get; private set; } = 0;

    /// <summary>Segundos restantes de la ronda en curso. Llega a 0 al cerrar la ronda.</summary>
    public float TimeRemaining { get; private set; }

    /// <summary>Cantidad de animales eliminados por click del jugador en la partida actual.</summary>
    public int Score { get; private set; } = 0;

    /// <summary>
    /// Duración de ronda expuesta como propiedad de solo lectura, para que otros
    /// scripts (ej. Animal.cs, al resolver un sobreviviente) no tengan que
    /// hardcodear el valor y quede siempre sincronizado con el Inspector.
    /// </summary>
    public float RoundDuration => roundDuration;

    // ----------------------------------------------------------------------
    // Eventos: cada módulo del juego se suscribe solo a los que le interesan.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Se dispara al iniciar cada ronda, con el número de ronda.
    /// Suscriptor: AnimalManager (hace el spawn de la nueva tanda de animales).
    /// </summary>
    public event Action<int> OnRoundStart;

    /// <summary>
    /// Se dispara cuando el timer de la ronda llega a 0, con el número de ronda.
    /// Suscriptores: cada Animal vivo (se resuelve como sobreviviente/no-eliminado).
    /// </summary>
    public event Action<int> OnRoundEnd;

    /// <summary>
    /// Se dispara cada frame mientras la ronda está activa, con los segundos restantes.
    /// Suscriptor: HUD (actualiza el texto del timer en pantalla).
    /// </summary>
    public event Action<float> OnTimerTick;

    /// <summary>
    /// Se dispara inmediatamente después de OnRoundEnd, con el número de ronda.
    /// A diferencia de OnRoundEnd, para este punto TODOS los animales (incluidos
    /// los sobrevivientes que también escuchan OnRoundEnd) ya terminaron de
    /// resolverse — los eventos en C# se invocan de forma síncrona y bloquean
    /// hasta que todos sus suscriptores retornan, así que es seguro avisar aquí
    /// que el lote de datos de la ronda está completo.
    /// Suscriptor: DataCollector (finaliza y entrega el lote de datos de la ronda).
    /// </summary>
    public event Action<int> OnRoundDataReady;

    /// <summary>
    /// Se dispara cada vez que el puntaje cambia, con el nuevo valor.
    /// Suscriptor: HUD (actualiza el contador de puntaje en pantalla).
    /// </summary>
    public event Action<int> OnScoreChanged;

    private Coroutine roundLoopCoroutine;

    private void Awake()
    {
        // Patrón Singleton simple: si ya existe una instancia en la escena,
        // esta copia se destruye para evitar dos GameManager corriendo a la vez.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Se suscribe en Start() y no en OnEnable() a propósito: Unity garantiza
        // que TODOS los Awake() de la escena terminan antes de que se llame a
        // CUALQUIER Start(), así que para este punto cualquier otro objeto que
        // dependa de GameManager.Instance ya lo tiene disponible, sin importar
        // el orden en la jerarquía.
        Animal.OnAnimalResolved += HandleAnimalResolved;

        StartGame();
    }

    private void OnDisable()
    {
        Animal.OnAnimalResolved -= HandleAnimalResolved;
    }

    /// <summary>
    /// Suma un punto solo si el animal fue eliminado por click del jugador
    /// (Fue_Eliminado = true). Un animal que sobrevivió por timeout no suma ni resta.
    /// </summary>
    private void HandleAnimalResolved(AnimalData data)
    {
        if (data.Fue_Eliminado)
        {
            Score++;
            OnScoreChanged?.Invoke(Score);
        }
    }

    /// <summary>
    /// Inicia (o reinicia desde cero) el ciclo de rondas: resetea ronda y
    /// puntaje, y arranca el loop infinito de RoundLoop().
    /// </summary>
    public void StartGame()
    {
        if (roundLoopCoroutine != null)
        {
            StopCoroutine(roundLoopCoroutine);
        }

        CurrentRound = 0;
        Score = 0;
        OnScoreChanged?.Invoke(Score);
        roundLoopCoroutine = StartCoroutine(RoundLoop());
    }

    /// <summary>
    /// Loop infinito de rondas: corre una ronda completa (RunRound), espera un
    /// breve delay de transición, y repite indefinidamente. Este es el "loop
    /// sin fin" definido en el diagrama de flujo del juego (Miro): el objetivo
    /// es poder observar la evolución de los animales a lo largo de muchas rondas.
    /// </summary>
    private IEnumerator RoundLoop()
    {
        while (true)
        {
            CurrentRound++;
            yield return StartCoroutine(RunRound());

            // Pausa de transición: le da tiempo a EvolutionCore de terminar de
            // calcular los nuevos parámetros antes de que arranque la siguiente ronda.
            yield return new WaitForSeconds(delayBetweenRounds);
        }
    }

    /// <summary>
    /// Ejecuta una ronda individual completa:
    ///   1. Avisa que la ronda empieza (OnRoundStart) → AnimalManager spawnea.
    ///   2. Corre el timer frame a frame (OnTimerTick) hasta llegar a 0.
    ///   3. Avisa que la ronda cerró (OnRoundEnd) → los animales sobrevivientes se resuelven.
    ///   4. Avisa que los datos ya están completos (OnRoundDataReady) → DataCollector entrega el lote.
    /// </summary>
    private IEnumerator RunRound()
    {
        CurrentState = GameState.RoundActive;
        TimeRemaining = roundDuration;

        OnRoundStart?.Invoke(CurrentRound);

        while (TimeRemaining > 0f)
        {
            TimeRemaining -= Time.deltaTime;
            OnTimerTick?.Invoke(Mathf.Max(TimeRemaining, 0f));
            yield return null;
        }

        TimeRemaining = 0f;
        CurrentState = GameState.RoundEnd;
        OnRoundEnd?.Invoke(CurrentRound);

        // Para este punto, OnRoundEnd ya terminó de propagarse a TODOS sus
        // suscriptores de forma síncrona (incluyendo cada Animal vivo, que se
        // resuelve como sobreviviente al recibir este mismo evento). Por eso
        // es seguro avisar aquí que los datos de la ronda están completos.
        OnRoundDataReady?.Invoke(CurrentRound);

        // A partir de aquí, EvolutionCore (suscrito a OnRoundDataReady) procesa
        // los datos y prepara los parámetros de spawn para la siguiente ronda.
        CurrentState = GameState.Adapting;
    }
}