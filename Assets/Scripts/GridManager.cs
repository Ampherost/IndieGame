using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Owns the combat grid: coordinate conversion, bounds, and which unit occupies each cell.
/// Place one of these in the CombatScene. Uses XY (2D) with cellSize spacing.
///
/// Map SHAPE: if a floor Tilemap is assigned, the playable area is exactly the cells
/// that contain a floor tile — so the map can be any non-rectangular shape (L-shapes,
/// inlets, holes). You "paint the map" simply by painting floor tiles. With no tilemap
/// assigned, the grid falls back to a full width × height rectangle.
///
/// OCCUPANCY: one unit per cell, enforced. Writes that would clobber another unit's
/// record are logged loudly rather than applied silently.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Dimensions")]
    [Tooltip("Max grid extent. With a floor tilemap, this only needs to be big enough to " +
             "contain the painted area; empty cells inside it are treated as out-of-bounds.")]
    public int width = 10;
    public int height = 8;
    public float cellSize = 1f;

    [Tooltip("World position of the bottom-left corner of cell (0,0).")]
    public Vector2 origin = Vector2.zero;

    [Header("Map Shape")]
    [Tooltip("Floor tilemap that defines the playable area. A cell is in-bounds only where a " +
             "floor tile exists. Leave empty to use a solid width × height rectangle.")]
    public Tilemap floorTilemap;

    [Tooltip("If true, cellSize and origin are auto-aligned to the floor tilemap on Awake.")]
    public bool alignToTilemap = true;

    [Header("Optional")]
    [Tooltip("Tiles on this layer block movement (walls, etc). Leave empty to ignore.")]
    public LayerMask obstacleLayer;

    [Header("Placement")]
    [Tooltip("How far a mis-placed unit may be nudged, in cells, when looking for a free tile.")]
    public int maxPlacementSearchRadius = 12;

    // What occupies each cell. null == empty.
    private Unit[,] occupants;

    // Cells that are part of the playable map. Built from the tilemap when present.
    private HashSet<Vector2Int> validCells;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (floorTilemap != null)
            BuildShapeFromTilemap();

        occupants = new Unit[width, height];
    }

    /// <summary>
    /// Derive the playable cells from the floor tilemap. Cells with a tile are valid;
    /// everything else is out-of-bounds. Also sizes the grid to fit the painted area.
    /// </summary>
    private void BuildShapeFromTilemap()
    {
        validCells = new HashSet<Vector2Int>();

        BoundsInt bounds = floorTilemap.cellBounds;

        if (alignToTilemap)
        {
            // Match spacing and origin to the tilemap so cell coordinates line up.
            cellSize = floorTilemap.cellSize.x;
            Vector3 worldMin = floorTilemap.CellToWorld(bounds.min);
            origin = new Vector2(worldMin.x, worldMin.y);
        }

        // Remap tilemap cells so the lowest painted cell becomes our (0,0).
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var tmCell = new Vector3Int(x, y, 0);
                if (floorTilemap.HasTile(tmCell))
                {
                    var local = new Vector2Int(x - bounds.xMin, y - bounds.yMin);
                    validCells.Add(local);
                }
            }
        }

        // Grow the grid to contain the painted region.
        width = Mathf.Max(width, bounds.size.x);
        height = Mathf.Max(height, bounds.size.y);
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
        // Must fall inside the array extents (keeps occupancy indexing safe)...
        if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height)
            return false;

        // ...and, if a map shape is defined, be one of its painted cells.
        if (validCells != null)
            return validCells.Contains(cell);

        return true;   // no shape defined -> full rectangle
    }

    /// <summary>True if this cell is part of the playable map (same as InBounds).</summary>
    public bool IsValidCell(Vector2Int cell) => InBounds(cell);

    /// <summary>Manually mark a cell as playable or not (e.g. for destructible terrain).</summary>
    public void SetCellValid(Vector2Int cell, bool valid)
    {
        validCells ??= new HashSet<Vector2Int>();
        if (valid) validCells.Add(cell);
        else validCells.Remove(cell);
    }

    // ---- Occupancy ----

    public Unit GetUnitAt(Vector2Int cell)
    {
        if (occupants == null) return null;
        if (!InBounds(cell)) return null;
        return occupants[cell.x, cell.y];
    }

    public bool IsOccupied(Vector2Int cell) => GetUnitAt(cell) != null;

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

    /// <summary>
    /// Record 'unit' as the occupant of 'cell'. Out-of-bounds writes are refused and
    /// clobbering another unit's record is reported — neither should ever happen once
    /// units place themselves through Unit.SnapToGrid().
    /// </summary>
    public void SetUnit(Vector2Int cell, Unit unit)
    {
        if (!InBounds(cell))
        {
            Debug.LogWarning(
                $"[GridManager] Refused to register {DescribeUnit(unit)} at {cell}: " +
                $"that cell is outside the playable map.", unit);
            return;
        }

        Unit existing = occupants[cell.x, cell.y];
        if (existing != null && existing != unit)
        {
            Debug.LogError(
                $"[GridManager] Cell {cell} already holds {DescribeUnit(existing)}; " +
                $"{DescribeUnit(unit)} is overwriting it. Two units share a tile — " +
                $"check their positions in the scene.", unit);
        }

        occupants[cell.x, cell.y] = unit;
    }

    public void ClearCell(Vector2Int cell)
    {
        if (InBounds(cell)) occupants[cell.x, cell.y] = null;
    }

    /// <summary>
    /// Clear a cell only if 'expected' is the unit currently recorded there. Prevents a
    /// unit with a stale Cell value from wiping another unit's occupancy record.
    /// </summary>
    public void ClearCell(Vector2Int cell, Unit expected)
    {
        if (!InBounds(cell)) return;
        if (occupants[cell.x, cell.y] == expected)
            occupants[cell.x, cell.y] = null;
    }

    /// <summary>Move occupancy record from one cell to another.</summary>
    public void MoveUnit(Vector2Int from, Vector2Int to, Unit unit)
    {
        ClearCell(from, unit);
        SetUnit(to, unit);
    }

    // ---- Placement help ----

    /// <summary>
    /// Find the closest cell to 'start' that is in-bounds and free.
    ///
    /// Search flood-fills outward through in-bounds cells, passing over occupied ones
    /// (so a unit can be stepped around) but never leaving the painted map — the result
    /// is therefore always in the same connected region as 'start'. If 'start' itself is
    /// off-map, falls back to a distance scan over the playable cells.
    ///
    /// Returns false if nothing free is found.
    /// </summary>
    public bool TryFindNearestFreeCell(Vector2Int start, out Vector2Int result, int maxSearchRadius = -1)
    {
        if (maxSearchRadius < 0) maxSearchRadius = maxPlacementSearchRadius;

        result = start;

        if (IsWalkable(start)) return true;

        // Off the map entirely -> BFS has nothing to walk along, so scan instead.
        if (!InBounds(start))
            return TryScanForClosestFreeCell(start, out result);

        var visited = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>();
        var depth = new Dictionary<Vector2Int, int> { [start] = 0 };
        queue.Enqueue(start);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int d = depth[current];
            if (d >= maxSearchRadius) continue;

            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                if (!visited.Add(next)) continue;
                if (!InBounds(next)) continue;      // stay inside the painted map

                if (IsWalkable(next))
                {
                    result = next;
                    return true;
                }

                // Occupied or blocked: keep walking through it to reach cells beyond.
                depth[next] = d + 1;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>Brute-force nearest free cell by Manhattan distance. Fallback for off-map starts.</summary>
    private bool TryScanForClosestFreeCell(Vector2Int start, out Vector2Int result)
    {
        result = start;
        int bestDist = int.MaxValue;
        bool found = false;

        if (validCells != null)
        {
            foreach (var cell in validCells)
            {
                if (!IsWalkable(cell)) continue;
                int d = Mathf.Abs(cell.x - start.x) + Mathf.Abs(cell.y - start.y);
                if (d < bestDist) { bestDist = d; result = cell; found = true; }
            }
            return found;
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (!IsWalkable(cell)) continue;
                int d = Mathf.Abs(x - start.x) + Mathf.Abs(y - start.y);
                if (d < bestDist) { bestDist = d; result = cell; found = true; }
            }
        }
        return found;
    }

    private static string DescribeUnit(Unit u)
    {
        if (u == null) return "an obstacle";
        return $"'{u.unitName}' ({u.name})";
    }

    // ---- Pathfinding helpers: BFS distance fields ----

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    /// <summary>
    /// The one BFS. Walks outward from 'start' up to 'maxSteps', recording the true
    /// 4-directional step count to every cell it can reach.
    ///
    /// 'passThrough' units are treated as passable — pass a unit in when its own cell
    /// shouldn't wall off the flood (the mover itself, or the target being pathed toward).
    /// Everything else occupied or blocked stays impassable. The start cell is always
    /// included at distance 0, even if something is standing on it.
    ///
    /// 'cameFrom' collects predecessors when the caller wants to rebuild a route; pass
    /// null when only distances are needed. 'goal' stops the search early once that cell
    /// is dequeued.
    /// </summary>
    private Dictionary<Vector2Int, int> Flood(
        Vector2Int start,
        int maxSteps,
        Unit[] passThrough,
        Dictionary<Vector2Int, Vector2Int> cameFrom = null,
        Vector2Int? goal = null)
    {
        var dist = new Dictionary<Vector2Int, int>();
        if (!InBounds(start)) return dist;

        dist[start] = 0;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (goal.HasValue && current == goal.Value) break;

            int d = dist[current];
            if (d >= maxSteps) continue;

            foreach (var dir in Directions)
            {
                Vector2Int next = current + dir;
                if (dist.ContainsKey(next)) continue;   // already visited
                if (!InBounds(next)) continue;
                if (!IsPassable(next, passThrough)) continue;

                dist[next] = d + 1;
                cameFrom?.Add(next, current);
                queue.Enqueue(next);
            }
        }
        return dist;
    }

    /// <summary>
    /// True step count from 'start' to every cell it can reach, up to 'maxSteps'. This is
    /// the honest cost on L-shaped or walled maps, where straight-line distance
    /// under-reports badly or points straight into a wall.
    /// </summary>
    public Dictionary<Vector2Int, int> GetDistanceField(
        Vector2Int start, int maxSteps, params Unit[] passThrough)
    {
        return Flood(start, maxSteps, passThrough);
    }

    /// <summary>Uncapped distance field from 'start'.</summary>
    public Dictionary<Vector2Int, int> GetDistanceField(Vector2Int start, params Unit[] passThrough)
    {
        return Flood(start, int.MaxValue, passThrough);
    }

    /// <summary>
    /// Shortest walkable route from 'start' to 'goal' as a list of cells to step onto,
    /// excluding 'start' itself and no longer than 'maxSteps'.
    ///
    /// Returns null when no route exists within the step limit. Returns an EMPTY list
    /// when start == goal — "you're already there" is success, not failure, and callers
    /// must not confuse the two.
    /// </summary>
    public List<Vector2Int> GetPath(
        Vector2Int start, Vector2Int goal, int maxSteps, params Unit[] passThrough)
    {
        var path = new List<Vector2Int>();
        if (start == goal) return path;

        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var dist = Flood(start, maxSteps, passThrough, cameFrom, goal);

        if (!dist.ContainsKey(goal)) return null;

        Vector2Int node = goal;
        while (node != start)
        {
            path.Add(node);
            node = cameFrom[node];
        }
        path.Reverse();
        return path;
    }

    /// <summary>Walkable, or occupied by one of the units we're allowed to route through.</summary>
    private bool IsPassable(Vector2Int cell, Unit[] passThrough)
    {
        if (IsWalkable(cell)) return true;
        if (passThrough == null || passThrough.Length == 0) return false;

        Unit occupant = GetUnitAt(cell);
        if (occupant == null) return false;      // blocked by terrain, not a unit

        foreach (var u in passThrough)
            if (u != null && u == occupant) return true;

        return false;
    }

    /// <summary>
    /// Returns all cells reachable from 'start' within 'moveRange' steps (4-directional),
    /// treating occupied/blocked cells as impassable. Excludes the start cell.
    /// </summary>
    public HashSet<Vector2Int> GetReachableCells(Vector2Int start, int moveRange)
    {
        var field = GetDistanceField(start, moveRange);
        var reachable = new HashSet<Vector2Int>(field.Keys);
        reachable.Remove(start);
        return reachable;
    }

    // ---- Editor visualization ----

    private void OnDrawGizmosSelected()
    {
        // If a shape is built (play mode), outline the actual playable cells.
        if (validCells != null && validCells.Count > 0)
        {
            Gizmos.color = Color.green;
            foreach (var cell in validCells)
            {
                Vector3 center = CellToWorld(cell);
                Gizmos.DrawWireCube(center, new Vector3(cellSize, cellSize, 0f) * 0.95f);
            }
            return;
        }

        // Otherwise (edit mode / no tilemap) draw the full rectangular grid.
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