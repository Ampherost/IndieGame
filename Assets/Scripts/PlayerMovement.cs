using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerCollisions))]
public class PlayerMovement : MonoBehaviour
{
    [Tooltip("Movement speed in units/sec")]
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    public Vector2 MoveInputReference => moveInput;
    public bool IsMoving => moveInput.sqrMagnitude > 0f;

    // import collisions script
    private PlayerCollisions collisions;



    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        inputActions = new PlayerInputActions();

        // Subscribe to the Move action
        inputActions.Player.Move.performed += OnMovePerformed;
        inputActions.Player.Move.canceled += OnMoveCanceled;

        // collisions
        collisions = GetComponent<PlayerCollisions>();
        collisions.OnSolidCollisionEnter += HandleWallHit;
    }

    void OnEnable()
    {
        inputActions.Enable();
    }

    void OnDisable()
    {
        inputActions.Disable();
    }

    private bool DialogueOpen =>
    DialogueManager.Instance != null && DialogueManager.Instance.IsOpen;

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        if (DialogueOpen)
            return; // ignore input while dialogue is open

        moveInput = ctx.ReadValue<Vector2>();
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        moveInput = Vector2.zero;
    }

    private void HandleWallHit(Collision2D col)
    {
        // stop movement immediately
        rb.linearVelocity = Vector2.zero;
    }

    void OnDestroy()
    {
        collisions.OnSolidCollisionEnter -= HandleWallHit;
    }

    void FixedUpdate()
    {
        if (DialogueOpen)
        {
            moveInput = Vector2.zero; // prevents “walking in place” animation
            rb.linearVelocity = Vector2.zero;
            return;
        }


        rb.linearVelocity = moveInput * moveSpeed;
    }

}

