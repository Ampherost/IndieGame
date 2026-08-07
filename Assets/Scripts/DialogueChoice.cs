using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A yes/no (or any two-option) question that can be appended to the end of a
/// dialogue. Plain [Serializable] class, not a MonoBehaviour or ScriptableObject,
/// so it shows up as an inline foldout on whatever component holds one.
///
/// The outcomes are UnityEvents: DialogueManager only knows "run outcome A" or
/// "run outcome B". What those actually do — load a scene, open a shop, start a
/// quest — is wired per-NPC in the inspector and stays out of the dialogue code.
/// </summary>
[Serializable]
public class DialogueChoice
{
    [TextArea]
    [Tooltip("The question shown on the choice panel, e.g. \"Do you want to fight?\"")]
    public string question = "Do you want to fight?";

    [Header("Option A")]
    [Tooltip("Label drawn on the first button.")]
    public string optionALabel = "Yes";
    [Tooltip("What happens when the player picks option A.")]
    public UnityEvent onOptionA;

    [Header("Option B")]
    [Tooltip("Label drawn on the second button.")]
    public string optionBLabel = "No";
    [Tooltip("What happens when the player picks option B. Leave empty to just close the dialogue.")]
    public UnityEvent onOptionB;

    /// <summary>
    /// True if this choice has enough filled in to be worth showing. Used so an
    /// untouched, default-constructed instance doesn't produce a blank panel.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(question) &&
        !string.IsNullOrWhiteSpace(optionALabel) &&
        !string.IsNullOrWhiteSpace(optionBLabel);
}