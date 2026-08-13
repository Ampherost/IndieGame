using UnityEngine;

/// <summary>
/// One win/loss rule for a battle.
///
/// TurnManager sweeps for these on Awake and evaluates them inside CheckCombatEnd, which
/// runs whenever the board changes — a death, a unit leaving the roster, or the start of
/// a phase. That means turn-limit and survival rules work without any extra plumbing.
///
/// Note the contract is "resolves the battle", not "wins it": an escort failure and a
/// boss kill are the same shape, they just report different winners.
///
/// Attach subclasses anywhere in the CombatScene. On maps where an objective is the real
/// win condition, consider turning off TurnManager.autoEndWhenTeamWipedOut — or leave it
/// on so a total party kill still registers as a loss.
/// </summary>
public abstract class CombatObjective : MonoBehaviour
{
    [Tooltip("Lower evaluates first. Only matters when two objectives could resolve on " +
             "the same frame with different winners.")]
    public int priority = 0;

    /// <summary>
    /// True to resolve the battle now. 'winner' may be null for a draw.
    /// Called frequently — keep it cheap and side-effect free.
    /// </summary>
    public abstract bool IsResolved(TurnManager turns, out Team? winner);

    /// <summary>Human-readable description, used in the log line when the objective fires.</summary>
    public virtual string Describe() => GetType().Name;
}
