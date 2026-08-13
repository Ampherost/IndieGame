using UnityEngine;

/// <summary>
/// "Defeat the commander" — the battle resolves the moment one named unit falls,
/// regardless of how many mooks are still standing.
///
/// Flip winsWhenBossFalls to Team.Enemy and assign a player unit to get the escort /
/// protect-the-lord version of the same rule.
/// </summary>
public class DefeatBossObjective : CombatObjective
{
    [Tooltip("The unit whose death resolves the battle.")]
    public Unit boss;

    [Tooltip("Which team wins when that unit falls. Set to Enemy for escort objectives.")]
    public Team winsWhenBossFalls = Team.Player;

    // Distinguishes "boss was assigned and has since been destroyed" from "boss was never
    // assigned". Without this, an empty inspector slot reads as a dead boss and hands out
    // victory on the first frame.
    private bool bossAssigned;

    private void Awake()
    {
        if (boss == null)
        {
            Debug.LogError("[DefeatBossObjective] No boss assigned. Disabling this objective " +
                           "so it can't end the battle immediately.", this);
            enabled = false;
            return;
        }
        bossAssigned = true;
    }

    public override bool IsResolved(TurnManager turns, out Team? winner)
    {
        winner = null;
        if (!bossAssigned) return false;

        // null here means the GameObject was destroyed outright, which counts as gone.
        if (boss != null && boss.IsAlive) return false;

        winner = winsWhenBossFalls;
        return true;
    }

    public override string Describe()
    {
        string who = boss != null ? boss.unitName : "the objective unit";
        return winsWhenBossFalls == Team.Player
            ? $"{who} was defeated"
            : $"{who} was lost";
    }
}
