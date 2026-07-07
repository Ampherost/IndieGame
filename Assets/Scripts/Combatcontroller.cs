using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-side combat input, turn-aware with attack resolution.
///
/// Flow:
///   1. Click a player unit (player phase only) -> movement range highlights (blue).
///   2. Click a reachable tile -> unit slides there.
///   3. If enemies are now in attack range, they highlight (red) and we wait for a target.
///        - Click a highlighted enemy -> attack resolves -> unit's turn ends.
///        - Right-click / click elsewhere -> unit waits -> turn ends.
///   4. If no enemies are in range after moving, the turn ends automatically.
/// </summary>
public class CombatController : MonoBehaviour
{
    [Header("Highlights")]
    [Tooltip("Semi-transparent square marking reachable movement tiles (e.g. blue).")]
    public GameObject moveHighlightPrefab;
    [Tooltip("Marker placed on enemies that can be attacked (e.g. red).")]
    public GameObject attackHighlightPrefab;

    public Camera combatCamera;

    private enum State { Idle, UnitSelected, AwaitingTarget }
    private State state = State.Idle;

    private Unit selectedUnit;
    private HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
    private readonly List<Unit> targetsInRange = new List<Unit>();
    private readonly List<GameObject> activeHighlights = new List<GameObject>();

    private bool inputLocked;   // true while a unit is animating / resolving

    private void Awake()
    {
        if (combatCamera == null) combatCamera = Camera.main;
    }

    private void Update()
    {
        if (inputLocked) return;
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentPhase != Team.Player)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 world = combatCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int cell = GridManager.Instance.WorldToCell(world);
            HandleLeftClick(cell);
        }

        if (Input.GetMouseButtonDown(1))
            HandleRightClick();
    }

    private void HandleLeftClick(Vector2Int cell)
    {
        switch (state)
        {
            case State.Idle:
            case State.UnitSelected:
                HandleSelectionClick(cell);
                break;

            case State.AwaitingTarget:
                HandleTargetClick(cell);
                break;
        }
    }

    private void HandleRightClick()
    {
        if (state == State.AwaitingTarget)
        {
            // Right-click while choosing a target == "wait" (no attack).
            FinishUnitTurn();
        }
        else
        {
            Deselect();
        }
    }

    // ---- Selection / movement ----

    private void HandleSelectionClick(Vector2Int cell)
    {
        Unit unitAtCell = GridManager.Instance.GetUnitAt(cell);

        if (unitAtCell != null && unitAtCell.team == Team.Player && !unitAtCell.HasActed)
        {
            Select(unitAtCell);
            return;
        }

        if (state == State.UnitSelected && reachable.Contains(cell))
        {
            StartCoroutine(MoveSelectedTo(cell));
            return;
        }

        Deselect();
    }

    private void Select(Unit unit)
    {
        ClearHighlights();
        selectedUnit = unit;
        state = State.UnitSelected;
        reachable = GridManager.Instance.GetReachableCells(unit.Cell, unit.moveRange);
        ShowMoveHighlights(reachable);
    }

    private IEnumerator MoveSelectedTo(Vector2Int dest)
    {
        List<Vector2Int> path = FindPath(selectedUnit.Cell, dest, selectedUnit.moveRange);
        if (path == null) { Deselect(); yield break; }

        Unit acting = selectedUnit;
        ClearHighlights();
        reachable.Clear();
        inputLocked = true;

        yield return StartCoroutine(acting.MoveAlong(path));

        inputLocked = false;

        // After arriving, check for attack targets.
        FindTargetsInRange(acting);
        if (targetsInRange.Count > 0)
        {
            state = State.AwaitingTarget;
            ShowAttackHighlights(targetsInRange);
        }
        else
        {
            FinishUnitTurn();
        }
    }

    // ---- Attacking ----

    private void FindTargetsInRange(Unit attacker)
    {
        targetsInRange.Clear();
        foreach (var enemy in TurnManager.Instance.UnitsOnTeam(Team.Enemy))
            if (attacker.CanAttack(enemy))
                targetsInRange.Add(enemy);
    }

    private void HandleTargetClick(Vector2Int cell)
    {
        Unit clicked = GridManager.Instance.GetUnitAt(cell);
        if (clicked != null && targetsInRange.Contains(clicked))
        {
            StartCoroutine(ResolveAttack(selectedUnit, clicked));
        }
        else
        {
            // Clicked something that isn't a valid target -> treat as wait.
            FinishUnitTurn();
        }
    }

    private IEnumerator ResolveAttack(Unit attacker, Unit target)
    {
        inputLocked = true;
        ClearHighlights();

        string log = attacker.Attack(target);
        Debug.Log(log);

        // If you want battle text on-screen, this is where DialogueManager slots in:
        // if (DialogueManager.Instance != null)
        //     DialogueManager.Instance.ShowDialogue(log.Split('\n'));

        yield return new WaitForSeconds(0.4f);   // brief beat for the exchange

        inputLocked = false;
        FinishUnitTurn();

        CheckEndConditions();
    }

    private void FinishUnitTurn()
    {
        Unit acting = selectedUnit;
        ClearHighlights();
        targetsInRange.Clear();
        reachable.Clear();
        selectedUnit = null;
        state = State.Idle;

        if (acting != null)
            TurnManager.Instance.NotifyUnitActed(acting);
    }

    private void Deselect()
    {
        selectedUnit = null;
        state = State.Idle;
        reachable.Clear();
        targetsInRange.Clear();
        ClearHighlights();
    }

    // ---- Win / loss check ----

    private void CheckEndConditions()
    {
        if (!TurnManager.Instance.AnyAlive(Team.Enemy))
            Debug.Log("Victory! All enemies defeated.");
        else if (!TurnManager.Instance.AnyAlive(Team.Player))
            Debug.Log("Defeat! All player units lost.");
    }

    // ---- BFS pathfinding ----

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

    // ---- Highlight rendering ----

    private void ShowMoveHighlights(HashSet<Vector2Int> cells)
    {
        if (moveHighlightPrefab == null) return;
        foreach (var cell in cells)
        {
            Vector3 pos = GridManager.Instance.CellToWorld(cell);
            activeHighlights.Add(Instantiate(moveHighlightPrefab, pos, Quaternion.identity));
        }
    }

    private void ShowAttackHighlights(List<Unit> targets)
    {
        if (attackHighlightPrefab == null) return;
        foreach (var t in targets)
        {
            Vector3 pos = GridManager.Instance.CellToWorld(t.Cell);
            activeHighlights.Add(Instantiate(attackHighlightPrefab, pos, Quaternion.identity));
        }
    }

    private void ClearHighlights()
    {
        foreach (var h in activeHighlights)
            if (h != null) Destroy(h);
        activeHighlights.Clear();
    }
}