using UnityEngine;

/// <summary>
/// Combat-scene camera control:
///   - Starts attached to an assigned protagonist unit (centers on it).
///   - The player can attach the camera to any unit (e.g. by selecting it);
///     the camera then follows that unit, including while it moves.
///   - Arrow keys / WASD pan freely to scout, temporarily detaching from the unit.
///   - A recenter key re-attaches to the protagonist.
///
/// Roams freely with no bounds. Attach to the combat Camera and assign the
/// protagonist in the inspector.
/// </summary>
public class CombatCameraController : MonoBehaviour
{
    [Header("Focus")]
    [Tooltip("The unit the camera attaches to when the scene starts, and the target of the recenter key.")]
    public Unit protagonist;

    [Tooltip("Distance from the board on the Z axis. For 2D orthographic this is typically negative, e.g. -10.")]
    public float cameraZ = -10f;

    [Header("Follow")]
    [Tooltip("How quickly the camera glides toward the unit it's attached to.")]
    public float followLerpSpeed = 8f;

    [Tooltip("If true, the camera jumps to a newly attached unit instead of gliding.")]
    public bool instantAttach = false;

    [Header("Panning")]
    [Tooltip("Manual pan speed in world units per second. Panning detaches from the followed unit.")]
    public float panSpeed = 8f;

    [Tooltip("Hold to pan faster.")]
    public KeyCode fastKey = KeyCode.LeftShift;
    public float fastMultiplier = 2.5f;

    [Header("Recenter")]
    [Tooltip("Press to re-attach the camera to the protagonist.")]
    public KeyCode recenterKey = KeyCode.F;

    // The unit the camera is currently attached to (null == free / detached by panning).
    private Unit followTarget;

    private void Start()
    {
        // Begin attached to the protagonist.
        if (protagonist != null)
        {
            AttachTo(protagonist, snapImmediately: true);
        }
    }

    private void Update()
    {
        // Manual panning takes priority and detaches from any followed unit.
        Vector2 pan = ReadPanInput();
        if (pan != Vector2.zero)
        {
            followTarget = null;   // detach — player is scouting
            float speed = panSpeed * (Input.GetKey(fastKey) ? fastMultiplier : 1f);
            transform.position += new Vector3(pan.x, pan.y, 0f) * speed * Time.deltaTime;
        }

        // Recenter re-attaches to the protagonist.
        if (protagonist != null && Input.GetKeyDown(recenterKey))
            AttachTo(protagonist, snapImmediately: instantAttach);
    }

    private void LateUpdate()
    {
        // Follow the attached unit (in LateUpdate so we track its post-movement position).
        if (followTarget == null || !followTarget.gameObject.activeInHierarchy) return;

        Vector3 goal = FocusPosition(followTarget.transform.position);
        transform.position = Vector3.Lerp(
            transform.position, goal, followLerpSpeed * Time.deltaTime);
    }

    /// <summary>Attach the camera to a unit so it stays centered on it. Public so other scripts can call it.</summary>
    public void AttachTo(Unit unit, bool snapImmediately = false)
    {
        if (unit == null) return;
        followTarget = unit;
        if (snapImmediately)
            transform.position = FocusPosition(unit.transform.position);
    }

    /// <summary>Convenience: attach using the controller's default glide behavior.</summary>
    public void FocusOn(Unit unit)
    {
        AttachTo(unit, snapImmediately: instantAttach);
    }

    private Vector2 ReadPanInput()
    {
        float x = 0f, y = 0f;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) x -= 1f;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) y += 1f;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) y -= 1f;
        return new Vector2(x, y).normalized;
    }

    /// <summary>Camera position that centers the given world point, preserving cameraZ.</summary>
    private Vector3 FocusPosition(Vector3 worldPoint)
    {
        return new Vector3(worldPoint.x, worldPoint.y, cameraZ);
    }
}