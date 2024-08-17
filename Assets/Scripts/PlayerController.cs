using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicPlayerController : MonoBehaviour
{
    public float mouseSensitivity = 2.0f;
    public float moveSpeed = 17f;
    public float jumpForce = 8.0f;

    private float verticalRotation = 0f;
    private Rigidbody rb;
    private float origMoveSpeed;

    private void Start() {
        rb = GetComponent<Rigidbody>();
        origMoveSpeed = moveSpeed;
    }

    private void Update() {
        HandleMouseLook();
        HandleJump();
    }

    void FixedUpdate()
    {
        HandlePlayerMovement();
    }

    void HandleMouseLook()
    {
        Cursor.lockState = CursorLockMode.Locked;
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);

        transform.Rotate(Vector3.up * mouseX);
        Camera.main.transform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    void HandlePlayerMovement()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical).normalized;
        Vector3 moveVelocity = transform.TransformDirection(moveDirection) * moveSpeed;

        rb.velocity = new Vector3(moveVelocity.x, rb.velocity.y, moveVelocity.z);

        if (Input.GetKey(KeyCode.LeftShift))
        {
            moveSpeed = origMoveSpeed + 10;
        }
        else if (!Input.GetKey(KeyCode.LeftShift))
        {
            moveSpeed = origMoveSpeed;
        }
    }

    void HandleJump()
    {
        if (Input.GetButtonDown("Jump") && IsGrounded())
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, 1.1f);
    }
}
