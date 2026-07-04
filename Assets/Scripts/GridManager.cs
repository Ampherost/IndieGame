using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the combat grid: coordinate conversion, bounds, and which unit occupies each cell.
/// Place one of these in the CombatScene. Uses XY (2D) with cellSize spacing.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Dimensions")]
    public int width = 10;
    public int height = 8;
    public float cellSize = 1f;

    [Tooltip("World position of the bottom-left corner of cell (0,0).")]
    public Vector2 origin = Vector2.zero;

    [Header("Optional")]
    [Tooltip("Tiles on this layer block movement (walls, etc). Leave empty to ignore.")]
    public LayerMask obstacleLayer;

    // What occupies each cell. null == empty.
    private Unit[,] occupants;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        occupants = new Unit[width, height];
    }

    // ---- Coordinate conversion ----

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.x - origin.x) / cellSize);
        int y = Mathf.FloorToInt((worldPos.y - origin.y) / cellSize);
        return new Vector2Int(x, y);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        // center of the cell
        float x = origin.x + (cell.x + 0.5f) * cellSize;
        float y = origin.y + (cell.y + 0.5f) * cellSize;
        return new Vector3(x, y, 0f);
    }

    public bool InBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    // ---- Occupancy ----

    public Unit GetUnitAt(Vector2Int cell)
    {
        if (!InBounds(cell)) return null;
        return occupants[cell.x, cell.y];
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (!InBounds(cell)) return false;
        if (occupants[cell.x, cell.y] != null) return false;

        // Physics obstacle check (optional)
        if (obstacleLayer.value != 0)
        {
            Collider2D hit = Physics2D.OverlapPoint(CellToWorld(cell), obstacleLayer);
            if (hit != null) return false;
        }
        return true;
    }

    public void SetUnit(Vector2Int cell, Unit unit)
    {
        if (InBounds(cell)) occupants[cell.x, cell.y] = unit;
    }

    public void ClearCell(Vector2Int cell)
    {
        if (InBounds(cell)) occupants[cell.x, cell.y] = null;
    }

    /// <summary>Move occupancy record from one cell to another.</summary>
    public void MoveUnit(Vector2Int from, Vector2Int to, Unit unit)
    {
        ClearCell(from);
        SetUnit(to, unit);
    }

    // ---- Pathfinding helper: reachable cells via flood fill (BFS) ----

    /// <summary>
    /// Returns all cells reachable from 'start' within 'moveRange' steps (4-directional),
    /// treating occupied/blocked cells as impassable. Excludes the start cell.
    /// </summary>
    public HashSet<Vector2Int> GetReachableCells(Vector2Int start, int moveRange)
    {
        var reachable = new HashSet<Vector2Int>();
        var dist = new Dictionary<Vector2Int, int> { [start] = 0 };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        Vector2Int[] dirs =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int d = dist[current];
            if (d >= moveRange) continue;

            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                if (dist.ContainsKey(next)) continue;   // already visited
                if (!InBounds(next)) continue;
                if (!IsWalkable(next)) continue;

                dist[next] = d + 1;
                reachable.Add(next);
                queue.Enqueue(next);
            }
        }
        return reachable;
    }

    // ---- Editor visualization ----

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        for (int x = 0; x <= width; x++)
        {
            Vector3 a = new Vector3(origin.x + x * cellSize, origin.y, 0);
            Vector3 b = new Vector3(origin.x + x * cellSize, origin.y + height * cellSize, 0);
            Gizmos.DrawLine(a, b);
        }
        for (int y = 0; y <= height; y++)
        {
            Vector3 a = new Vector3(origin.x, origin.y + y * cellSize, 0);
            Vector3 b = new Vector3(origin.x + width * cellSize, origin.y + y * cellSize, 0);
            Gizmos.DrawLine(a, b);
        }
    }
}