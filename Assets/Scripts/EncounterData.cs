using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How a battle is won.</summary>
public enum VictoryCondition
{
    /// <summary>Kill everything. Handled by TurnManager's built-in team-wipe rule.</summary>
    RoutEnemies,

    /// <summary>Kill the one enemy marked 'isBoss'. Spawns a DefeatBossObjective.</summary>
    DefeatBoss,

    /// <summary>Stay alive for N rounds. Spawns a SurviveRoundsObjective.</summary>
    SurviveRounds
}

/// <summary>What happens when the player loses.</summary>
public enum DefeatAction
{
    /// <summary>Send them back to the overworld, bruised. Forgiving; good default.</summary>
    ReturnToOverworld,

    /// <summary>Reload the battle with the party's pre-battle HP restored.</summary>
    RetryBattle,

    /// <summary>Load a dedicated game over scene.</summary>
    LoadGameOverScene
}

/// <summary>
/// Everything a battle needs to know about itself: which map, which enemies and where,
/// how it's won, and where the player goes afterward.
///
/// One asset per fight. An NPC points its DialogueChoice at an EncounterTrigger holding
/// one of these, and the whole fight is authored in the inspector with no new code.
///
/// Create with: Assets -> Create -> SRPG -> Encounter
/// </summary>
[CreateAssetMenu(fileName = "NewEncounter", menuName = "SRPG/Encounter")]
public class EncounterData : ScriptableObject
{
    /// <summary>One enemy on the board: who they are and which cell they start on.</summary>
    [Serializable]
    public class EnemySpawn
    {
        public UnitDefinition definition;

        [Tooltip("Grid cell to spawn on. These are GridManager cell coordinates, not world " +
                 "position — (0,0) is the bottom-left of the painted map.")]
        public Vector2Int cell;

        [Tooltip("Optional. Overrides the definition's name, e.g. 'Bandit Captain'.")]
        public string nameOverride;

        [Tooltip("Only meaningful when the victory condition is Defeat Boss.")]
        public bool isBoss;
    }

    [Header("Scenes")]
    [Tooltip("The combat scene to load for this fight.")]
    public string combatSceneName = "CombatScene";

    [Tooltip("Where to go when the battle ends. Leave blank to return to whichever scene " +
             "the player launched the fight from.")]
    public string returnSceneName = "";

    [Header("Player Deployment")]
    [Tooltip("Cells the party spawns on, in order. The first deployable party member takes " +
             "the first cell, and so on. Also caps how many units you can field.")]
    public List<Vector2Int> playerSpawnCells = new List<Vector2Int>();

    [Tooltip("Hard cap on deployed units, on top of the number of spawn cells.")]
    [Min(1)] public int maxDeployed = 4;

    [Header("Enemies")]
    public List<EnemySpawn> enemies = new List<EnemySpawn>();

    [Header("Win / Loss")]
    public VictoryCondition victory = VictoryCondition.RoutEnemies;

    [Tooltip("Only used by Survive Rounds.")]
    [Min(1)] public int roundsToSurvive = 5;

    public DefeatAction onDefeat = DefeatAction.ReturnToOverworld;

    [Tooltip("Only used by Load Game Over Scene.")]
    public string gameOverSceneName = "MainMenuScene";

    /// <summary>Sanity check, logged once when the encounter launches.</summary>
    public bool Validate()
    {
        bool ok = true;

        if (string.IsNullOrWhiteSpace(combatSceneName))
        {
            Debug.LogError($"[Encounter] '{name}' has no combat scene name.", this);
            ok = false;
        }

        if (playerSpawnCells == null || playerSpawnCells.Count == 0)
        {
            Debug.LogError($"[Encounter] '{name}' has no player spawn cells — the party has " +
                           $"nowhere to stand.", this);
            ok = false;
        }

        if (enemies == null || enemies.Count == 0)
            Debug.LogWarning($"[Encounter] '{name}' has no enemies. The battle will resolve " +
                             $"the moment it starts.", this);

        if (victory == VictoryCondition.DefeatBoss && FindBossSpawn() == null)
        {
            Debug.LogError($"[Encounter] '{name}' is a Defeat Boss battle but no enemy has " +
                           $"'Is Boss' ticked.", this);
            ok = false;
        }

        return ok;
    }

    public EnemySpawn FindBossSpawn()
    {
        if (enemies == null) return null;
        foreach (var e in enemies)
            if (e != null && e.isBoss && e.definition != null) return e;
        return null;
    }
}
