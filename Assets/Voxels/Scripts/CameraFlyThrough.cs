using UnityEngine;

public class CameraFlyThrough : MonoBehaviour
{
    public float turnSpeed = 60.0f;
    public float pitchSensitivity = 2.0f;
    public float yawSensitivity = 2.0f;

    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private bool isCursorLocked = true;

    private void Start()
    {
        ToggleCursorState(); // Initial cursor state setup
    }

    void Update()
    {
        // Toggle cursor visibility and lock state when Control is pressed
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
        {
            isCursorLocked = !isCursorLocked;
            ToggleCursorState();
        }

        if (isCursorLocked)
        {
            // Get the mouse input only when the cursor is locked
            yaw += yawSensitivity * Input.GetAxis("Mouse X");
            pitch -= pitchSensitivity * Input.GetAxis("Mouse Y");

            // Clamp the pitch rotation to prevent flipping
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            // Apply the rotation to the camera
            transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);
        }
    }

    private void ToggleCursorState()
    {
        Cursor.lockState = isCursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isCursorLocked;
    }
}