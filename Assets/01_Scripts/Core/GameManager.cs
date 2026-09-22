using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// GameManager: controla el ciclo de rondas del juego (timer, estado, número de ronda).
/// No conoce directamente a AnimalManager, DataCollector ni EvolutionCore:
/// esos módulos se suscriben a los eventos de aquí (OnRoundStart, OnRoundEnd, OnTimerTick)
/// para mantener la arquitectura desacoplada.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Configuración de Ronda")]
    [SerializeField] public float roundDuration = 10f;
    [SerializeField] public float delayBetweenRounds = 2f;

    public enum GameState
    {
        WaitingToStart,
        RoundActive,
        RoundEnd,
        Adapting // el EvolutionCore está procesando datos y generando parámetros nuevos
    }

    public GameState CurrentState { get; private set; } = GameState.WaitingToStart;
    public int CurrentRound { get; private set; } = 0;
    public float TimeRemaining { get; private set; }

    // Expuesto para que otros scripts (ej. Animal.cs) no hardcodeen la duración de ronda.
    public float RoundDuration => roundDuration;

    // --- Eventos ---
    // AnimalManager se suscribe a OnRoundStart para hacer el spawn de la ronda.
    public event Action<int> OnRoundStart;

    // DataCollector y EvolutionCore se suscriben a OnRoundEnd para procesar
    // los datos de la ronda que acaba de terminar.
    public event Action<int> OnRoundEnd;

    // UI/HUD se suscribe a OnTimerTick para actualizar el contador visual.
    public event Action<float> OnTimerTick;

    // DataCollector se suscribe aquí (en vez de a OnRoundEnd) para finalizar y
    // entregar el lote de datos de la ronda: se dispara justo después de
    // OnRoundEnd, así que para este punto TODOS los animales (incluidos los
    // sobrevivientes, que también escuchan OnRoundEnd) ya se resolvieron.
    public event Action<int> OnRoundDataReady;

    private Coroutine roundLoopCoroutine;

    private void Awake()
    {
        // Singleton simple: si ya existe una instancia, esta se destruye.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        StartGame();
    }

    /// <summary>
    /// Inicia (o reinicia) el ciclo de rondas desde cero.
    /// </summary>
    public void StartGame()
    {
        if (roundLoopCoroutine != null)
        {
            StopCoroutine(roundLoopCoroutine);
        }

        CurrentRound = 0;
        roundLoopCoroutine = StartCoroutine(RoundLoop());
    }

    /// <summary>
    /// Loop infinito: corre una ronda, espera un breve delay, y repite.
    /// Es el "loop sin fin" definido en el diagrama de Miro.
    /// </summary>
    private IEnumerator RoundLoop()
    {
        while (true)
        {
            CurrentRound++;
            yield return StartCoroutine(RunRound());

            // Corto período de transición antes de iniciar la siguiente ronda
            // (tiempo para que EvolutionCore termine de calcular los nuevos parámetros).
            yield return new WaitForSeconds(delayBetweenRounds);
        }
    }

    /// <summary>
    /// Ejecuta una ronda individual: dispara el spawn, corre el timer,
    /// y al finalizar dispara el cierre de ronda.
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
        // suscriptores de forma síncrona (incluyendo cada Animal vivo, que
        // se resuelve como sobreviviente al recibir este mismo evento).
        // Por eso es seguro avisar aquí que los datos de la ronda están completos.
        OnRoundDataReady?.Invoke(CurrentRound);

        // A partir de aquí, EvolutionCore (suscrito a OnRoundDataReady) procesa
        // los datos y prepara los parámetros para la siguiente ronda.
        CurrentState = GameState.Adapting;
    }
}