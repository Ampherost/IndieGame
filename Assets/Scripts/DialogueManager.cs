using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    // DialogueManager controls a Canvas UI made with unity assets Canvas -> Panel -> Text component
    public static DialogueManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject dialoguePanel;
    public TMP_Text dialogueText;

    private string[] currentLines;
    private int index;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        dialoguePanel.SetActive(false);
    }

    public void ShowDialogue(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;

        currentLines = lines;
        index = 0;

        dialoguePanel.SetActive(true);
        dialogueText.text = currentLines[index];
    }

    public void NextLine()
    {
        if (currentLines == null) return;

        index++;

        if (index >= currentLines.Length)
        {
            CloseDialogue();
            return;
        }

        dialogueText.text = currentLines[index];
    }

    public void CloseDialogue()
    {
        dialoguePanel.SetActive(false);
        currentLines = null;
        index = 0;
    }

    public bool IsOpen => dialoguePanel.activeSelf;
}
