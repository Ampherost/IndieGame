using UnityEngine;

/// <summary>
/// "Hold out for N rounds." Resolves in the acting team's favour once the round counter
/// passes the limit.
///
/// TurnManager evaluates objectives inside CheckCombatEnd, which runs at the start of
/// every phase — so this needs no Update loop and no extra plumbing. The battle resolves
/// on the first phase of round N+1.
///
/// Note this doesn't stop the other side winning first: if the player is wiped out on
/// round 3 of a 5-round survival, TurnManager's team-wipe rule fires and they lose.
/// </summary>
public class SurviveRoundsObjective : CombatObjective
{
    [Tooltip("Survive this many complete rounds.")]
    [Min(1)] public int rounds = 5;

    [Tooltip("Who wins by lasting it out.")]
    public Team survivingTeam = Team.Player;

    public override bool IsResolved(TurnManager turns, out Team? winner)
    {
        winner = null;
        if (turns == null) return false;

        // RoundNumber has already ticked over to rounds + 1 by the time the round after the
        // last one begins, which is the moment the objective is met.
        if (turns.RoundNumber <= rounds) return false;

        winner = survivingTeam;
        return true;
    }

    public override string Describe() => $"{survivingTeam} team survived {rounds} rounds";
}
