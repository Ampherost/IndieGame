using UnityEngine;

public class PlayerAnimationScript : MonoBehaviour
{
    private Animator animator;  // reference to our animator component
    private PlayerMovement playerMovement; // reference to our movement component

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // grab the current input vector
        Vector2 input = playerMovement.MoveInputReference;
        if (input != Vector2.zero)
        {
            animator.SetFloat("MoveX", input.x);
            animator.SetFloat("MoveY", input.y);
        }
        

    }
}
