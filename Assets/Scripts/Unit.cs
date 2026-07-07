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

    private void Start()
    {
        // Snap to nearest cell based on where it was placed in the editor.
        var grid = GridManager.Instance;
        Vector2Int startCell = grid.WorldToCell(transform.position);
        PlaceAt(startCell);
    }

    /// <summary>Instantly place the unit at a cell and register occupancy.</summary>
    public void PlaceAt(Vector2Int cell)
    {
        Cell = cell;
        transform.position = GridManager.Instance.CellToWorld(cell);
        GridManager.Instance.SetUnit(cell, this);
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
        GridManager.Instance.ClearCell(Cell);
        gameObject.SetActive(false);
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
}