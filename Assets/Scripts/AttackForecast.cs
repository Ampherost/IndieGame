/// <summary>
/// The outcome of one attack: what it *would* do (forecast) and what it *did* do
/// (resolution) are the same numbers, produced by the same code path.
///
/// Unit.PreviewAttack() builds one of these without touching any state; Unit.Attack()
/// builds one and then applies it. That's the whole point — a forecast that lies to the
/// player is worse than no forecast, and the only reliable way to stop it lying is to
/// make sure there's exactly one place the math lives.
/// </summary>
public struct AttackForecast
{
    public Unit attacker;
    public Unit target;

    /// <summary>False when the attack isn't legal (null, dead, same team, out of range).</summary>
    public bool isValid;

    /// <summary>Raw damage the strike deals, before HP clamping. This is the number to show.</summary>
    public int damage;

    public int targetHPBefore;
    public int targetHPAfter;
    public bool targetDies;

    /// <summary>True if the target survives and can reach back.</summary>
    public bool targetCounters;
    public int counterDamage;

    public int attackerHPBefore;
    public int attackerHPAfter;
    public bool attackerDies;

    /// <summary>HP actually removed from the target (differs from 'damage' on overkill).</summary>
    public int ActualDamage => targetHPBefore - targetHPAfter;

    /// <summary>HP actually removed from the attacker by the counter.</summary>
    public int ActualCounterDamage => attackerHPBefore - attackerHPAfter;

    public string TargetHPText => $"{targetHPBefore} -> {targetHPAfter}";
    public string AttackerHPText => $"{attackerHPBefore} -> {attackerHPAfter}";

    /// <summary>One-line summary of the counter, ready for a forecast panel.</summary>
    public string CounterText
    {
        get
        {
            if (!isValid) return "-";
            if (targetDies) return "No counter (defeated)";
            if (!targetCounters) return "No counter";
            return $"Counters for {counterDamage}";
        }
    }
}
