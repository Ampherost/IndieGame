using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the player-phase / enemy-phase loop.
/// Tracks all units, resets their "acted" flag at the start of each phase,
/// and ends a phase automatically once every unit on that team has acted.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [Header("Registration")]
    [Tooltip("If true, finds all Units in the scene on Start. Otherwise register them manually.")]
    public bool autoRegisterUnitsOnStart = true;

    public Team CurrentPhase { get; private set; } = Team.Player;
    public int RoundNumber { get; private set; } = 1;

    // Fired when a new phase begins (Player or Enemy). Good hook for UI banners / AI.
    public event Action<Team> OnPhaseStart;
    // Fired when the round counter advances (both phases complete).
    public event Action<int> OnRoundStart;

    private readonly List<Unit> allUnits = new List<Unit>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (autoRegisterUnitsOnStart)
        {
            allUnits.Clear();
            allUnits.AddRange(FindObjectsByType<Unit>(FindObjectsSortMode.None));
        }
        BeginPhase(Team.Player);
    }

    public void RegisterUnit(Unit u)
    {
        if (!allUnits.Contains(u)) allUnits.Add(u);
    }

    public void UnregisterUnit(Unit u)
    {
        allUnits.Remove(u);
    }

    /// <summary>Call after a unit finishes its action (moved + acted, or waited).</summary>
    public void NotifyUnitActed(Unit u)
    {
        u.HasActed = true;
        if (AllUnitsActed(CurrentPhase))
            EndPhase();
    }

    public IReadOnlyList<Unit> AllUnits => allUnits;

    public IEnumerable<Unit> UnitsOnTeam(Team team)
    {
        foreach (var u in allUnits)
            if (u != null && u.IsAlive && u.team == team)
                yield return u;
    }

    private bool AllUnitsActed(Team team)
    {
        foreach (var u in allUnits)
            if (u != null && u.IsAlive && u.team == team && !u.HasActed)
                return false;
        return true;
    }

    private void BeginPhase(Team team)
    {
        CurrentPhase = team;

        // Reset the acting team's units so they can move again.
        foreach (var u in allUnits)
            if (u != null && u.IsAlive && u.team == team)
                u.HasActed = false;

        Debug.Log($"=== {team} Phase (Round {RoundNumber}) ===");
        OnPhaseStart?.Invoke(team);

        // If a phase begins with no living units on that team, skip it immediately.
        if (AllUnitsActed(team))
            EndPhase();
    }

    private void EndPhase()
    {
        if (CurrentPhase == Team.Player)
        {
            BeginPhase(Team.Enemy);
        }
        else
        {
            RoundNumber++;
            OnRoundStart?.Invoke(RoundNumber);
            BeginPhase(Team.Player);
        }
    }

    // ---- Win / loss ----

    public bool AnyAlive(Team team)
    {
        foreach (var u in UnitsOnTeam(team)) return true;
        return false;
    }
}
