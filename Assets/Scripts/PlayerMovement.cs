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

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
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
        // Apply velocity; Rigidbody2D.gravityScale should be 0
        rb.linearVelocity = moveInput * moveSpeed;
    }

}

