using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy-phase AI. For each enemy, in order:
///   1. Find the nearest living player unit (by walkable path distance).
///   2. If already in attack range, attack it.
///   3. Otherwise, move to the reachable tile that gets closest to that target,
///      then attack if the new position puts a player in range.
///
/// Distances come from GridManager BFS distance fields, not straight-line math, so
/// walls and L-shaped maps are respected: an enemy walks around a barrier instead of
/// pressing itself against the near side of it forever.
///
/// One exception, deliberately: attack RANGE is still Manhattan, because that's what
/// Unit.DistanceTo / Unit.CanAttack use. Attacks reach over walls; movement does not.
///
/// SUBSCRIPTION: TurnManager sets its singleton in Awake and does not announce the first
/// phase until a frame after Start, so subscribing in Start is always in time. The old
/// wait-a-frame coroutine existed only to dodge an ordering assumption that no longer
/// holds.
/// </summary>
public class EnemyPhaseController : MonoBehaviour
{
    [Tooltip("Seconds between each enemy's action, so the phase is readable.")]
    public float actionDelay = 0.35f;

    [Tooltip("Brief pause after an attack resolves.")]
    public float attackPause = 0.4f;

    // Scores for cells with no walkable route to the target. Any genuinely reachable cell
    // beats every unreachable one, but unreachable cells stay ordered by straight-line
    // distance so a blocked enemy still drifts the right way instead of freezing.
    private const int UnreachablePenalty = 1000000;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    // Start() runs once; OnEnable can run again after a manual disable/enable cycle.
    private bool started;

    private void Start()
    {
        started = true;
        Subscribe();
    }

    private void OnEnable()
    {
        if (started) Subscribe();   // re-enable; the first subscribe happens in Start
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (TurnManager.Instance == null)
        {
            Debug.LogError("[EnemyPhaseController] No TurnManager in the scene — enemies " +
                           "will never take a turn.", this);
            return;
        }

        // Remove first so a double subscribe can't queue the phase twice.
        TurnManager.Instance.OnPhaseStart -= HandlePhaseStart;
        TurnManager.Instance.OnPhaseStart += HandlePhaseStart;
    }

    private void Unsubscribe()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnPhaseStart -= HandlePhaseStart;
    }

    private void HandlePhaseStart(Team team)
    {
        if (team == Team.Enemy)
            StartCoroutine(RunEnemyPhase());
    }

    private IEnumerator RunEnemyPhase()
    {
        var enemies = new List<Unit>(TurnManager.Instance.UnitsOnTeam(Team.Enemy));

        foreach (var enemy in enemies)
        {
            // A player unit may have fallen mid-phase, ending the battle.
            if (TurnManager.Instance.CombatOver) yield break;

            // Skip anything that died, despawned, or was lifted off the board earlier in
            // this same phase. TurnManager no longer waits on off-grid units either.
            if (enemy == null || !enemy.IsAlive || !enemy.IsOnGrid) continue;

            yield return StartCoroutine(TakeEnemyTurn(enemy));
            yield return new WaitForSeconds(actionDelay);

            if (enemy != null)
                TurnManager.Instance.NotifyUnitActed(enemy);
        }
    }

    private IEnumerator TakeEnemyTurn(Unit enemy)
    {
        Unit target = FindNearestPlayer(enemy);
        if (target == null)
        {
            Debug.Log($"{enemy.unitName} finds no target and waits.");
            yield break;
        }

        // Already in range? Attack without moving.
        if (enemy.CanAttack(target))
        {
            yield return StartCoroutine(DoAttack(enemy, target));
            yield break;
        }

        // Otherwise, close the distance.
        Vector2Int destination = FindBestApproachCell(enemy, target);
        List<Vector2Int> path = GridManager.Instance.GetPath(
            enemy.Cell, destination, enemy.moveRange, enemy);
        if (path != null && path.Count > 0)
            yield return StartCoroutine(enemy.MoveAlong(path));

        // After moving, attack if a player is now in range (target may have moved
        // in a prior enemy's turn, so re-scan rather than assuming the same one).
        Unit reachableTarget = FindAttackableFrom(enemy);
        if (reachableTarget != null)
            yield return StartCoroutine(DoAttack(enemy, reachableTarget));
    }

    private IEnumerator DoAttack(Unit attacker, Unit target)
    {
        string log = attacker.Attack(target);
        Debug.Log(log);

        // Optional battle text:
        // if (DialogueManager.Instance != null)
        //     DialogueManager.Instance.ShowDialogue(log.Split('\n'));

        yield return new WaitForSeconds(attackPause);
    }

    // ---- Targeting ----

    /// <summary>
    /// Nearest player by walkable path length, so an enemy commits to the player it can
    /// actually get to rather than one two tiles away on the far side of a wall.
    /// </summary>
    private Unit FindNearestPlayer(Unit enemy)
    {
        // One uncapped flood from the enemy serves every candidate target.
        var field = GridManager.Instance.GetDistanceField(enemy.Cell, enemy);

        Unit best = null;
        int bestScore = int.MaxValue;

        foreach (var player in TurnManager.Instance.UnitsOnTeam(Team.Player))
        {
            if (!player.IsOnGrid) continue;   // rescued / in transport: not a target

            int d = PathDistanceToUnit(field, player);
            if (d == int.MaxValue)
                d = UnreachablePenalty + enemy.DistanceTo(player.Cell);

            if (d < bestScore)
            {
                bestScore = d;
                best = player;
            }
        }
        return best;
    }

    /// <summary>
    /// Path length to a unit's cell. That cell is occupied, so it usually isn't in the
    /// field — the true cost is one step past its cheapest walkable neighbour.
    /// Returns int.MaxValue when no route exists.
    /// </summary>
    private int PathDistanceToUnit(Dictionary<Vector2Int, int> field, Unit unit)
    {
        if (field.TryGetValue(unit.Cell, out int direct)) return direct;

        int best = int.MaxValue;
        foreach (var dir in Directions)
            if (field.TryGetValue(unit.Cell + dir, out int d) && d + 1 < best)
                best = d + 1;

        return best;
    }

    private Unit FindAttackableFrom(Unit enemy)
    {
        foreach (var player in TurnManager.Instance.UnitsOnTeam(Team.Player))
            if (enemy.CanAttack(player))
                return player;
        return null;
    }

    /// <summary>
    /// Among all cells this enemy can reach (plus its current cell), choose where to go.
    /// Priority:
    ///   1. If any reachable cell puts the target within attack range, pick the one
    ///      requiring the least MOVEMENT (real step count, not straight-line).
    ///   2. Otherwise, pick the reachable cell with the smallest PATH distance to the
    ///      target, tie-broken by least movement.
    ///
    /// Both passes score with BFS fields. Straight-line scoring is what makes an enemy
    /// hug the near face of a wall forever instead of walking around to the opening.
    /// </summary>
    private Vector2Int FindBestApproachCell(Unit enemy, Unit target)
    {
        var grid = GridManager.Instance;

        // Cost to reach each candidate cell this turn. The keys are exactly the candidate
        // set, and include the enemy's own cell at cost 0 (standing still is allowed).
        Dictionary<Vector2Int, int> travelCost =
            grid.GetDistanceField(enemy.Cell, enemy.moveRange, enemy);

        // Cost from the target to everywhere. Both units pass through so neither one's
        // own tile walls off the flood.
        Dictionary<Vector2Int, int> toTarget =
            grid.GetDistanceField(target.Cell, enemy, target);

        // Pass 1: cells from which the enemy could attack the target.
        // Attack range stays Manhattan to match Unit.CanAttack — shots cross walls.
        Vector2Int bestAttackCell = enemy.Cell;
        int bestTravel = int.MaxValue;
        bool foundAttackCell = false;

        foreach (var entry in travelCost)
        {
            if (ManhattanBetween(entry.Key, target.Cell) > enemy.attackRange) continue;

            if (entry.Value < bestTravel)
            {
                bestTravel = entry.Value;
                bestAttackCell = entry.Key;
                foundAttackCell = true;
            }
        }
        if (foundAttackCell) return bestAttackCell;

        // Pass 2: no attack cell reachable — get as close as possible along a real path.
        Vector2Int best = enemy.Cell;
        int bestDistToTarget = ApproachScore(toTarget, enemy.Cell, target.Cell);
        int bestTravelForBest = 0;

        foreach (var entry in travelCost)
        {
            int d = ApproachScore(toTarget, entry.Key, target.Cell);

            // Closer wins; equally close means take the cell we spend less moving to get to.
            if (d < bestDistToTarget ||
                (d == bestDistToTarget && entry.Value < bestTravelForBest))
            {
                bestDistToTarget = d;
                bestTravelForBest = entry.Value;
                best = entry.Key;
            }
        }
        return best;
    }

    /// <summary>
    /// How good a cell is as an approach: true path distance to the target when one
    /// exists, otherwise a penalised straight-line score. Lower is better.
    /// </summary>
    private int ApproachScore(Dictionary<Vector2Int, int> toTarget, Vector2Int cell, Vector2Int targetCell)
    {
        if (toTarget.TryGetValue(cell, out int d)) return d;
        return UnreachablePenalty + ManhattanBetween(cell, targetCell);
    }

    private int ManhattanBetween(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}