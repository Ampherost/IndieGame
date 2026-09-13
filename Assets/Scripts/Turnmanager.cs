using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the player-phase / enemy-phase loop.
/// Tracks all units, resets their "acted" flag at the start of each phase,
/// and ends a phase automatically once every unit on that team has acted.
///
/// ROSTER: the scene sweep happens in Awake, not Start. Every Awake runs before any
/// Start, and no unit deactivates itself until its own Start, so an Awake snapshot is
/// both complete and free of ghosts. After that, units push their own registration
/// through OnEnable / OnDisable, so runtime reinforcements need no special handling.
///
/// FIRST PHASE: deferred by one frame so every Unit.Start() -> SnapToGrid() has resolved
/// before anyone is asked to act. CombatStarted gates input until then.
///
/// Phase advancement is iterative, not recursive: a phase where nobody can act
/// is skipped inside a loop, so an empty board can never blow the stack.
///
/// WIN CONDITIONS: CombatObjective components in the scene are evaluated first, so a
/// boss kill or an escort failure outranks the generic team-wipe rule.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [Header("Registration")]
    [Tooltip("If true, finds all Units in the scene on Awake. Otherwise register them manually.")]
    public bool autoRegisterUnitsOnStart = true;

    [Header("End Conditions")]
    [Tooltip("End the battle as soon as one team has no living units. Turn this off while " +
             "prototyping a scene that intentionally has only one team in it, or on a map " +
             "where a CombatObjective is the real win condition.")]
    public bool autoEndWhenTeamWipedOut = true;

    public Team CurrentPhase { get; private set; } = Team.Player;
    public int RoundNumber { get; private set; } = 1;

    /// <summary>False until the first phase begins. Input and end-checks stay parked until then.</summary>
    public bool CombatStarted { get; private set; }

    // Held through player movement and the attack-resolution pause.
    public bool IsPlayerActionInProgress { get; internal set; }

    public bool CanEndPlayerPhase => CombatStarted && !CombatOver &&
        CurrentPhase == Team.Player && !IsPlayerActionInProgress;

    /// <summary>True once the battle has resolved. No further phases will begin.</summary>
    public bool CombatOver { get; private set; }

    /// <summary>Winning team, or null for a draw (nobody left on either side).</summary>
    public Team? Winner { get; private set; }

    // Fired when a new phase begins (Player or Enemy). Good hook for UI banners / AI.
    public event Action<Team> OnPhaseStart;
    // Fired when the round counter advances (both phases complete).
    public event Action<int> OnRoundStart;
    // Fired when a unit dies, before end-of-battle checks run. Objectives, kill counters,
    // quest triggers and battle UI hang off this rather than off special cases in here.
    public event Action<Unit> OnUnitDied;
    // Fired once when the battle resolves. Argument is the winner, or null for a draw.
    public event Action<Team?> OnCombatEnd;

    // Dead units stay in this list as a record of the battle — CanStillAct filters them
    // out, so a corpse can never stall a phase, but objectives and after-action reports
    // can still ask about them.
    private readonly List<Unit> allUnits = new List<Unit>();

    private CombatObjective[] objectives = Array.Empty<CombatObjective>();

    // Guards against EndPhase being re-entered while a transition is already in flight.
    private bool resolvingPhaseChange;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (autoRegisterUnitsOnStart)
        {
            allUnits.Clear();
            allUnits.AddRange(FindObjectsByType<Unit>(FindObjectsSortMode.None));
        }

        objectives = FindObjectsByType<CombatObjective>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Array.Sort(objectives, (a, b) => a.priority.CompareTo(b.priority));
    }

    private IEnumerator Start()
    {
        // One frame of slack: every Unit.Start() -> SnapToGrid() resolves first, so units
        // that turn out to be unplaceable are already off the roster before the first
        // phase is announced.
        yield return null;

        CombatStarted = true;
        BeginPhase(Team.Player);
    }

    // ---- Roster ----

    public void RegisterUnit(Unit u)
    {
        if (u == null || allUnits.Contains(u)) return;
        allUnits.Add(u);
    }

    /// <summary>
    /// Drop a unit from the roster. Called by Unit.OnDisable for despawns, retreats and
    /// units that failed to place — NOT for battle deaths, which stay registered.
    ///
    /// Re-evaluates the phase afterward: the roster just shrank, so the battle may now be
    /// decided, or the acting team may have just lost the last unit that owed us an action.
    /// Without this the turn loop hangs forever.
    /// </summary>
    public void UnregisterUnit(Unit u)
    {
        if (u == null || !allUnits.Remove(u)) return;
        if (!CombatStarted || CombatOver) return;

        if (CheckCombatEnd()) return;
        if (AllUnitsActed(CurrentPhase)) EndPhase();
    }

    public IReadOnlyList<Unit> AllUnits => allUnits;

    public IEnumerable<Unit> UnitsOnTeam(Team team)
    {
        foreach (var u in allUnits)
            if (u != null && u.IsAlive && u.team == team)
                yield return u;
    }

    // ---- Acting ----

    /// <summary>Call after a unit finishes its action (moved + acted, or waited).</summary>
    public void NotifyUnitActed(Unit u)
    {
        if (CombatOver || u == null) return;

        // A coroutine mid-flight (e.g. EnemyPhaseController iterating a snapshot list) can
        // report after the phase has already flipped. Ignore those.
        if (u.team != CurrentPhase) return;

        u.HasActed = true;

        if (AllUnitsActed(CurrentPhase))
            EndPhase();
    }

    /// <summary>Call when a unit dies so objectives and the battle can resolve immediately.</summary>
    public void NotifyUnitDied(Unit u)
    {
        if (u == null || CombatOver) return;

        OnUnitDied?.Invoke(u);
        CheckCombatEnd();
    }

    /// <summary>
    /// Can this unit still owe us an action this phase? Off-grid units cannot: a unit that
    /// has been rescued, captured, loaded into a transport or otherwise pulled off the
    /// board is alive and registered but unselectable, and waiting on it deadlocks the phase.
    /// </summary>
    private static bool CanStillAct(Unit u, Team team)
    {
        return u != null && u.IsAlive && u.IsOnGrid && u.team == team && !u.HasActed;
    }

    private bool AllUnitsActed(Team team)
    {
        foreach (var u in allUnits)
            if (CanStillAct(u, team))
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
        // dead or off-board). Skip forward in a loop rather than recursing through
        // EndPhase, and cap the number of consecutive skips so an unactable board
        // terminates instead of ping-ponging forever.
        const int maxConsecutiveSkips = 4;   // two full rounds' worth of phases

        for (int skips = 0; ; skips++)
        {
            CurrentPhase = team;

            // Reset the acting team's units so they can move again. Deliberately broader
            // than CanStillAct: a unit that returns to the board mid-phase should find its
            // flag already cleared.
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

    /// <summary>
    /// Cut a phase short: mark every unit on 'team' as having acted, then hand over.
    /// Backs the End Turn button, so the player isn't forced to move every unit.
    /// </summary>
    public void EndPhaseEarly(Team team)
    {
        if (!CombatStarted || CombatOver || CurrentPhase != team) return;
        if (team == Team.Player && !CanEndPlayerPhase) return;

        foreach (var u in allUnits)
            if (u != null && u.IsAlive && u.team == team)
                u.HasActed = true;

        EndPhase();
    }

    // ---- Win / loss ----

    public bool AnyAlive(Team team)
    {
        foreach (var u in UnitsOnTeam(team)) return true;
        return false;
    }

    /// <summary>
    /// Ends the battle if any objective is met, or if either side has been wiped out.
    /// Returns true if combat is over (now or already). Safe to call at any time.
    /// </summary>
    public bool CheckCombatEnd()
    {
        if (CombatOver) return true;
        if (!CombatStarted) return false;

        // Explicit objectives outrank the generic wipe rule: a boss kill should win even
        // with enemy mooks still standing.
        foreach (var obj in objectives)
        {
            if (obj == null || !obj.isActiveAndEnabled) continue;

            if (obj.IsResolved(this, out Team? objWinner))
            {
                Debug.Log($"[TurnManager] Objective met — {obj.Describe()}.");
                EndCombat(objWinner);
                return true;
            }
        }

        if (!autoEndWhenTeamWipedOut) return false;

        bool playersAlive = AnyAlive(Team.Player);
        bool enemiesAlive = AnyAlive(Team.Enemy);

        if (playersAlive && enemiesAlive) return false;

        if (playersAlive) EndCombat(Team.Player);
        else if (enemiesAlive) EndCombat(Team.Enemy);
        else EndCombat(null);            // both sides gone -> draw

        return true;
    }

    /// <summary>Register an objective created at runtime (spawned reinforcement waves, etc).</summary>
    public void RegisterObjective(CombatObjective objective)
    {
        if (objective == null) return;
        foreach (var o in objectives)
            if (o == objective) return;

        var list = new List<CombatObjective>(objectives) { objective };
        list.Sort((a, b) => a.priority.CompareTo(b.priority));
        objectives = list.ToArray();
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
