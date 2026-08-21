using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The doorway between the overworld and a battle.
///
/// Static rather than a MonoBehaviour on purpose: the object that starts a fight is about
/// to be destroyed by the scene load, so it can't carry the payload across. Statics
/// survive LoadScene; they're reset when you leave play mode, which is exactly the
/// lifetime we want.
///
/// Flow:
///   overworld: BattleLauncher.Begin(encounter)  -> stores payload, loads combat scene
///   combat:    BattleRunner reads Pending       -> spawns the board
///   combat:    BattleRunner.RecordResult(...)   -> stores the outcome, loads return scene
/// </summary>
public static class BattleLauncher
{
    /// <summary>The encounter currently being fought, or null in the overworld.</summary>
    public static EncounterData Pending { get; private set; }

    /// <summary>True once a battle has resolved and the result hasn't been consumed yet.</summary>
    public static bool HasResult { get; private set; }

    /// <summary>Winner of the last battle: Player, Enemy, or null for a draw.</summary>
    public static Team? LastWinner { get; private set; }

    /// <summary>Id of the encounter that produced LastWinner. Useful for "did I already win this?" checks.</summary>
    public static EncounterData LastEncounter { get; private set; }

    public static bool LastBattleWasVictory => HasResult && LastWinner == Team.Player;

    /// <summary>
    /// Start a battle. Records where the player was standing so they can be put back, then
    /// loads the combat scene.
    /// </summary>
    public static void Begin(EncounterData encounter)
    {
        if (encounter == null)
        {
            Debug.LogError("[BattleLauncher] Begin was called with no encounter assigned.");
            return;
        }

        if (!encounter.Validate()) return;

        Pending = encounter;
        HasResult = false;
        LastWinner = null;

        GameData data = GameData.Instance;
        if (data != null)
        {
            string back = string.IsNullOrWhiteSpace(encounter.returnSceneName)
                ? SceneManager.GetActiveScene().name
                : encounter.returnSceneName;

            data.SetReturnPoint(back, FindPlayerPosition());
        }
        else
        {
            Debug.LogWarning("[BattleLauncher] No GameData in the scene. The battle will start " +
                             "with no party, and there's nowhere to return to. Drop the GameData " +
                             "prefab into this scene.");
        }

        Debug.Log($"[BattleLauncher] Starting '{encounter.name}' -> {encounter.combatSceneName}");
        SceneManager.LoadScene(encounter.combatSceneName);
    }

    /// <summary>Called by BattleRunner once TurnManager reports the battle is over.</summary>
    public static void RecordResult(EncounterData encounter, Team? winner)
    {
        LastEncounter = encounter;
        LastWinner = winner;
        HasResult = true;
    }

    /// <summary>Clear the payload once we're safely back in the overworld.</summary>
    public static void ClearPending()
    {
        Pending = null;
    }

    /// <summary>
    /// Where the player is standing right now. Uses PlayerMovement (which only the player
    /// has) and falls back to the built-in Player tag.
    /// </summary>
    private static Vector3 FindPlayerPosition()
    {
        var mover = Object.FindFirstObjectByType<PlayerMovement>();
        if (mover != null) return mover.transform.position;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.transform.position;

        Debug.LogWarning("[BattleLauncher] Couldn't find the player to record a return " +
                         "position. They'll come back at the scene's default spot.");
        return Vector3.zero;
    }
}
