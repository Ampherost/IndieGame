using System.Collections;
using UnityEngine;

public enum Team { Player, Enemy }

/// <summary>
/// A single combat unit that occupies one grid cell.
/// Attach to a sprite GameObject in the CombatScene and register it with the grid at start.
/// </summary>
public class Unit : MonoBehaviour
{
    [Header("Identity")]
    public string unitName = "Unit";
    public Team team = Team.Player;

    [Header("Stats")]
    public int maxHP = 20;
    public int currentHP = 20;
    public int attack = 5;
    public int defense = 2;
    public int moveRange = 4;
    public int attackRange = 1;

    [Header("Movement")]
    [Tooltip("How fast the unit slides between tiles, in cells/sec.")]
    public float moveSpeed = 6f;

    // Grid position (source of truth for logic; transform follows it).
    public Vector2Int Cell { get; private set; }

    // Set true after this unit has acted this turn.
    public bool HasActed { get; set; }

    public bool IsAlive => currentHP > 0;
    public bool IsMoving { get; private set; }

    /// <summary>True once this unit holds a real cell on the grid.</summary>
    public bool IsOnGrid { get; private set; }

    private void Start()
    {
        SnapToGrid();
    }

    /// <summary>
    /// Claim a grid cell based on where this unit was placed in the editor.
    ///
    /// If that cell is unusable — off the painted map, blocked, or already claimed by
    /// another unit (Start() order across GameObjects is arbitrary, so which unit gets
    /// there first is not something to rely on) — the unit is nudged to the nearest free
    /// cell and a warning names both tiles. A unit that cannot be placed at all is
    /// deactivated and unregistered, because an unplaceable-but-alive unit can never be
    /// selected and would stall the player phase forever.
    /// </summary>
    public void SnapToGrid()
    {
        var grid = GridManager.Instance;
        if (grid == null)
        {
            Debug.LogError($"[Unit] '{unitName}' found no GridManager in the scene.", this);
            enabled = false;
            return;
        }

        Vector2Int desired = grid.WorldToCell(transform.position);

        if (grid.IsWalkable(desired))
        {
            PlaceAt(desired);
            return;
        }

        string reason = DescribeBlockage(grid, desired);

        if (grid.TryFindNearestFreeCell(desired, out Vector2Int free))
        {
            Debug.LogWarning(
                $"[Unit] '{unitName}' was placed on cell {desired} but {reason}. " +
                $"Nudged to {free} — fix the placement in the scene.", this);
            PlaceAt(free);
            return;
        }

        Debug.LogError(
            $"[Unit] '{unitName}' could not be placed near {desired} ({reason}) and no free " +
            $"cell was found. Deactivating it so it doesn't stall the turn loop.", this);

        IsOnGrid = false;
        if (TurnManager.Instance != null) TurnManager.Instance.UnregisterUnit(this);
        gameObject.SetActive(false);
    }

    private string DescribeBlockage(GridManager grid, Vector2Int cell)
    {
        if (!grid.InBounds(cell))
            return "that cell is outside the playable map";

        Unit other = grid.GetUnitAt(cell);
        if (other != null)
            return $"'{other.unitName}' ({other.name}) is already standing there";

        return "that cell is blocked by an obstacle";
    }

    /// <summary>Instantly place the unit at a cell and register occupancy.</summary>
    public void PlaceAt(Vector2Int cell)
    {
        Cell = cell;
        transform.position = GridManager.Instance.CellToWorld(cell);
        GridManager.Instance.SetUnit(cell, this);
        IsOnGrid = true;
    }

    /// <summary>Smoothly move along a path (list of adjacent cells), updating occupancy at the end.</summary>
    public IEnumerator MoveAlong(System.Collections.Generic.List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) yield break;

        IsMoving = true;
        Vector2Int from = Cell;

        foreach (var step in path)
        {
            Vector3 target = GridManager.Instance.CellToWorld(step);
            while ((transform.position - target).sqrMagnitude > 0.0001f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        Vector2Int dest = path[path.Count - 1];
        GridManager.Instance.MoveUnit(from, dest, this);
        Cell = dest;
        IsMoving = false;
    }

    /// <summary>Standard FE-ish damage: attacker.attack - target.defense, min 1.</summary>
    public void TakeDamage(int rawAttack)
    {
        int dmg = Mathf.Max(1, rawAttack - defense);
        currentHP = Mathf.Max(0, currentHP - dmg);
        Debug.Log($"{unitName} took {dmg} damage ({currentHP}/{maxHP} HP left)");

        if (!IsAlive) Die();
    }

    private void Die()
    {
        Debug.Log($"{unitName} was defeated.");

        // Checked clear: only release the cell if we're the unit recorded there.
        GridManager.Instance.ClearCell(Cell, this);
        IsOnGrid = false;
        gameObject.SetActive(false);

        if (TurnManager.Instance != null)
            TurnManager.Instance.NotifyUnitDied(this);
    }

    /// <summary>Manhattan distance to another cell — used for attack range checks.</summary>
    public int DistanceTo(Vector2Int other)
    {
        return Mathf.Abs(Cell.x - other.x) + Mathf.Abs(Cell.y - other.y);
    }

    /// <summary>True if 'target' is within this unit's attack range from its current cell.</summary>
    public bool CanAttack(Unit target)
    {
        if (target == null || !target.IsAlive) return false;
        if (target.team == team) return false;              // no friendly fire
        return DistanceTo(target.Cell) <= attackRange;
    }

    /// <summary>
    /// Resolve an attack against 'target': this unit hits first, and if the target
    /// survives and can reach back, it counterattacks. Returns a short battle log.
    /// </summary>
    public string Attack(Unit target)
    {
        var log = new System.Text.StringBuilder();

        // Primary strike
        int before = target.currentHP;
        target.TakeDamage(attack);
        int dealt = before - target.currentHP;
        log.AppendLine($"{unitName} attacks {target.unitName} for {dealt}.");

        // Counterattack, if target survived and has us in range
        if (target.IsAlive && target.CanAttack(this))
        {
            int cBefore = currentHP;
            TakeDamage(target.attack);
            int cDealt = cBefore - currentHP;
            log.AppendLine($"{target.unitName} counters for {cDealt}.");
        }
        else if (!target.IsAlive)
        {
            log.AppendLine($"{target.unitName} is defeated!");
        }

        return log.ToString().TrimEnd();
    }

#if UNITY_EDITOR
    // ---- Edit-time overlap warning ----
    // Draws the cell this unit will snap to. Red means another unit snaps to the same
    // cell, so you can catch duplicate placement before entering play mode.
    private void OnDrawGizmos()
    {
        GridManager grid = GridManager.Instance != null
            ? GridManager.Instance
            : FindFirstObjectByType<GridManager>();
        if (grid == null) return;

        Vector2Int myCell = grid.WorldToCell(transform.position);
        bool clash = false;

        foreach (var other in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (other == this) continue;
            if (grid.WorldToCell(other.transform.position) == myCell) { clash = true; break; }
        }

        Gizmos.color = clash ? Color.red : new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawWireCube(grid.CellToWorld(myCell),
                            new Vector3(grid.cellSize, grid.cellSize, 0f) * 0.9f);
    }
#endif
}