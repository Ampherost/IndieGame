using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class PlayerInteractions : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("Press this key to interact (temporary test).")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("Only objects on this layer can be interacted with.")]
    public LayerMask interactablesLayer;

    // The interactable object currently in range (for testing)
    private GameObject currentTarget;

    private void Awake()
    {
        // Make sure our collider is a trigger (since this is a proximity detector)
        Collider2D col = GetComponent<Collider2D>();
        if (!col.isTrigger)
            col.isTrigger = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            if (currentTarget != null)
            {
                Debug.Log($"Interacted with: {currentTarget.name}");
            }
            else
            {
                Debug.Log("No NPC in range to interact with.");
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Filter by layer mask
        if (((1 << other.gameObject.layer) & interactablesLayer) == 0)
            return;

        // For now: just pick the first thing that enters range
        currentTarget = other.gameObject;

        Debug.Log($"NPC in range: {currentTarget.name}");
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject == currentTarget)
        {
            Debug.Log($"NPC out of range: {currentTarget.name}");
            currentTarget = null;
        }
    }
}

