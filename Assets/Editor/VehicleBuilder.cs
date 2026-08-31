using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Adds a driveable vehicle to the open scene: chassis Rigidbody + box collider, four WheelColliders
/// with a posed cylinder each, and a wired VehicleController. Placing four wheels by hand is the
/// fiddly part, so it happens here; the object it leaves behind is an ordinary scene object to edit
/// and save. Runs again freely - each run adds another vehicle.
/// </summary>
public static class VehicleBuilder
{
    private const float WheelRadius = 0.35f;
    private const float SuspensionDistance = 0.3f;
    private const float TrackHalfWidth = 0.8f;   // wheel centres either side of the chassis
    private const float WheelbaseHalf = 1.4f;    // front and rear axles either side of the origin
    private const string InteractableTag = "Interactable";

    [MenuItem("Tools/Build Vehicle In Open Scene")]
    public static void Build()
    {
        if (!InternalEditorUtility.tags.Contains(InteractableTag))
        {
            InternalEditorUtility.AddTag(InteractableTag);
        }

        // The Interactor raycasts for this tag and finds the IInteractable on the same object: the
        // chassis collider and the VehicleController both live on the root, so getting in works.
        GameObject vehicle = new GameObject("Vehicle") { tag = InteractableTag };

        // Wheels hang from the origin, so the body rides at rest height above the ground.
        Vector3 restHeight = Vector3.up * (WheelRadius + SuspensionDistance * 0.5f);
        FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
        vehicle.transform.position = player != null
            ? player.transform.position + player.transform.forward * 5f + restHeight
            : restHeight;

        Rigidbody body = vehicle.AddComponent<Rigidbody>();
        body.mass = 1200f;
        body.linearDamping = 0.05f;   // coasting slows down eventually; the wheels have no engine braking
        body.angularDamping = 0.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // Body and cabin as two boxes rather than one: the shape matches what you see, and the cabin
        // gives the interact raycast something to hit at standing eye height.
        Slab(vehicle, "Body", new Vector3(0f, 0.35f, 0f), new Vector3(1.4f, 0.7f, 3.6f));
        Slab(vehicle, "Cabin", new Vector3(0f, 0.95f, -0.2f), new Vector3(1.3f, 0.5f, 1.6f));

        VehicleController controller = vehicle.AddComponent<VehicleController>();
        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty wheels = serialized.FindProperty("wheels");
        wheels.arraySize = 4;

        Wheel(wheels.GetArrayElementAtIndex(0), vehicle.transform, "Wheel FL", -TrackHalfWidth, WheelbaseHalf, true, false);
        Wheel(wheels.GetArrayElementAtIndex(1), vehicle.transform, "Wheel FR", TrackHalfWidth, WheelbaseHalf, true, false);
        Wheel(wheels.GetArrayElementAtIndex(2), vehicle.transform, "Wheel RL", -TrackHalfWidth, -WheelbaseHalf, false, true);
        Wheel(wheels.GetArrayElementAtIndex(3), vehicle.transform, "Wheel RR", TrackHalfWidth, -WheelbaseHalf, false, true);

        serialized.FindProperty("drive").objectReferenceValue = GameSceneBuilder.ActionReference("Player/Move");
        serialized.FindProperty("handbrake").objectReferenceValue = GameSceneBuilder.ActionReference("Player/Jump");
        serialized.FindProperty("interact").objectReferenceValue = GameSceneBuilder.ActionReference("Player/Interact");

        // Driver seat inside the cabin (the player stands on it, eyes at ~1.6 m) and a spot beside the
        // car to step out onto, clear of the wheels.
        serialized.FindProperty("player").objectReferenceValue = player;
        serialized.FindProperty("seat").objectReferenceValue =
            Marker("Seat", vehicle.transform, new Vector3(-0.35f, -0.5f, 0.1f));
        serialized.FindProperty("exit").objectReferenceValue =
            Marker("Exit", vehicle.transform, new Vector3(-1.5f, -0.5f, 0f));
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (player == null)
        {
            Debug.LogWarning("No FirstPersonController in the open scene - assign the vehicle Player by hand.");
        }

        Undo.RegisterCreatedObjectUndo(vehicle, "Build Vehicle");
        EditorSceneManager.MarkSceneDirty(vehicle.scene);
        Selection.activeGameObject = vehicle;
        Debug.Log("Built " + vehicle.name + " in " + vehicle.scene.name + " - save the scene (Ctrl+S).", vehicle);
    }

    /// <summary>
    /// One corner: the WheelCollider, and under it a hub the controller poses plus a cylinder turned
    /// on its side to be the tyre. The hub exists because the pose spins the wheel about its X axis
    /// while a Unity cylinder stands up its Y axis.
    /// </summary>
    private static void Wheel(SerializedProperty element, Transform vehicle, string name, float x, float z, bool steers, bool drives)
    {
        GameObject corner = new GameObject(name);
        corner.transform.SetParent(vehicle, false);
        corner.transform.localPosition = new Vector3(x, 0f, z);

        WheelCollider wheel = corner.AddComponent<WheelCollider>();
        wheel.radius = WheelRadius;
        wheel.suspensionDistance = SuspensionDistance;
        wheel.mass = 20f;
        wheel.wheelDampingRate = 0.25f;

        GameObject hub = new GameObject("Hub");
        hub.transform.SetParent(corner.transform, false);

        GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tyre.name = "Tyre";
        tyre.transform.SetParent(hub.transform, false);
        tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        tyre.transform.localScale = new Vector3(WheelRadius * 2f, 0.125f, WheelRadius * 2f);
        Object.DestroyImmediate(tyre.GetComponent<Collider>());   // the WheelCollider is the collision

        element.FindPropertyRelative("collider").objectReferenceValue = wheel;
        element.FindPropertyRelative("visual").objectReferenceValue = hub.transform;
        element.FindPropertyRelative("steers").boolValue = steers;
        element.FindPropertyRelative("drives").boolValue = drives;
    }

    private static Transform Marker(string name, Transform parent, Vector3 localPosition)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = localPosition;
        return marker.transform;
    }

    /// <summary>A visible slab of car and its collider; the collider goes on the root, next to the Rigidbody.</summary>
    private static void Slab(GameObject vehicle, string name, Vector3 centre, Vector3 size)
    {
        BoxCollider box = vehicle.AddComponent<BoxCollider>();
        box.center = centre;
        box.size = size;

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(vehicle.transform, false);
        cube.transform.localPosition = centre;
        cube.transform.localScale = size;
        Object.DestroyImmediate(cube.GetComponent<Collider>());   // the box on the root replaces it
    }
}
