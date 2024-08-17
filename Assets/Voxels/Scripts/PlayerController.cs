using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float flySpeed = 12.0f; // Speed when flying
    private bool isFlying = false; // Whether the player is currently flying

    public CharacterController characterController;
    public Transform cameraTransform;

    public float speed = 6.0f;
    public float gravity = -9.81f;
    public float jumpHeight = 2.0f;

    private Vector3 playerVelocity;
    private bool groundedPlayer;
    private Transform playerTransform;

    void Start()
    {
        SetPlayerCenter();
        playerTransform = transform;
    }

    public Vector3 getPlayerPosition()
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
    }

    void Fly()
    {
        // Get input for flying
        float x = Input.GetAxis("Horizontal");
        float y = Input.GetAxis("Jump") - Input.GetAxis("Crouch"); // Space to go up, Crouch (Ctrl) to go down
        float z = Input.GetAxis("Vertical");

        Vector3 flyDirection = cameraTransform.right * x + cameraTransform.up * y + cameraTransform.forward * z;
        transform.position += flyDirection * flySpeed * Time.deltaTime;

        /*if (Input.GetKeyDown(KeyCode.AltGr) || Input.GetKeyDown(KeyCode.LeftAlt))
        {
            isCursorLocked = !isCursorLocked;
            ToggleCursorState();
        }*/
    }

    void SetPlayerCenter() {
        // Calculate the center position of the world
        int worldCenterIndex = World.Instance.worldSize / 2;
        float worldCenterX = worldCenterIndex * World.Instance.chunkSize;
        float worldCenterZ = worldCenterIndex * World.Instance.chunkSize;

        float noiseValue = GlobalNoise.GetGlobalNoiseValue(worldCenterX, worldCenterZ, World.Instance.noiseArray);

        // Normalize noise value to [0, 1]
        float normalizedNoiseValue = (noiseValue + 1) / 2;

        // Calculate maxHeight
        float maxHeight = normalizedNoiseValue * World.Instance.maxHeight;
       
        // Adjust the height for the player's position (assuming the player's capsule collider has a height of 2 units)
        maxHeight += 1.5f; // This ensures that the base of the player is at the terrain height

        // Set the player's position to be on top of the terrain
        transform.position = new Vector3(worldCenterX, maxHeight, worldCenterZ);
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

        characterController.Move(move * Time.deltaTime * speed);

        // Changes the height position of the player..
        if (Input.GetButtonDown("Jump") && groundedPlayer)
        {
            playerVelocity.y += Mathf.Sqrt(jumpHeight * -3.0f * gravity);
        }

        playerVelocity.y += gravity * Time.deltaTime;
        characterController.Move(playerVelocity * Time.deltaTime);
    }
}