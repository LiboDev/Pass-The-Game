using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse look, living on a camera rig that is NOT parented to the player. The rig copies the player's
/// eye target each frame and owns its own rotation, so the Rigidbody's interpolated pose can never
/// fight the camera - the usual source of jitter on a physics-driven first person controller.
///
/// Yaw is written to <see cref="orientation"/>, a yaw-only transform under the player that
/// <see cref="FirstPersonController"/> uses as its movement basis. The player's Rigidbody itself is
/// never rotated at all.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("Yaw-only transform under the player; the controller moves along its forward")]
    private Transform orientation;
    [SerializeField, Tooltip("Transform under the player this rig sits on; it drops when crouching")]
    private Transform eyeTarget;
    [SerializeField] private InputActionReference look;

    [Header("Tuning")]
    [SerializeField, Min(0f), Tooltip("Degrees per mouse pixel")] private float lookSensitivity = 0.1f;
    [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;

    private float yaw;
    private float pitch;

    /// <summary>Camera roll in degrees. Nothing drives it yet; a wall run or strafe tilt would.</summary>
    public float Roll { get; set; }

    /// <summary>Where the player is facing. Movement follows this, so it is the player's heading.</summary>
    public float Yaw => yaw;

    /// <summary>
    /// Points the player somewhere without touching the mouse - respawning at a spawn point, say.
    /// The Rigidbody's own rotation is meaningless here, so this is the only way to turn the player.
    /// </summary>
    public void SetYaw(float degrees)
    {
        yaw = degrees;
        ApplyYaw();
    }

    private void Awake()
    {
        if (!orientation || !eyeTarget || !look)
        {
            Debug.LogError("PlayerLook needs orientation, eyeTarget and look assigned", this);
            enabled = false;
            return;
        }

        yaw = orientation.eulerAngles.y;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Yaw lands in Update because FixedUpdate reads it for the movement direction; waiting until
    // LateUpdate would move the player along last frame's heading.
    private void Update()
    {
        // Mouse deltas are already per frame, so no deltaTime on look.
        Vector2 delta = look.action.ReadValue<Vector2>() * lookSensitivity;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        ApplyYaw();
    }

    // The camera pose is set in LateUpdate: by then Rigidbody interpolation has already smoothed the
    // player's transform for this frame, so following it here is what keeps the view steady.
    private void LateUpdate()
    {
        transform.SetPositionAndRotation(eyeTarget.position, Quaternion.Euler(pitch, yaw, Roll));
    }

    private void ApplyYaw()
    {
        orientation.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
}
