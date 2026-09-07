using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A four-wheeled vehicle riding on Unity's WheelColliders: every wheel keeps its own suspension
/// spring and friction curves, so ride height, weight transfer, traction loss and slide all come out
/// of the physics engine instead of being faked here. This component only feeds the wheels steering,
/// motor and brake torque, and poses the wheel meshes from the colliders.
///
/// Suspension is derived from the Rigidbody's mass in <see cref="Awake"/> - ride frequency and damping
/// ratio rather than raw spring numbers - so a heavier car does not sink through its travel and a
/// lighter one does not pogo. Whatever the WheelColliders show in the Inspector is overwritten.
///
/// The player drives it by looking at it and interacting - the Interactor calls <see cref="Interact"/>;
/// pressing the same key while seated gets back out. Parked, it holds the brakes on instead of rolling.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class VehicleController : MonoBehaviour, IInteractable
{
    [System.Serializable]
    private class Wheel
    {
        public WheelCollider collider;
        [Tooltip("Mesh posed from the collider each step, so it shows suspension travel and wheel spin")]
        public Transform visual;
        public bool steers;
        public bool drives;
    }

    [Header("References")]
    [SerializeField] private Wheel[] wheels = new Wheel[4];
    [SerializeField, Tooltip("Vector2: Y throttles and reverses, X steers")] private InputActionReference drive;
    [SerializeField] private InputActionReference handbrake;
    [SerializeField, Tooltip("The same action the Interactor uses - read here to get back out")]
    private InputActionReference interact;

    [Header("Driver")]
    [SerializeField, Tooltip("The player allowed to drive this")] private FirstPersonController player;
    [SerializeField, Tooltip("Where the driver stands to drive; the camera ends up ~1.6 m above it")]
    private Transform seat;
    [SerializeField, Tooltip("Where the driver is put down on getting out")] private Transform exit;

    [Header("Engine")]
    [SerializeField, Min(0f), Tooltip("Nm at each driven wheel at full throttle")] private float motorTorque = 900f;
    [SerializeField, Min(0f), Tooltip("Nm at every wheel while braking")] private float brakeTorque = 3000f;
    [SerializeField, Min(1f), Tooltip("m/s the motor torque fades to nothing at")] private float topSpeed = 25f;

    [Header("Steering")]
    [SerializeField, Range(0f, 45f)] private float maxSteerAngle = 30f;
    [SerializeField, Min(1f), Tooltip("Degrees per second the front wheels turn towards the input")]
    private float steerRate = 90f;

    [Header("Suspension and grip (applied to every wheel on Awake)")]
    [SerializeField, Range(0.5f, 3f), Tooltip("Hz the body bounces at: ~1.2 soft, ~2.5 stiff")]
    private float rideFrequency = 1.4f;
    [SerializeField, Range(0.1f, 1.5f), Tooltip("1 = critically damped; below that the car floats")]
    private float dampingRatio = 0.6f;
    [SerializeField, Min(0f), Tooltip("Grip along the wheel: traction and braking")] private float forwardGrip = 1.6f;
    [SerializeField, Min(0f), Tooltip("Grip across the wheel: lower slides the tail out")] private float sidewaysGrip = 1.8f;
    [SerializeField, Tooltip("Local centre of mass; low and central is what stops it rolling over")]
    private Vector3 centreOfMass = new Vector3(0f, -0.2f, 0f);

    private Rigidbody body;
    private Rigidbody playerBody;
    private Interactor playerInteractor;
    private Grapple playerGrapple;
    private Vector2 input;
    private float steerAngle;
    private bool driving;
    private int enteredFrame;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (!drive || !handbrake || !interact || !player || !seat || !exit || wheels.Length == 0
            || System.Array.Exists(wheels, wheel => wheel == null || wheel.collider == null))
        {
            Debug.LogError("VehicleController needs drive, handbrake, interact, player, seat, exit and a "
                + "WheelCollider on every wheel", this);
            enabled = false;
            return;
        }

        playerBody = player.GetComponent<Rigidbody>();
        playerInteractor = player.GetComponent<Interactor>();
        playerGrapple = player.GetComponent<Grapple>(); // optional; the player may have no tethers
        body.centerOfMass = centreOfMass;

        float sprungMass = body.mass / wheels.Length;
        foreach (Wheel wheel in wheels)
        {
            Tune(wheel.collider, sprungMass);
        }
    }

    /// <summary>
    /// Textbook spring on a quarter of the car: k = m(2*pi*f)^2 holds the body at the chosen bounce
    /// frequency, c = 2*zeta*sqrt(k*m) damps it. Rest position sits mid-travel so a wheel can both
    /// drop into a dip and compress over a bump.
    /// </summary>
    private void Tune(WheelCollider wheel, float sprungMass)
    {
        float stiffness = sprungMass * Mathf.Pow(2f * Mathf.PI * rideFrequency, 2f);

        JointSpring spring = wheel.suspensionSpring;
        spring.spring = stiffness;
        spring.damper = 2f * dampingRatio * Mathf.Sqrt(stiffness * sprungMass);
        spring.targetPosition = 0.5f;
        wheel.suspensionSpring = spring;

        // Stiffness scales the whole friction curve, which is the one knob worth exposing: the curve
        // shape PhysX ships (peak grip just past the slip point, then falling away) is already right.
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = forwardGrip;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideways = wheel.sidewaysFriction;
        sideways.stiffness = sidewaysGrip;
        wheel.sidewaysFriction = sideways;
    }

    /// <summary>Called by the player's Interactor when the car is looked at: gets in and takes the wheel.</summary>
    public void Interact()
    {
        if (driving)
        {
            return;
        }

        driving = true;
        enteredFrame = Time.frameCount; // one key press must not get straight back out again
        player.LookOnly = true;            // the camera rig is its own object, so looking carries on
        playerInteractor.enabled = false;  // its raycast starts inside the car; getting out is read below

        if (playerGrapple)
        {
            playerGrapple.ReleaseAll(); // a joint on a body about to go kinematic drags the car with it
        }

        // Kinematic and deaf to collisions, so the player neither shoves the car about from the seat nor
        // falls through its floor. The Rigidbody stays enabled: parenting below is what carries it along.
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        playerBody.isKinematic = true;
        playerBody.detectCollisions = false;

        player.transform.SetParent(seat);
        player.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private void GetOut()
    {
        driving = false;
        input = Vector2.zero;

        player.transform.SetParent(null);

        // Nothing to restore about the facing: PlayerLook kept its own yaw for the whole ride, and
        // this body's rotation is frozen and never read. Moved while still kinematic, then released.
        player.transform.position = exit.position;

        playerBody.isKinematic = false;
        playerBody.detectCollisions = true;
        playerBody.linearVelocity = Vector3.zero; // getting out of a moving car must not fling the player
        playerBody.angularVelocity = Vector3.zero;
        playerInteractor.enabled = true;
        player.LookOnly = false;
    }

    private void Update()
    {
        if (!driving)
        {
            return;
        }

        if (Time.frameCount != enteredFrame && interact.action.WasPressedThisFrame())
        {
            GetOut();
            return;
        }

        input = drive.action.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        float speed = Vector3.Dot(body.linearVelocity, transform.forward);

        // Parked it sits on the brakes; throttle held against the direction of travel is the brake
        // pedal, not reverse gear.
        bool braking = !driving || handbrake.action.IsPressed()
            || (input.y * speed < 0f && Mathf.Abs(speed) > 1f);

        // Torque tapers off towards top speed; drag alone would let it accelerate forever.
        float torque = braking ? 0f : motorTorque * input.y * Mathf.Clamp01(1f - Mathf.Abs(speed) / topSpeed);
        steerAngle = Mathf.MoveTowards(steerAngle, input.x * maxSteerAngle, steerRate * Time.fixedDeltaTime);

        foreach (Wheel wheel in wheels)
        {
            if (wheel.steers)
            {
                wheel.collider.steerAngle = steerAngle;
            }

            wheel.collider.motorTorque = wheel.drives ? torque : 0f;
            wheel.collider.brakeTorque = braking ? brakeTorque : 0f;

            if (wheel.visual)
            {
                wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
                wheel.visual.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
