using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recolecta los AnimalData de todos los animales resueltos (por click o por
/// timeout) durante la ronda en curso, y los entrega en un solo lote cuando la
/// ronda cierra, vía el evento OnRoundDataCollected. EvolutionCore se suscribe
/// a ese evento para calcular aptitud y los nuevos parámetros de spawn.
/// </summary>
public class DataCollector : MonoBehaviour
{
    // EvolutionCore se suscribe aquí para recibir todos los datos de la ronda
    // que acaba de cerrar, ya listos para procesar.
    public event Action<int, List<AnimalData>> OnRoundDataCollected;

    private readonly List<AnimalData> currentRoundData = new List<AnimalData>();

    private void Start()
    {
        // Se suscribe en Start() por la misma razón que en AnimalManager/Animal:
        // Unity garantiza que todos los Awake() ya corrieron, así que
        // GameManager.Instance existe con seguridad sin importar el orden
        // en la jerarquía de la escena.
        Animal.OnAnimalResolved += HandleAnimalResolved;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundDataReady += HandleRoundDataReady;
        }
    }

    private void OnDisable()
    {
        Animal.OnAnimalResolved -= HandleAnimalResolved;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRoundDataReady -= HandleRoundDataReady;
        }
    }

    private void HandleAnimalResolved(AnimalData data)
    {
        currentRoundData.Add(data);
    }

    /// <summary>
    /// Se dispara únicamente después de que TODOS los animales de la ronda
    /// (eliminados por click o resueltos por timeout) ya notificaron su
    /// resultado — ver la nota en GameManager.OnRoundDataReady.
    /// </summary>
    private void HandleRoundDataReady(int roundNumber)
    {
        // Copia defensiva: quien reciba la lista no puede alterar el estado interno.
        var snapshot = new List<AnimalData>(currentRoundData);

        OnRoundDataCollected?.Invoke(roundNumber, snapshot);

        currentRoundData.Clear();
    }

    /// <summary>Cantidad de animales resueltos hasta ahora en la ronda en curso (útil para debug/HUD).</summary>
    public int CurrentRoundCount => currentRoundData.Count;
}