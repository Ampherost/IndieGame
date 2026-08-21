using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the player back where they were standing when a battle started, instead of at the
/// scene's authored spawn point.
///
/// Attach to the player object in the overworld / exploration scenes. Does nothing on a
/// fresh start, so the editor position is still the "new game" spawn.
/// </summary>
public class OverworldPlayerPlacer : MonoBehaviour
{
    [Tooltip("Nudge applied to the recorded position, so you don't land exactly on top of " +
             "the NPC you were talking to.")]
    public Vector2 offset = new Vector2(0f, -0.5f);

    private void Start()
    {
        GameData data = GameData.Instance;
        if (data == null || !data.hasReturnPoint) return;

        // The return point belongs to one specific scene. Ignore it anywhere else, so a
        // battle that hands you off to a *different* scene doesn't teleport you oddly.
        if (data.returnSceneName != SceneManager.GetActiveScene().name) return;

        transform.position = data.returnPosition + (Vector3)offset;

        // One-shot: the next scene load shouldn't reuse it.
        data.ClearReturnPoint();
    }
}
