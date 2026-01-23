using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PlayerInteractions : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("Press this key to interact (temporary test).")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("Only objects on this layer can be interacted with (optional filter).")]
    public LayerMask interactablesLayer;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    // Current interactable in range
    private IInteractable current;

    private void Awake()
    {
        // Ensure this collider is a trigger (interaction range)
        Collider2D col = GetComponent<Collider2D>();
        if (!col.isTrigger)
            col.isTrigger = true;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(interactKey))
            return;

        // If dialogue is currently open, advance it
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsOpen)
        {
            if (enableDebugLogs)
                Debug.Log("[Interact] Advancing dialogue");

            DialogueManager.Instance.NextLine();
            return;
        }

        // Otherwise, try interacting with something in range
        TryInteract();
    }


    private void OnTriggerEnter2D(Collider2D other)
    {
        // Optional layer filter (keeps your old behavior)
        if (interactablesLayer.value != 0 &&
            ((1 << other.gameObject.layer) & interactablesLayer.value) == 0)
            return;

        if (other.TryGetComponent<IInteractable>(out var interactable))
        {
            current = interactable;

            if (enableDebugLogs)
                Debug.Log($"[Interact] In range: {other.name}");
        }
        else
        {
            // Helpful when debugging layers/colliders
            if (enableDebugLogs)
                Debug.Log($"[Interact] Entered range but object has no IInteractable: {other.name}");
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // Optional layer filter again
        if (interactablesLayer.value != 0 &&
            ((1 << other.gameObject.layer) & interactablesLayer.value) == 0)
            return;

        if (other.TryGetComponent<IInteractable>(out var interactable) && interactable == current)
        {
            if (enableDebugLogs)
                Debug.Log($"[Interact] Out of range: {other.name}");

            current = null;
        }
    }

    // Keep this function-style API from version 2
    public void TryInteract()
    {
        if (current != null)
        {
            if (enableDebugLogs)
                Debug.Log($"[Interact] TryInteract() -> {((MonoBehaviour)current).name}");

            current.Interact(gameObject);
        }
        else
        {
            if (enableDebugLogs)
                Debug.Log("[Interact] TryInteract() -> No interactable in range.");
        }
    }

    public bool HasInteractable => current != null;
}


