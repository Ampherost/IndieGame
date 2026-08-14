using System;
using System.Collections;
using UnityEngine;

public enum Team { Player, Enemy }

/// <summary>
/// A single combat unit that occupies one grid cell.
/// Attach to a sprite GameObject in the CombatScene and register it with the grid at start.
///
/// A unit holds exactly one cell at a time. PlaceAt() releases the previous cell before
/// claiming a new one, so teleports / reinforcements / rescue can't leak occupancy.
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

    // ---- UI hooks ----
    // Fired whenever currentHP changes, so health bars and info panels can redraw
    // without polling every frame.
    public event Action<Unit> OnHPChanged;
    // Fired once, immediately before the unit is deactivated.
    public event Action<Unit> OnDied;

    // Grid position (source of truth for logic; transform follows it).
    public Vector2Int Cell { get; private set; }

    // Set true after this unit has acted this turn.
    public bool HasActed { get; set; }

    public bool IsAlive => currentHP > 0;
    public bool IsMoving { get; private set; }

    /// <summary>True while this unit holds a cell on the grid.</summary>
    public bool IsOnGrid { get; private set; }

    /// <summary>0..1 health fraction, safe against a zero/negative maxHP.</summary>
    public float HPFraction => maxHP <= 0 ? 0f : Mathf.Clamp01((float)currentHP / maxHP);

    private void Start()
    {
        SnapToGrid();
    }

    // ---- Placement ----

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

        if (grid.IsWalkable(desired) && PlaceAt(desired))
            return;

        string reason = DescribeBlockage(grid, desired);

        if (grid.TryFindNearestFreeCell(desired, out Vector2Int free) && PlaceAt(free))
        {
            Debug.LogWarning(
                $"[Unit] '{unitName}' was placed on cell {desired} but {reason}. " +
                $"Nudged to {free} — fix the placement in the scene.", this);
            return;
        }

        Debug.LogError(
            $"[Unit] '{unitName}' could not be placed near {desired} ({reason}) and no free " +
            $"cell was found. Deactivating it so it doesn't stall the turn loop.", this);

        RemoveFromGrid();
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

    /// <summary>
    /// Put the unit on 'cell', releasing whatever cell it held before.
    ///
    /// Refuses and returns false if the destination is off-map or held by another unit —
    /// validation happens before the old cell is released, so a rejected placement leaves
    /// the unit exactly where it was rather than stranding it off the grid.
    /// </summary>
    public bool PlaceAt(Vector2Int cell)
    {
        var grid = GridManager.Instance;
        if (grid == null) return false;

        if (!grid.InBounds(cell))
        {
            Debug.LogWarning(
                $"[Unit] '{unitName}' cannot take cell {cell}: outside the playable map.", this);
            return false;
        }

        Unit sitting = grid.GetUnitAt(cell);
        if (sitting != null && sitting != this)
        {
            Debug.LogWarning(
                $"[Unit] '{unitName}' cannot take cell {cell}: " +
                $"'{sitting.unitName}' is already there.", this);
            return false;
        }

        // Release the cell we were holding before claiming the new one.
        if (IsOnGrid && Cell != cell)
            grid.ClearCell(Cell, this);

        Cell = cell;
        transform.position = grid.CellToWorld(cell);
        grid.SetUnit(cell, this);
        IsOnGrid = true;
        return true;
    }

    /// <summary>
    /// Instant relocation for teleports, reinforcements, warp-in skills and the like.
    /// Takes 'cell' when it's free; with allowNudge, falls back to the nearest free cell
    /// so a summon aimed at an occupied tile lands beside it instead of failing outright.
    /// Returns false only if nothing suitable was found.
    /// </summary>
    public bool TryWarpTo(Vector2Int cell, bool allowNudge = true)
    {
        var grid = GridManager.Instance;
        if (grid == null) return false;

        if ((grid.IsWalkable(cell) || grid.GetUnitAt(cell) == this) && PlaceAt(cell))
            return true;

        if (!allowNudge) return false;

        if (grid.TryFindNearestFreeCell(cell, out Vector2Int free) && PlaceAt(free))
        {
            Debug.Log($"{unitName} warped to {free} ({cell} was unavailable).");
            return true;
        }

        Debug.LogWarning($"[Unit] '{unitName}' found no free cell near {cell} to warp to.", this);
        return false;
    }

    /// <summary>
    /// Take the unit off the board without killing it — rescue, capture, retreat, or a
    /// transport pickup. The tile is freed; the unit stays alive and registered, so put
    /// it back with PlaceAt / TryWarpTo when it's dropped off.
    /// </summary>
    public void RemoveFromGrid()
    {
        if (!IsOnGrid) return;
        if (GridManager.Instance != null)
            GridManager.Instance.ClearCell(Cell, this);
        IsOnGrid = false;
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
        IsOnGrid = true;
        IsMoving = false;
    }

    // ---- Damage ----

    /// <summary>
    /// The one damage formula. Both the forecast and the real hit call this, so they
    /// cannot drift apart — change the rules here and the preview follows automatically.
    /// </summary>
    public static int ComputeDamage(int rawAttack, int defense)
    {
        return Mathf.Max(1, rawAttack - defense);
    }

    /// <summary>Standard FE-ish damage: attacker.attack - target.defense, min 1.</summary>
    public void TakeDamage(int rawAttack)
    {
        int dmg = ComputeDamage(rawAttack, defense);
        currentHP = Mathf.Max(0, currentHP - dmg);
        Debug.Log($"{unitName} took {dmg} damage ({currentHP}/{maxHP} HP left)");

        OnHPChanged?.Invoke(this);

        if (!IsAlive) Die();
    }

    private void Die()
    {
        Debug.Log($"{unitName} was defeated.");

        OnDied?.Invoke(this);

        RemoveFromGrid();
        gameObject.SetActive(false);

        if (TurnManager.Instance != null)
            TurnManager.Instance.NotifyUnitDied(this);
    }

    // ---- Range ----

    /// <summary>Manhattan distance to another cell — used for attack range checks.</summary>
    public int DistanceTo(Vector2Int other)
    {
        return Mathf.Abs(Cell.x - other.x) + Mathf.Abs(Cell.y - other.y);
    }

    /// <summary>True if 'target' is within this unit's attack range from its current cell.</summary>
    public bool CanAttack(Unit target)
    {
        return CanAttackFrom(Cell, target, target != null ? target.Cell : Vector2Int.zero);
    }

    /// <summary>
    /// Range check with both cells supplied, so a forecast can ask "could I hit that from
    /// over there?" without moving anyone. Everything except the two cells is read from
    /// live state, so a dead or off-board unit is still untouchable.
    /// </summary>
    public bool CanAttackFrom(Vector2Int myCell, Unit target, Vector2Int targetCell)
    {
        if (target == null || !target.IsAlive || !IsAlive) return false;
        if (!IsOnGrid || !target.IsOnGrid) return false;     // off-board units are untouchable
        if (target.team == team) return false;               // no friendly fire

        int d = Mathf.Abs(myCell.x - targetCell.x) + Mathf.Abs(myCell.y - targetCell.y);
        return d <= attackRange;
    }

    // ---- Attacking ----

    /// <summary>Forecast an attack from where this unit is standing right now.</summary>
    public AttackForecast PreviewAttack(Unit target)
    {
        return PreviewAttack(target, Cell);
    }

    /// <summary>
    /// Work out exactly what an attack would do, changing nothing. Pass 'fromCell' to
    /// forecast from a tile the unit hasn't moved to yet — damage doesn't depend on
    /// position, but whether the target can counter does.
    ///
    /// isValid is false when the attack isn't legal; the HP fields still hold current
    /// values, so a panel can render a greyed-out row without special-casing.
    /// </summary>
    public AttackForecast PreviewAttack(Unit target, Vector2Int fromCell)
    {
        var f = new AttackForecast
        {
            attacker = this,
            target = target,
            isValid = false,
            attackerHPBefore = currentHP,
            attackerHPAfter = currentHP,
        };

        if (target == null) return f;

        f.targetHPBefore = target.currentHP;
        f.targetHPAfter = target.currentHP;

        if (!CanAttackFrom(fromCell, target, target.Cell)) return f;

        f.isValid = true;
        f.damage = ComputeDamage(attack, target.defense);
        f.targetHPAfter = Mathf.Max(0, target.currentHP - f.damage);
        f.targetDies = f.targetHPAfter <= 0;

        // The target counters only if it survives and has us in reach from where it stands.
        if (!f.targetDies && target.CanAttackFrom(target.Cell, this, fromCell))
        {
            f.targetCounters = true;
            f.counterDamage = ComputeDamage(target.attack, defense);
            f.attackerHPAfter = Mathf.Max(0, currentHP - f.counterDamage);
            f.attackerDies = f.attackerHPAfter <= 0;
        }

        return f;
    }

    /// <summary>
    /// Resolve an attack against 'target': this unit hits first, and if the target
    /// survives and can reach back, it counterattacks. Returns a short battle log.
    ///
    /// The exchange is decided by PreviewAttack before any HP moves, so what resolves is
    /// exactly what the forecast panel promised.
    /// </summary>
    public string Attack(Unit target)
    {
        AttackForecast f = PreviewAttack(target);

        if (!f.isValid)
        {
            Debug.LogWarning(
                $"[Unit] '{unitName}' was told to attack " +
                $"'{(target != null ? target.unitName : "null")}' but the attack isn't legal.", this);
            return string.Empty;
        }

        var log = new System.Text.StringBuilder();

        target.TakeDamage(attack);
        log.AppendLine($"{unitName} attacks {target.unitName} for {f.ActualDamage}.");

        if (f.targetCounters)
        {
            TakeDamage(target.attack);
            log.AppendLine($"{target.unitName} counters for {f.ActualCounterDamage}.");
        }
        else if (f.targetDies)
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