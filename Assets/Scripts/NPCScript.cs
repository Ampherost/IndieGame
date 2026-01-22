using UnityEngine;

public class NPCScript : MonoBehaviour, IInteractable
{
    [TextArea]
    public string[] lines;

    public void Interact(GameObject interactor)
    {
        DialogueManager.Instance.ShowDialogue(lines);
    }
}
