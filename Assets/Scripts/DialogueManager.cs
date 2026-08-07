using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Drives two panels: the normal dialogue panel (Canvas -> Panel -> TMP Text) and
/// an optional choice panel (question label + two Buttons).
///
/// The choice panel is generic: labels and onClick handlers are assigned in code at
/// display time, so one panel in the scene serves every NPC. Listeners are cleared
/// before being re-added, so repeated interactions never stack handlers.
///
/// This class knows nothing about combat, scenes, or quests — outcomes arrive as
/// UnityEvents on the DialogueChoice and are simply invoked.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("Dialogue UI")]
    public GameObject dialoguePanel;
    public TMP_Text dialogueText;

    [Header("Choice UI (optional — only needed if any NPC ends with a question)")]
    [Tooltip("Separate GameObject from dialoguePanel. Holds the question and both buttons.")]
    public GameObject choicePanel;
    public TMP_Text choiceQuestionText;
    public Button optionAButton;
    public Button optionBButton;

    [Tooltip("Optional. Leave empty and the label is found automatically in the button's children.")]
    public TMP_Text optionAText;
    [Tooltip("Optional. Leave empty and the label is found automatically in the button's children.")]
    public TMP_Text optionBText;

    private string[] currentLines;
    private int index;
    private DialogueChoice pendingChoice;

    /// <summary>True while the player must pick one of the two buttons.</summary>
    public bool IsAwaitingChoice { get; private set; }

    /// <summary>
    /// True while any part of the dialogue flow owns the screen. Deliberately includes
    /// the choice state, so callers that only check IsOpen can't re-trigger an NPC
    /// interaction while a question is on screen.
    /// </summary>
    public bool IsOpen => (dialoguePanel != null && dialoguePanel.activeSelf) || IsAwaitingChoice;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (dialoguePanel == null)
            Debug.LogWarning("[DialogueManager] No dialoguePanel assigned — dialogue cannot be shown. " +
                             "Assign it in the inspector.", this);
        else
            dialoguePanel.SetActive(false);

        if (dialogueText == null)
            Debug.LogWarning("[DialogueManager] No dialogueText assigned — lines cannot be displayed. " +
                             "Assign the TMP_Text in the inspector.", this);

        if (choicePanel != null)
            choicePanel.SetActive(false);
    }

    // ---- Public API ----

    /// <summary>
    /// Show a run of lines, optionally ending with a two-option question.
    /// Pass choice = null (the default) for plain dialogue — behaviour is unchanged.
    /// </summary>
    public void ShowDialogue(string[] lines, DialogueChoice choice = null)
    {
        bool hasLines = lines != null && lines.Length > 0;
        bool hasChoice = choice != null && choice.IsConfigured;

        if (!hasLines && !hasChoice) return;

        // Drop anything left over from a previous interaction.
        HideChoicePanel();

        pendingChoice = hasChoice ? choice : null;
        currentLines = hasLines ? lines : null;
        index = 0;

        if (!hasLines)
        {
            // A choice with no preamble: go straight to the question.
            ShowChoicePanel();
            return;
        }

        if (dialoguePanel == null || dialogueText == null)
        {
            Debug.LogWarning("[DialogueManager] ShowDialogue was called but dialoguePanel or " +
                             "dialogueText is not assigned. Nothing will be displayed.", this);
            return;
        }

        dialoguePanel.SetActive(true);
        dialogueText.text = currentLines[index];
    }

    /// <summary>
    /// Advance to the next line. Past the last line, shows the pending choice if there
    /// is one, otherwise closes as before. Does nothing while a choice is on screen —
    /// the buttons are the only way forward.
    /// </summary>
    public void NextLine()
    {
        if (IsAwaitingChoice) return;
        if (currentLines == null) return;

        index++;

        if (index >= currentLines.Length)
        {
            if (pendingChoice != null)
            {
                ShowChoicePanel();
                return;
            }

            CloseDialogue();
            return;
        }

        if (dialogueText != null)
            dialogueText.text = currentLines[index];
    }

    /// <summary>Tear everything down and return control to the player.</summary>
    public void CloseDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        HideChoicePanel();

        currentLines = null;
        pendingChoice = null;
        index = 0;
    }

    // ---- Choice handling ----

    private void ShowChoicePanel()
    {
        DialogueChoice choice = pendingChoice;

        if (choice == null)
        {
            CloseDialogue();
            return;
        }

        if (choicePanel == null || choiceQuestionText == null ||
            optionAButton == null || optionBButton == null)
        {
            Debug.LogWarning("[DialogueManager] A dialogue ended with a choice, but the choice UI is " +
                             "incomplete (need choicePanel, choiceQuestionText, optionAButton and " +
                             "optionBButton). Closing the dialogue instead.", this);
            CloseDialogue();
            return;
        }

        // The question replaces the dialogue box. If you'd rather keep the last line
        // visible behind the buttons, delete the next two lines.
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        choiceQuestionText.text = choice.question;

        ConfigureButton(optionAButton, optionAText, choice.optionALabel,
                        () => ResolveChoice(choice.onOptionA));
        ConfigureButton(optionBButton, optionBText, choice.optionBLabel,
                        () => ResolveChoice(choice.onOptionB));

        choicePanel.SetActive(true);
        IsAwaitingChoice = true;
    }

    /// <summary>
    /// Point a button at one outcome. Existing listeners are removed first so handlers
    /// from earlier NPCs (or earlier conversations with this one) can't accumulate.
    /// </summary>
    private void ConfigureButton(Button button, TMP_Text assignedLabel, string text, UnityAction handler)
    {
        TMP_Text label = assignedLabel != null
            ? assignedLabel
            : button.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.text = text;
        else
            Debug.LogWarning($"[DialogueManager] Button '{button.name}' has no TMP_Text child and no " +
                             $"label assigned, so its text can't be set.", button);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(handler);
        button.interactable = true;
    }

    /// <summary>
    /// A button was clicked. The dialogue is fully torn down *before* the outcome runs,
    /// so an outcome that loads a scene or starts new dialogue isn't fighting a panel
    /// that still thinks it's open.
    /// </summary>
    private void ResolveChoice(UnityEvent outcome)
    {
        if (!IsAwaitingChoice) return;   // guards a double-click landing twice

        CloseDialogue();

        outcome?.Invoke();
    }

    private void HideChoicePanel()
    {
        IsAwaitingChoice = false;

        if (optionAButton != null) optionAButton.onClick.RemoveAllListeners();
        if (optionBButton != null) optionBButton.onClick.RemoveAllListeners();
        if (choicePanel != null) choicePanel.SetActive(false);
    }
}