using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float flySpeed = 12.0f; // Speed when flying
    private bool isFlying = true; // Whether the player is currently flying

    public CharacterController characterController;
    public Transform cameraTransform;

    public float speed = 6.0f;
    public float gravity = -9.81f;
    public float jumpHeight = 2.0f;

    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private Transform playerTransform;

    // Block interaction settings
    public float interactionDistance = 5f;
    public LayerMask blockLayer;
    public World world; // Reference to the World component

    void Start()
    {
        //SetPlayerCenter();
        playerTransform = transform;
        if (world == null)
        {
            world = FindObjectOfType<World>();
            if (world == null)
            {
                Debug.LogError("World component not found! Please assign it in the inspector.");
            }
        }
    }

    public Vector3 GetPlayerPosition()
    {
        return playerTransform.position; // Return the current position of the player.
    }

    void Update() 
    {
        // Toggle fly mode
        if (Input.GetKeyDown(KeyCode.F)) {
            isFlying = !isFlying;
            characterController.enabled = !isFlying; // Disable the character controller when flying
        }

        if (isFlying) {
            Fly();
        } else {
            PlayerMove();
        }

        // Handle block interaction
        HandleBlockInteraction();
    }

    void HandleBlockInteraction()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactionDistance, blockLayer))
        {
            // Left click to break block
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 blockPos = hit.point - hit.normal * 0.5f;
                world.SetBlock(blockPos, Voxel.VoxelType.Air);
            }
            // Right click to place block
            else if (Input.GetMouseButtonDown(1))
            {
                Vector3 blockPos = hit.point + hit.normal * 0.5f;
                // Don't place block if it would intersect with the player
                if (!WouldBlockIntersectPlayer(blockPos))
                {
                    world.SetBlock(blockPos, Voxel.VoxelType.Stone); // Default to stone, you can change this
                }
            }
        }
    }

    bool WouldBlockIntersectPlayer(Vector3 blockPos)
    {
        // Check if the block position is too close to the player
        Vector3 playerPos = transform.position;
        float minDistance = 0.8f; // Reduced from 1.5f to allow closer block placement

        return Vector3.Distance(blockPos, playerPos) < minDistance;
    }

    void Fly()
    {
        // Get input for flying
        float x = Input.GetAxis("Horizontal");
        float y = Input.GetAxis("Jump") - Input.GetAxis("Crouch"); // Space to go up, Crouch (Ctrl) to go down
        float z = Input.GetAxis("Vertical");

        Vector3 flyDirection = cameraTransform.right * x + cameraTransform.up * y + cameraTransform.forward * z;
        transform.position += flySpeed * Time.deltaTime * flyDirection;

        /*if (Input.GetKeyDown(KeyCode.AltGr) || Input.GetKeyDown(KeyCode.LeftAlt))
        {
            isCursorLocked = !isCursorLocked;
            ToggleCursorState();
        }*/
    }

    void PlayerMove() {
        groundedPlayer = characterController.isGrounded;
        if (groundedPlayer && playerVelocity.y < 0)
        {
            playerVelocity.y = 0f;
        }

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        move = cameraTransform.forward * move.z + cameraTransform.right * move.x;
        move.y = 0; // We do not want to move up/down by the camera's forward vector

        _ = characterController.Move(speed * Time.deltaTime * move);

        // Changes the height position of the player..
        if (Input.GetButtonDown("Jump") && groundedPlayer)
        {
            playerVelocity.y += Mathf.Sqrt(jumpHeight * -3.0f * gravity);
        }

        playerVelocity.y += gravity * Time.deltaTime;
        characterController.Move(playerVelocity * Time.deltaTime);
    }
}