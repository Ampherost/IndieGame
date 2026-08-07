using UnityEngine;

/// <summary>
/// An interactable NPC. Says its lines, and optionally ends with a two-option
/// question whose outcomes are wired per-NPC in the inspector.
/// </summary>
public class NPCScript : MonoBehaviour, IInteractable
{
    [TextArea]
    public string[] lines;

    [Header("Ending Choice (optional)")]
    [Tooltip("Tick this to end the conversation with a yes/no question. " +
             "Leave it off and this NPC behaves exactly as before.")]
    public bool endsWithChoice = false;

    [Tooltip("Only used when 'Ends With Choice' is ticked.")]
    public DialogueChoice choice = new DialogueChoice();

    public void Interact(GameObject interactor)
    {
        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning($"[NPC] '{name}' was interacted with, but there is no DialogueManager " +
                             $"in the scene.", this);
            return;
        }

        DialogueManager.Instance.ShowDialogue(lines, ActiveChoice());
    }

    /// <summary>
    /// The choice to append, or null for plain dialogue.
    ///
    /// Note the explicit bool: Unity always serializes a non-null instance for a plain
    /// [Serializable] class field, so 'choice' is never null at runtime and can't itself
    /// signal "no choice configured".
    /// </summary>
    private DialogueChoice ActiveChoice()
    {
        if (!endsWithChoice) return null;

        if (choice == null || !choice.IsConfigured)
        {
            Debug.LogWarning($"[NPC] '{name}' has 'Ends With Choice' ticked but the question or one of " +
                             $"the option labels is blank. Skipping the choice.", this);
            return null;
        }

        return choice;
    }
}