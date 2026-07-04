using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player-side combat input: click a unit to select it, see its move range,
/// click a highlighted tile to move there. This is Phase 3 of the roadmap —
/// turn system and combat resolution build on top of it.
/// </summary>
public class CombatController : MonoBehaviour
{
    [Header("Highlight")]
    [Tooltip("A simple sprite prefab (e.g. a semi-transparent square) used to mark reachable tiles.")]
    public GameObject tileHighlightPrefab;
    public Camera combatCamera;

    private Unit selectedUnit;
    private HashSet<Vector2Int> reachable = new HashSet<Vector2Int>();
    private readonly List<GameObject> activeHighlights = new List<GameObject>();

    private void Awake()
    {
        if (combatCamera == null) combatCamera = Camera.main;
    }

    private void Update()
    {
        // Left click
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 world = combatCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2Int cell = GridManager.Instance.WorldToCell(world);
            HandleClick(cell);
        }

        // Right click / escape to deselect
        if (Input.GetMouseButtonDown(1))
            Deselect();
    }

    private void HandleClick(Vector2Int cell)
    {
        Unit unitAtCell = GridManager.Instance.GetUnitAt(cell);

        // Case 1: clicking a selectable player unit -> select it
        if (unitAtCell != null && unitAtCell.team == Team.Player && !unitAtCell.HasActed)
        {
            Select(unitAtCell);
            return;
        }

        // Case 2: a unit is selected and we clicked a reachable tile -> move
        if (selectedUnit != null && reachable.Contains(cell))
        {
            MoveSelectedTo(cell);
            return;
        }

        // Case 3: clicked empty / invalid -> deselect
        Deselect();
    }

    private void Select(Unit unit)
    {
        ClearHighlights();
        selectedUnit = unit;
        reachable = GridManager.Instance.GetReachableCells(unit.Cell, unit.moveRange);
        ShowHighlights(reachable);
    }

    private void MoveSelectedTo(Vector2Int dest)
    {
        List<Vector2Int> path = FindPath(selectedUnit.Cell, dest, selectedUnit.moveRange);
        if (path == null) { Deselect(); return; }

        StartCoroutine(selectedUnit.MoveAlong(path));
        selectedUnit.HasActed = true;   // consumed for this turn (refine when turn system lands)
        Deselect();
    }

    private void Deselect()
    {
        selectedUnit = null;
        reachable.Clear();
        ClearHighlights();
    }

    // ---- BFS pathfinding: reconstructs a shortest path within range ----

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

        // Walk back from goal to start
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