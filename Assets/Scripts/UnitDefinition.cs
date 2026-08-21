using UnityEngine;

/// <summary>
/// The template for one kind of unit: which prefab to spawn and what its base stats are.
///
/// This is the *shared* half of a unit's identity — it never changes during play. The
/// mutable half (current HP, whether they're dead) lives in a PartyMember, or is simply
/// discarded when the battle ends in the case of enemies.
///
/// Create with: Assets -> Create -> SRPG -> Unit Definition
/// </summary>
[CreateAssetMenu(fileName = "NewUnit", menuName = "SRPG/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    [Header("Identity")]
    public string unitName = "Unit";

    [Tooltip("Prefab with a Unit component on it. Sprite, collider, animator — whatever you " +
             "want the unit to look like in the combat scene.")]
    public GameObject prefab;

    [Header("Base Stats")]
    public int maxHP = 20;
    public int attack = 5;
    public int defense = 2;
    public int moveRange = 4;
    public int attackRange = 1;

    /// <summary>
    /// Stamp these stats onto a freshly spawned Unit. Current HP is set to full — callers
    /// that are restoring a wounded party member overwrite it afterward.
    /// </summary>
    public void ApplyTo(Unit unit)
    {
        if (unit == null) return;

        unit.unitName = unitName;
        unit.maxHP = maxHP;
        unit.currentHP = maxHP;
        unit.attack = attack;
        unit.defense = defense;
        unit.moveRange = moveRange;
        unit.attackRange = attackRange;
    }

    /// <summary>True if this definition can actually produce a unit.</summary>
    public bool IsSpawnable => prefab != null && prefab.GetComponent<Unit>() != null;
}
