using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the player-phase / enemy-phase loop.
/// Tracks all units, resets their "acted" flag at the start of each phase,
/// and ends a phase automatically once every unit on that team has acted.
///
/// Phase advancement is iterative, not recursive: a phase where nobody can act
/// is skipped inside a loop, so an empty board can never blow the stack.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [Header("Registration")]
    [Tooltip("If true, finds all Units in the scene on Start. Otherwise register them manually.")]
    public bool autoRegisterUnitsOnStart = true;

    [Header("End Conditions")]
    [Tooltip("End the battle as soon as one team has no living units. Turn this off while " +
             "prototyping a scene that intentionally has only one team in it.")]
    public bool autoEndWhenTeamWipedOut = true;

    public Team CurrentPhase { get; private set; } = Team.Player;
    public int RoundNumber { get; private set; } = 1;

    /// <summary>True once the battle has resolved. No further phases will begin.</summary>
    public bool CombatOver { get; private set; }

    /// <summary>Winning team, or null for a draw (nobody left on either side).</summary>
    public Team? Winner { get; private set; }

    // Fired when a new phase begins (Player or Enemy). Good hook for UI banners / AI.
    public event Action<Team> OnPhaseStart;
    // Fired when the round counter advances (both phases complete).
    public event Action<int> OnRoundStart;
    // Fired once when the battle resolves. Argument is the winner, or null for a draw.
    public event Action<Team?> OnCombatEnd;

    private readonly List<Unit> allUnits = new List<Unit>();

    // Guards against EndPhase being re-entered while a transition is already in flight.
    private bool resolvingPhaseChange;

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
        if (CombatOver || u == null) return;

        u.HasActed = true;

        if (AllUnitsActed(CurrentPhase))
            EndPhase();
    }

    /// <summary>Call when a unit dies so the battle can resolve immediately, mid-phase.</summary>
    public void NotifyUnitDied(Unit u)
    {
        CheckCombatEnd();
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

    // ---- Phase flow ----

    private void BeginPhase(Team team)
    {
        if (CombatOver) return;

        // Resolve the battle before handing control to anyone.
        if (CheckCombatEnd()) return;

        // A phase can legitimately have nobody able to act (every unit on that team is
        // dead). Skip forward in a loop rather than recursing through EndPhase, and cap
        // the number of consecutive skips so an unactable board terminates instead of
        // ping-ponging forever.
        const int maxConsecutiveSkips = 4;   // two full rounds' worth of phases

        for (int skips = 0; ; skips++)
        {
            CurrentPhase = team;

            // Reset the acting team's units so they can move again.
            foreach (var u in allUnits)
                if (u != null && u.IsAlive && u.team == team)
                    u.HasActed = false;

            if (!AllUnitsActed(team))
                break;   // somebody can act — this is a real phase

            if (skips >= maxConsecutiveSkips)
            {
                Debug.LogWarning("[TurnManager] No unit on either team can act. Ending combat.");
                EndCombat(null);
                return;
            }

            // Advance to the next phase without recursion.
            if (team == Team.Enemy)
            {
                RoundNumber++;
                OnRoundStart?.Invoke(RoundNumber);
            }
            team = (team == Team.Player) ? Team.Enemy : Team.Player;
        }

        // Clear the transition guard *before* announcing, so a listener that finishes the
        // phase synchronously can still trigger EndPhase.
        resolvingPhaseChange = false;

        Debug.Log($"=== {CurrentPhase} Phase (Round {RoundNumber}) ===");
        OnPhaseStart?.Invoke(CurrentPhase);
    }

    private void EndPhase()
    {
        if (CombatOver || resolvingPhaseChange) return;

        resolvingPhaseChange = true;

        Team next;
        if (CurrentPhase == Team.Player)
        {
            next = Team.Enemy;
        }
        else
        {
            RoundNumber++;
            OnRoundStart?.Invoke(RoundNumber);
            next = Team.Player;
        }

        BeginPhase(next);

        resolvingPhaseChange = false;   // in case BeginPhase returned early
    }

    // ---- Win / loss ----

    public bool AnyAlive(Team team)
    {
        foreach (var u in UnitsOnTeam(team)) return true;
        return false;
    }

    /// <summary>
    /// Ends the battle if either side has been wiped out.
    /// Returns true if combat is over (now or already). Safe to call at any time.
    /// </summary>
    public bool CheckCombatEnd()
    {
        if (CombatOver) return true;
        if (!autoEndWhenTeamWipedOut) return false;

        bool playersAlive = AnyAlive(Team.Player);
        bool enemiesAlive = AnyAlive(Team.Enemy);

        if (playersAlive && enemiesAlive) return false;

        if (playersAlive) EndCombat(Team.Player);
        else if (enemiesAlive) EndCombat(Team.Enemy);
        else EndCombat(null);            // both sides gone -> draw

        return true;
    }

    private void EndCombat(Team? winner)
    {
        if (CombatOver) return;

        CombatOver = true;
        Winner = winner;

        Debug.Log(winner.HasValue
            ? $"Combat over — {winner.Value} team wins on round {RoundNumber}."
            : $"Combat over — draw on round {RoundNumber}.");

        OnCombatEnd?.Invoke(winner);
    }
}
