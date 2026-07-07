using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy-phase AI. For each enemy, in order:
///   1. Find the nearest living player unit (by Manhattan distance).
///   2. If already in attack range, attack it.
///   3. Otherwise, move to the reachable tile that gets closest to that target,
///      then attack if the new position puts a player in range.
///
/// Reuses GridManager reachability + Unit.Attack, so behavior stays consistent
/// with the player's own combat rules (including counterattacks).
/// </summary>
public class EnemyPhaseController : MonoBehaviour
{
    [Tooltip("Seconds between each enemy's action, so the phase is readable.")]
    public float actionDelay = 0.35f;

    [Tooltip("Brief pause after an attack resolves.")]
    public float attackPause = 0.4f;

    private void OnEnable()
    {
        StartCoroutine(WaitForTurnManager());
    }

    private IEnumerator WaitForTurnManager()
    {
        yield return null;  // TurnManager sets up in Start()
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnPhaseStart += HandlePhaseStart;
    }

    private void OnDisable()
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
            if (enemy == null || !enemy.IsAlive) continue;

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
        if (destination != enemy.Cell)
        {
            List<Vector2Int> path = FindPath(enemy.Cell, destination, enemy.moveRange);
            if (path != null && path.Count > 0)
                yield return StartCoroutine(enemy.MoveAlong(path));
        }

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

    private Unit FindNearestPlayer(Unit enemy)
    {
        Unit best = null;
        int bestDist = int.MaxValue;
        foreach (var player in TurnManager.Instance.UnitsOnTeam(Team.Player))
        {
            int d = enemy.DistanceTo(player.Cell);
            if (d < bestDist)
            {
                bestDist = d;
                best = player;
            }
        }
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
    ///      requiring the least movement (stay safe / don't overcommit).
    ///   2. Otherwise, pick the reachable cell with the smallest distance to the target.
    /// </summary>
    private Vector2Int FindBestApproachCell(Unit enemy, Unit target)
    {
        HashSet<Vector2Int> reachable =
            GridManager.Instance.GetReachableCells(enemy.Cell, enemy.moveRange);

        // Include the current cell as a candidate (standing still is allowed).
        var candidates = new List<Vector2Int>(reachable) { enemy.Cell };

        // Pass 1: cells from which the enemy could attack the target.
        Vector2Int bestAttackCell = enemy.Cell;
        int bestTravel = int.MaxValue;
        bool foundAttackCell = false;

        foreach (var cell in candidates)
        {
            int distToTarget = ManhattanBetween(cell, target.Cell);
            if (distToTarget <= enemy.attackRange)
            {
                int travel = ManhattanBetween(enemy.Cell, cell);
                if (travel < bestTravel)
                {
                    bestTravel = travel;
                    bestAttackCell = cell;
                    foundAttackCell = true;
                }
            }
        }
        if (foundAttackCell) return bestAttackCell;

        // Pass 2: no attack cell reachable — just get as close as possible.
        Vector2Int best = enemy.Cell;
        int bestDistToTarget = ManhattanBetween(enemy.Cell, target.Cell);
        foreach (var cell in candidates)
        {
            int d = ManhattanBetween(cell, target.Cell);
            if (d < bestDistToTarget)
            {
                bestDistToTarget = d;
                best = cell;
            }
        }
        return best;
    }

    private int ManhattanBetween(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // ---- Pathfinding (same BFS as the player controller) ----

    private List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, int maxSteps)
    {
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var dist = new Dictionary<Vector2Int, int> { [start] = 0 };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == goal) break;
            if (dist[current] >= maxSteps) continue;

            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                if (dist.ContainsKey(next)) continue;
                if (!GridManager.Instance.InBounds(next)) continue;
                if (!GridManager.Instance.IsWalkable(next)) continue;

                dist[next] = dist[current] + 1;
                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(goal)) return null;

        var path = new List<Vector2Int>();
        Vector2Int node = goal;
        while (node != start)
        {
            path.Add(node);
            node = cameFrom[node];
        }
        path.Reverse();
        return path;
    }
}