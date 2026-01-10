using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class PlayerCollisions : MonoBehaviour
{
    [Tooltip("Which layers count as 'solid' for collision responses.")]
    public LayerMask solidObjectsLayer;

    public LayerMask interactablesLayer;

    /// Fired once when you first collide with a solid object.
    public event Action<Collision2D> OnSolidCollisionEnter;

    /// Fired once when you stop colliding with that solid object.
    public event Action<Collision2D> OnSolidCollisionExit;

    // keep track of all solid colliders we're in contact with
    private readonly HashSet<Collider2D> activeSolids = new HashSet<Collider2D>();

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // only care about collisions on the solidObjectsLayer
        if (IsInLayerMask(collision.gameObject.layer, solidObjectsLayer | interactablesLayer))
        {
            // if this is our first contact with this collider…
            if (activeSolids.Add(collision.collider))
                OnSolidCollisionEnter?.Invoke(collision);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (activeSolids.Remove(collision.collider))
            OnSolidCollisionExit?.Invoke(collision);
    }

    /// <summary>
    /// Helper to test a layer against a mask.
    /// </summary>
    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    /// <summary>
    /// True if the player is currently touching at least one solid object.
    /// </summary>
    public bool IsTouchingSolid => activeSolids.Count > 0;
}
