using UnityEngine;

public class CameraFlyThrough : MonoBehaviour
{
    public float turnSpeed = 60.0f;
    public float pitchSensitivity = 2.0f;
    public float yawSensitivity = 2.0f;

    private float yaw = 0.0f;
    private float pitch = 0.0f;

    void Update()
    {
        Cursor.lockState = CursorLockMode.Locked;

        // Get the mouse input only when the cursor is locked
        yaw += yawSensitivity * Input.GetAxis("Mouse X");
        pitch -= pitchSensitivity * Input.GetAxis("Mouse Y");

        // Clamp the pitch rotation to prevent flipping
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        // Apply the rotation to the camera
        transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);
    }
}