using UnityEngine;

/// <summary>
/// Drop this on an NPC (or anything else) and point a DialogueChoice's onOptionA at
/// EncounterTrigger.Begin(). That replaces the SceneManagerScript.LoadCombat() wiring,
/// which loads the combat scene but tells it nothing about the fight.
///
/// Everything about the battle lives in the EncounterData asset, so one NPC can start a
/// tutorial skirmish and another a boss fight with no code between them.
/// </summary>
public class EncounterTrigger : MonoBehaviour
{
    [Tooltip("The fight this object starts.")]
    public EncounterData encounter;

    [Header("Repeat")]
    [Tooltip("If true, this fight can only be won once — later attempts do nothing.")]
    public bool onceOnly = true;

    [Tooltip("Optional. What to do instead when the fight has already been won " +
             "(e.g. show a different line of dialogue).")]
    public UnityEngine.Events.UnityEvent onAlreadyCleared;

    /// <summary>True once this encounter has been won in this play session.</summary>
    public bool Cleared { get; private set; }

    private void Awake()
    {
        // Coming back from the fight we started: notice that we won it.
        if (encounter != null &&
            BattleLauncher.HasResult &&
            BattleLauncher.LastEncounter == encounter &&
            BattleLauncher.LastBattleWasVictory)
        {
            Cleared = true;
        }
    }

    /// <summary>Hook this up to DialogueChoice.onOptionA in the inspector.</summary>
    public void Begin()
    {
        if (encounter == null)
        {
            Debug.LogError($"[EncounterTrigger] '{name}' has no encounter assigned, so there's " +
                           $"nothing to start.", this);
            return;
        }

        if (onceOnly && Cleared)
        {
            Debug.Log($"[EncounterTrigger] '{encounter.name}' has already been won.");
            onAlreadyCleared?.Invoke();
            return;
        }

        BattleLauncher.Begin(encounter);
    }
}
