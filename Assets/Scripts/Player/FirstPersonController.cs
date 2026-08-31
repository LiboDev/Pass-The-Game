using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Walk, look, jump and crouch on a CharacterController. Yaw turns this body, pitch turns the camera
/// pivot. Reads the project-wide actions through references wired in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private InputActionReference move;
    [SerializeField] private InputActionReference look;
    [SerializeField] private InputActionReference jump;
    [SerializeField] private InputActionReference crouch;
    [SerializeField, Tooltip("Optional capsule mesh kept scaled to the CharacterController (see FitCapsule)")]
    private Transform bodyVisual;

    [Header("Tuning")]
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("Metres the jump peaks at")] private float jumpHeight = 1.2f;
    [SerializeField, Tooltip("Negative = down, m/s²")] private float gravity = -20f;
    [SerializeField, Min(0f), Tooltip("Degrees per mouse pixel")] private float lookSensitivity = 0.1f;
    [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;

    [Header("Crouch")]
    [SerializeField, Min(0.1f), Tooltip("Capsule height while crouched; standing height is taken from the CharacterController")]
    private float crouchHeight = 1f;
    [SerializeField, Range(0f, 1f)] private float crouchSpeedMultiplier = 0.5f;
    [SerializeField, Min(0f), Tooltip("Metres per second the capsule shrinks or grows")]
    private float crouchTransitionSpeed = 8f;

    private CharacterController controller;
    private float standHeight;
    private float standEyeHeight;
    private float pitch;
    private float verticalVelocity;

    public bool IsCrouching => controller.height < standHeight - 0.01f;

    /// <summary>
    /// Set while something else owns the player's position - riding in a vehicle, say. Mouse look keeps
    /// working; walking, crouching and gravity stop, so the disabled CharacterController is left alone.
    /// </summary>
    public bool LookOnly { get; set; }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!cameraPivot || !move || !look || !jump || !crouch)
        {
            Debug.LogError("FirstPersonController needs cameraPivot, move, look, jump and crouch assigned", this);
            enabled = false;
            return;
        }

        standHeight = controller.height;
        standEyeHeight = cameraPivot.localPosition.y;
        if (bodyVisual)
        {
            FitCapsule(bodyVisual, controller);
        }
    }

    /// <summary>Scales a Unity capsule primitive (height 2, radius 0.5) onto the controller's capsule.</summary>
    public static void FitCapsule(Transform capsule, CharacterController controller)
    {
        capsule.localPosition = controller.center;
        capsule.localScale = new Vector3(controller.radius * 2f, controller.height * 0.5f, controller.radius * 2f);
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Look();
        if (LookOnly)
        {
            return;
        }

        Crouch();
        Move();
    }

    private void Look()
    {
        // Mouse deltas are already per frame, so no deltaTime on look.
        Vector2 lookDelta = look.action.ReadValue<Vector2>() * lookSensitivity;
        transform.Rotate(0f, lookDelta.x, 0f);
        pitch = Mathf.Clamp(pitch - lookDelta.y, -maxPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>Held crouch. Standing back up waits until nothing is above the head.</summary>
    private void Crouch()
    {
        float targetHeight = crouch.action.IsPressed() || !HasHeadroom() ? crouchHeight : standHeight;
        float height = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        if (Mathf.Approximately(height, controller.height))
        {
            return;
        }

        // Centre follows height so the capsule shrinks from the top and the feet stay put.
        controller.height = height;
        controller.center = new Vector3(0f, height * 0.5f, 0f);
        cameraPivot.localPosition = new Vector3(0f, standEyeHeight - (standHeight - height), 0f);
        if (bodyVisual)
        {
            FitCapsule(bodyVisual, controller);
        }
    }

    private void Move()
    {
        Vector2 input = move.action.ReadValue<Vector2>();
        float speed = IsCrouching ? walkSpeed * crouchSpeedMultiplier : walkSpeed;
        Vector3 planar = (transform.right * input.x + transform.forward * input.y) * speed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f; // small downward push keeps isGrounded stable on slopes and steps
            }

            if (jump.action.IsPressed()) // held, not pressed: landing while holding Space jumps again
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move((planar + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    /// <summary>Sweeps the capsule's top sphere up to standing height; the player's own capsule overlaps at the start and is ignored.</summary>
    private bool HasHeadroom()
    {
        if (controller.height >= standHeight)
        {
            return true;
        }

        float radius = controller.radius;
        Vector3 topSphere = transform.position + Vector3.up * (controller.height - radius);
        return !Physics.SphereCast(topSphere, radius * 0.9f, Vector3.up, out _, standHeight - controller.height,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }
}
