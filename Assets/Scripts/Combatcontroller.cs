using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-side combat input, now turn-aware.
/// Flow: select a player unit (player phase only) -> see move range ->
/// click a reachable tile -> unit slides there -> unit is marked as acted.
/// (Phase 5 will insert an attack/wait choice between "arrived" and "acted".)
/// </summary>
public class CombatController : MonoBehaviour
{
    [Header("Highlight")]
    [Tooltip("A semi-transparent square sprite prefab used to mark reachable tiles.")]
    public GameObject tileHighlightPrefab;
    public Camera combatCamera;

    private Unit selectedUnit;
    private HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
    private readonly List<GameObject> activeHighlights = new List<GameObject>();

    private bool inputLocked;   // true while a unit is animating

    private void Awake()
    {
        if (combatCamera == null) combatCamera = Camera.main;
    }

    private void Update()
    {
        // Only the player acts during the player phase, and not mid-animation.
        if (inputLocked) return;
        if (TurnManager.Instance == null || TurnManager.Instance.CurrentPhase != Team.Player)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector3 world = combatCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int cell = GridManager.Instance.WorldToCell(world);
            HandleClick(cell);
        }

        if (Input.GetMouseButtonDown(1))
            Deselect();
    }

    private void HandleClick(Vector2Int cell)
    {
        Unit unitAtCell = GridManager.Instance.GetUnitAt(cell);

        if (unitAtCell != null && unitAtCell.team == Team.Player && !unitAtCell.HasActed)
        {
            Select(unitAtCell);
            return;
        }

        if (selectedUnit != null && reachable.Contains(cell))
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
        reachable = GridManager.Instance.GetReachableCells(unit.Cell, unit.moveRange);
        ShowHighlights(reachable);
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
        selectedUnit = null;

        // For now, moving completes the unit's turn.
        // Phase 5 will pop an attack/wait menu here before notifying.
        TurnManager.Instance.NotifyUnitActed(acting);
    }

    private void Deselect()
    {
        selectedUnit = null;
        reachable.Clear();
        ClearHighlights();
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

    private void ShowHighlights(HashSet<Vector2Int> cells)
    {
        if (tileHighlightPrefab == null) return;
        foreach (var cell in cells)
        {
            Vector3 pos = GridManager.Instance.CellToWorld(cell);
            GameObject h = Instantiate(tileHighlightPrefab, pos, Quaternion.identity);
            activeHighlights.Add(h);
        }
    }

    private void ClearHighlights()
    {
        foreach (var h in activeHighlights)
            if (h != null) Destroy(h);
        activeHighlights.Clear();
    }
}