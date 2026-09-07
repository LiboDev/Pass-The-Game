using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drops a showcase yard into the open scene: thirteen stations, each one way of wiring the existing
/// interaction scripts - Door, MoveInteractable, RotateInteractable, ScaleInteractable - into doors,
/// gates, lifts, bridges and machines. Nothing here runs at play time; the scene objects it writes are
/// the artifact, so edit them in the editor afterwards. Re-running deletes the old showcase root and
/// rebuilds it, which is why it asks first.
/// </summary>
public static class InteractableShowcaseBuilder
{
    private const string RootName = "Interactable Showcase";
    private const string InteractableTag = "Interactable";
    private const string DoorMaterialPath = "Assets/Materials/Door.mat";
    private const float StationGap = 8f;   // spacing along X
    private const float RowGap = 12f;      // spacing along Z
    private const float FirstRowZ = 10f;   // the player starts near the origin looking down +Z

    private static Material stone, wood, metal, brass, gloom, crystal;

    [MenuItem("Tools/Build Interactable Showcase")]
    public static void Build()
    {
        if (!InternalEditorUtility.tags.Contains(InteractableTag))
        {
            Debug.LogError("The '" + InteractableTag + "' tag is missing - add it under Project Settings > "
                + "Tags and Layers, then run this again.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);
        if (existing != null)
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Rebuild Interactable Showcase",
                    scene.name + " already has an \"" + RootName + "\". Rebuilding replaces it and "
                    + "discards any edits made to it in the editor.",
                    "Replace it",
                    "Cancel"))
            {
                return;
            }

            Undo.DestroyObjectImmediate(existing);
        }

        LoadMaterials();

        GameObject showcase = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(showcase, "Build Interactable Showcase");

        HingedDoor(Station(showcase, 0f, 0f, "01  Hinged Door", "Door"));
        DoubleDoors(Station(showcase, 1f, 0f, "02  Double Doors", "Door x2"));
        SlidingDoor(Station(showcase, 2f, 0f, "03  Sliding Door", "MoveInteractable"));
        Portcullis(Station(showcase, 3f, 0f, "04  Portcullis", "MoveInteractable"));
        RevolvingDoor(Station(showcase, 4f, 0f, "05  Revolving Door", "RotateInteractable"));
        Trapdoor(Station(showcase, 5f, 0f, "06  Trapdoor", "RotateInteractable"));

        FreightLift(Station(showcase, 0f, 1f, "07  Freight Lift", "MoveInteractable"));
        Shuttle(Station(showcase, 1f, 1f, "08  Shuttle Platform", "MoveInteractable"));
        Drawbridge(Station(showcase, 2f, 1f, "09  Drawbridge", "RotateInteractable"));
        Turntable(Station(showcase, 3f, 1f, "10  Turntable Bridge", "RotateInteractable"));
        Idol(Station(showcase, 4f, 1f, "11  Cursed Idol", "ScaleInteractable"));
        ShrinkingWall(Station(showcase, 5f, 1f, "12  Shrinking Wall", "ScaleInteractable"));

        PillarBank(Station(showcase, 2.5f, 2f, "13  Rising Pillars", "MoveInteractable, 5 targets"));

        EnsureInteractor();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = showcase;
        Debug.Log("Built the interactable showcase in " + scene.name + " - save the scene (Ctrl+S).", showcase);
    }

    /// <summary>A slab to stand the station on and a floating label naming the script it demonstrates.</summary>
    private static Transform Station(GameObject showcase, float column, float row, string title, string script)
    {
        GameObject station = new GameObject(title);
        station.transform.SetParent(showcase.transform, false);
        station.transform.localPosition =
            new Vector3((column - 2.5f) * StationGap, 0f, FirstRowZ + row * RowGap);

        Cube("Slab", station.transform, new Vector3(0f, 0.02f, 0f), new Vector3(6.5f, 0.04f, 6.5f),
            stone, collider: false);
        Label(title + "\n" + script, station.transform, new Vector3(0f, 3.6f, -3f));
        return station.transform;
    }

    // ---------------------------------------------------------------- doors

    /// <summary>The plain case: the hinge object owns the tag, the collider and the Door script.</summary>
    private static void HingedDoor(Transform station)
    {
        Frame(station, 1.3f, 2.2f);
        Panel(station, new Vector3(0.5f, 0f, 0f), -1f, 100f);
    }

    /// <summary>Two Doors mirrored around the same frame - each leaf is its own interactable.</summary>
    private static void DoubleDoors(Transform station)
    {
        Frame(station, 2.4f, 2.4f);
        Panel(station, new Vector3(-1.05f, 0f, 0f), 1f, -100f, 1.04f, 2.22f);
        Panel(station, new Vector3(1.05f, 0f, 0f), -1f, 100f, 1.04f, 2.22f);
    }

    /// <summary>MoveInteractable pointed at its own transform: the leaf slides itself into the pocket.</summary>
    private static void SlidingDoor(Transform station)
    {
        Frame(station, 1.3f, 2.2f);
        Cube("Pocket Wall", station.transform, new Vector3(1.6f, 1.1f, 0f), new Vector3(1.2f, 2.2f, 0.35f), stone);
        Cube("Rail", station.transform, new Vector3(0.6f, 2.28f, 0f), new Vector3(2.6f, 0.1f, 0.2f), metal, collider: false);

        GameObject leaf = Cube("Leaf", station.transform, new Vector3(0f, 1.03f, 0f),
            new Vector3(1f, 2.05f, 0.1f), metal);
        leaf.tag = InteractableTag;

        MoveInteractable slide = leaf.AddComponent<MoveInteractable>();
        SetTargets(slide, leaf.transform);
        SetVector(slide, "positionOffset", new Vector3(1.05f, 0f, 0f));
        SetFloat(slide, "unitsPerSecond", 1.6f);
    }

    /// <summary>Same script, offset straight up: the gate lifts into the lintel.</summary>
    private static void Portcullis(Transform station)
    {
        Frame(station, 2.1f, 2.7f);

        GameObject gate = Tagged("Gate", station.transform, new Vector3(0f, 1.15f, 0f),
            Vector3.zero, new Vector3(1.75f, 2.2f, 0.2f));

        for (int i = 0; i < 5; i++)
        {
            Cube("Bar " + i, gate.transform, new Vector3(-0.68f + i * 0.34f, 0f, 0f),
                new Vector3(0.09f, 2.2f, 0.09f), metal, collider: false);
        }

        Cube("Brace Top", gate.transform, new Vector3(0f, 0.95f, 0f), new Vector3(1.75f, 0.1f, 0.1f), metal, collider: false);
        Cube("Brace Low", gate.transform, new Vector3(0f, -0.95f, 0f), new Vector3(1.75f, 0.1f, 0.1f), metal, collider: false);

        MoveInteractable lift = gate.AddComponent<MoveInteractable>();
        SetTargets(lift, gate.transform);
        SetVector(lift, "positionOffset", new Vector3(0f, 2.25f, 0f));
        SetFloat(lift, "unitsPerSecond", 1.2f);
    }

    /// <summary>
    /// Four leaves on a hub that quarter-turns. Only the central column carries a collider, so the
    /// raycast reaches the interactable through the leaves instead of stopping on one.
    /// </summary>
    private static void RevolvingDoor(Transform station)
    {
        Cube("Housing L", station.transform, new Vector3(-1.75f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 2.6f), stone);
        Cube("Housing R", station.transform, new Vector3(1.75f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 2.6f), stone);

        GameObject hub = Tagged("Hub", station.transform, Vector3.zero,
            new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 2.2f, 0.35f));

        Cube("Column", hub.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.3f, 2.2f, 0.3f), brass, collider: false);

        for (int i = 0; i < 4; i++)
        {
            Quaternion yaw = Quaternion.Euler(0f, i * 90f, 0f);
            GameObject leaf = Cube("Leaf " + i, hub.transform,
                yaw * new Vector3(0.8f, 0f, 0f) + new Vector3(0f, 1.05f, 0f),
                new Vector3(1.6f, 2.05f, 0.06f), wood, collider: false);
            leaf.transform.localRotation = yaw;
        }

        RotateInteractable spin = hub.AddComponent<RotateInteractable>();
        SetTargets(spin, hub.transform);
        SetVector(spin, "rotationOffset", new Vector3(0f, 90f, 0f));
        SetFloat(spin, "degreesPerSecond", 70f);
    }

    /// <summary>
    /// A hatch in a deck. The pivot sits on the hinge edge and rolls about Z, so the same script that
    /// yaws a revolving door flips a floor panel up.
    /// </summary>
    private static void Trapdoor(Transform station)
    {
        Cube("Deck N", station.transform, new Vector3(0f, 0.4f, 1.6f), new Vector3(4.4f, 0.2f, 1.2f), stone);
        Cube("Deck S", station.transform, new Vector3(0f, 0.4f, -1.6f), new Vector3(4.4f, 0.2f, 1.2f), stone);
        Cube("Deck W", station.transform, new Vector3(-1.6f, 0.4f, 0f), new Vector3(1.2f, 0.2f, 2f), stone);
        Cube("Deck E", station.transform, new Vector3(1.6f, 0.4f, 0f), new Vector3(1.2f, 0.2f, 2f), stone);
        Cube("Step", station.transform, new Vector3(0f, 0.13f, -2.6f), new Vector3(2f, 0.26f, 0.8f), stone);
        Cube("Shaft", station.transform, new Vector3(0f, 0.03f, 0f), new Vector3(2f, 0.02f, 2f), gloom, collider: false);

        GameObject hatch = Tagged("Hatch", station.transform, new Vector3(-1f, 0.5f, 0f),
            new Vector3(1f, -0.05f, 0f), new Vector3(2f, 0.12f, 2f));

        Cube("Panel", hatch.transform, new Vector3(1f, -0.05f, 0f), new Vector3(2f, 0.1f, 2f), wood, collider: false);
        Cube("Ring", hatch.transform, new Vector3(1.7f, 0.03f, 0f), new Vector3(0.25f, 0.06f, 0.25f), brass, collider: false);

        RotateInteractable flip = hatch.AddComponent<RotateInteractable>();
        SetTargets(flip, hatch.transform);
        SetVector(flip, "rotationOffset", new Vector3(0f, 0f, 80f));
        SetFloat(flip, "degreesPerSecond", 140f);
    }

    // ------------------------------------------------------------- machines

    /// <summary>A lever that moves something else: the script sits on the handle, the target is the platform.</summary>
    private static void FreightLift(Transform station)
    {
        for (int i = 0; i < 4; i++)
        {
            Cube("Post " + i, station.transform,
                new Vector3(i < 2 ? -1.5f : 1.5f, 1.75f, i % 2 == 0 ? -1.5f : 1.5f),
                new Vector3(0.15f, 3.5f, 0.15f), metal);
        }

        Cube("Head", station.transform, new Vector3(0f, 3.5f, 0f), new Vector3(3.2f, 0.15f, 3.2f), metal);

        GameObject platform = Cube("Platform", station.transform, new Vector3(0f, 0.15f, 0f),
            new Vector3(2.6f, 0.2f, 2.6f), metal);
        Cube("Crate", platform.transform, new Vector3(0f, 3f, 0f), new Vector3(0.35f, 4f, 0.35f), wood);

        GameObject lever = Switch("Lever", station.transform, new Vector3(2.4f, 0f, -1.2f));
        MoveInteractable hoist = lever.AddComponent<MoveInteractable>();
        SetTargets(hoist, platform.transform);
        SetVector(hoist, "positionOffset", new Vector3(0f, 3f, 0f));
        SetFloat(hoist, "unitsPerSecond", 1.4f);
    }

    /// <summary>The same script with a horizontal offset - a cart that runs its rail on command.</summary>
    private static void Shuttle(Transform station)
    {
        Cube("Rail L", station.transform, new Vector3(-0.9f, 0.08f, 0.5f), new Vector3(0.16f, 0.16f, 7f), metal);
        Cube("Rail R", station.transform, new Vector3(0.9f, 0.08f, 0.5f), new Vector3(0.16f, 0.16f, 7f), metal);

        GameObject cart = Cube("Cart", station.transform, new Vector3(0f, 0.3f, -2f),
            new Vector3(2.2f, 0.25f, 2.2f), metal);
        Cube("Crate", cart.transform, new Vector3(0f, 1.8f, 0f), new Vector3(0.4f, 3f, 0.4f), wood);

        GameObject button = Switch("Button", station.transform, new Vector3(2.2f, 0f, -2f));
        MoveInteractable run = button.AddComponent<MoveInteractable>();
        SetTargets(run, cart.transform);
        SetVector(run, "positionOffset", new Vector3(0f, 0f, 5f));
        SetFloat(run, "unitsPerSecond", 2.5f);
    }

    /// <summary>
    /// Authored raised: the start rotation is position 1, so the offset that cancels it is the
    /// "lowered" state and the bridge drops when you pull it.
    /// </summary>
    private static void Drawbridge(Transform station)
    {
        Chasm(station);

        GameObject bridge = Tagged("Bridge", station.transform, new Vector3(0f, 0.3f, -2f),
            new Vector3(0f, -0.08f, 2f), new Vector3(2.4f, 0.16f, 4f));
        bridge.transform.localRotation = Quaternion.Euler(-80f, 0f, 0f);

        Cube("Plank", bridge.transform, new Vector3(0f, -0.08f, 2f), new Vector3(2.4f, 0.15f, 4f), wood, collider: false);
        Cube("Chain L", bridge.transform, new Vector3(-1.1f, 0.02f, 2f), new Vector3(0.07f, 0.07f, 4f), brass, collider: false);
        Cube("Chain R", bridge.transform, new Vector3(1.1f, 0.02f, 2f), new Vector3(0.07f, 0.07f, 4f), brass, collider: false);

        RotateInteractable lower = bridge.AddComponent<RotateInteractable>();
        SetTargets(lower, bridge.transform);
        SetVector(lower, "rotationOffset", new Vector3(80f, 0f, 0f));
        SetFloat(lower, "degreesPerSecond", 45f);
    }

    /// <summary>Starts parked alongside the chasm; a quarter turn swings it across.</summary>
    private static void Turntable(Transform station)
    {
        Chasm(station);

        GameObject hub = Tagged("Turntable", station.transform, new Vector3(0f, 0.3f, 0f),
            new Vector3(0f, 0.35f, 0f), new Vector3(0.9f, 0.7f, 0.9f));
        hub.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Cube("Deck", hub.transform, new Vector3(0f, -0.08f, 0f), new Vector3(2.4f, 0.16f, 6f), wood);
        Cube("Capstan", hub.transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.8f, 0.7f, 0.8f), brass, collider: false);

        RotateInteractable swing = hub.AddComponent<RotateInteractable>();
        SetTargets(swing, hub.transform);
        SetVector(swing, "rotationOffset", new Vector3(0f, -90f, 0f));
        SetFloat(swing, "degreesPerSecond", 35f);
    }

    /// <summary>ScaleInteractable on a pedestal, growing the thing standing on it.</summary>
    private static void Idol(Transform station)
    {
        Cube("Plinth", station.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.8f, 0.6f, 1.8f), stone);

        GameObject figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        figure.name = "Idol";
        figure.transform.SetParent(station, false);
        figure.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        figure.transform.localScale = new Vector3(0.5f, 0.55f, 0.5f);
        figure.GetComponent<MeshRenderer>().sharedMaterial = crystal;
        Object.DestroyImmediate(figure.GetComponent<Collider>());

        GameObject altar = Switch("Altar Switch", station.transform, new Vector3(1.9f, 0f, -1.2f));
        ScaleInteractable swell = altar.AddComponent<ScaleInteractable>();
        SetTargets(swell, figure.transform);
        SetVector(swell, "scaleMultiplier", new Vector3(2.6f, 2.6f, 2.6f));
        SetFloat(swell, "unitsPerSecond", 0.6f);
    }

    /// <summary>Scaling one axis to almost nothing: the wall thins out of the way instead of moving.</summary>
    private static void ShrinkingWall(Transform station)
    {
        Cube("Jamb L", station.transform, new Vector3(-2.6f, 1.3f, 0f), new Vector3(0.5f, 2.6f, 0.6f), stone);
        Cube("Jamb R", station.transform, new Vector3(2.6f, 1.3f, 0f), new Vector3(0.5f, 2.6f, 0.6f), stone);

        GameObject wall = Cube("Wall", station.transform, new Vector3(0f, 1.3f, 0f),
            new Vector3(4.8f, 2.6f, 0.4f), stone);

        GameObject dial = Switch("Dial", station.transform, new Vector3(2.6f, 0f, -1.6f));
        ScaleInteractable shrink = dial.AddComponent<ScaleInteractable>();
        SetTargets(shrink, wall.transform);
        SetVector(shrink, "scaleMultiplier", new Vector3(0.06f, 1f, 1f));
        SetFloat(shrink, "unitsPerSecond", 3f);
    }

    /// <summary>One switch, five targets - the whole point of the targets array being a list.</summary>
    private static void PillarBank(Transform station)
    {
        Transform[] pillars = new Transform[5];
        for (int i = 0; i < pillars.Length; i++)
        {
            pillars[i] = Cube("Pillar " + i, station.transform,
                new Vector3(-2.4f + i * 1.2f, 0.75f, 0f), new Vector3(0.9f, 1.5f, 0.9f), stone).transform;
        }

        GameObject master = Switch("Master Switch", station.transform, new Vector3(3.2f, 0f, -1.4f));
        MoveInteractable raise = master.AddComponent<MoveInteractable>();
        SetTargets(raise, pillars);
        SetVector(raise, "positionOffset", new Vector3(0f, 2.5f, 0f));
        SetFloat(raise, "unitsPerSecond", 1.1f);
    }

    // ----------------------------------------------------------- ingredients

    /// <summary>Two posts and a lintel; the inner opening is width - 0.3 wide.</summary>
    private static void Frame(Transform station, float width, float height)
    {
        GameObject frame = new GameObject("Frame");
        frame.transform.SetParent(station, false);

        float post = (width - 0.15f) * 0.5f;
        Cube("Post L", frame.transform, new Vector3(-post, height * 0.5f, 0f), new Vector3(0.15f, height, 0.15f), stone);
        Cube("Post R", frame.transform, new Vector3(post, height * 0.5f, 0f), new Vector3(0.15f, height, 0.15f), stone);
        Cube("Lintel", frame.transform, new Vector3(0f, height - 0.075f, 0f), new Vector3(width, 0.15f, 0.15f), stone);
    }

    /// <summary>
    /// One swinging leaf. The hinge object holds the tag, the collider and the Door script; the mesh is
    /// a collider-less child, so the raycast always lands on the object that has Interact().
    /// </summary>
    private static void Panel(Transform station, Vector3 hinge, float direction, float openAngle,
        float width = 0.98f, float height = 2.05f)
    {
        GameObject door = Tagged("Door", station, hinge,
            new Vector3(direction * width * 0.5f, height * 0.5f, 0f), new Vector3(width, height, 0.1f));

        Cube("Panel", door.transform, new Vector3(direction * width * 0.5f, height * 0.5f, 0f),
            new Vector3(width, height, 0.08f), wood, collider: false);
        Cube("Handle", door.transform, new Vector3(direction * (width - 0.15f), height * 0.5f, -0.09f),
            new Vector3(0.08f, 0.08f, 0.16f), brass, collider: false);

        Door swing = door.AddComponent<Door>();
        SetFloat(swing, "openAngle", openAngle);
    }

    /// <summary>A gap in the floor for the bridge stations - kerbs low enough to step over.</summary>
    private static void Chasm(Transform station)
    {
        Cube("Kerb N", station.transform, new Vector3(0f, 0.15f, 2.6f), new Vector3(6f, 0.3f, 1f), stone);
        Cube("Kerb S", station.transform, new Vector3(0f, 0.15f, -2.6f), new Vector3(6f, 0.3f, 1f), stone);
        Cube("Void", station.transform, new Vector3(0f, 0.05f, 0f), new Vector3(6f, 0.02f, 4.2f), gloom, collider: false);
    }

    /// <summary>A waist-high post to look at: the collider the Interactor raycast has to find.</summary>
    private static GameObject Switch(string name, Transform station, Vector3 localPosition)
    {
        GameObject go = Tagged(name, station, localPosition,
            new Vector3(0f, 0.65f, 0f), new Vector3(0.45f, 1.3f, 0.45f));

        Cube("Post", go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.16f, 1f, 0.16f), metal, collider: false);
        Cube("Knob", go.transform, new Vector3(0f, 1.08f, 0f), new Vector3(0.34f, 0.2f, 0.34f), brass, collider: false);
        return go;
    }

    private static GameObject Tagged(string name, Transform parent, Vector3 localPosition,
        Vector3 colliderCentre, Vector3 colliderSize)
    {
        GameObject go = new GameObject(name);
        go.tag = InteractableTag;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = colliderCentre;
        box.size = colliderSize;
        return go;
    }

    private static GameObject Cube(string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Material material, bool collider = true)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = localScale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!collider)
        {
            Object.DestroyImmediate(cube.GetComponent<Collider>());
        }

        return cube;
    }

    /// <summary>Legacy TextMesh; it reads from the -Z side, which is where the player walks in from.</summary>
    private static void Label(string text, Transform station, Vector3 localPosition)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(station, false);
        go.transform.localPosition = localPosition;

        TextMesh label = go.AddComponent<TextMesh>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 64;
        label.characterSize = 0.11f;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = new Color(0.95f, 0.86f, 0.6f);
        go.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
    }

    // -------------------------------------------------------------- plumbing

    /// <summary>
    /// The showcase is useless without something to look at it with, and SampleScene's player has no
    /// Interactor. Adds one and the HUD it needs, reusing the game scene's; set fields are left alone.
    /// </summary>
    private static void EnsureInteractor()
    {
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller == null)
        {
            Debug.LogWarning("No FirstPersonController in the open scene - the showcase is built, but "
                + "nothing can interact with it yet.");
            return;
        }

        if (!controller.TryGetComponent(out Interactor interactor))
        {
            interactor = Undo.AddComponent<Interactor>(controller.gameObject);
        }

        // The camera rig is a root object, not a child of the player, so look for it by its component.
        PlayerLook look = Object.FindFirstObjectByType<PlayerLook>();
        Transform eye = look != null ? look.transform : (Camera.main != null ? Camera.main.transform : null);
        if (eye != null)
        {
            GameSceneBuilder.FillIfEmpty(interactor, "eye", eye);
        }

        SerializedObject serialized = new SerializedObject(interactor);
        if (serialized.FindProperty("prompt").objectReferenceValue == null)
        {
            GameObject prompt = GameSceneBuilder.BuildHud();
            Undo.RegisterCreatedObjectUndo(prompt.transform.root.gameObject, "Add HUD");
            GameSceneBuilder.FillIfEmpty(interactor, "prompt", prompt);
        }

        GameSceneBuilder.FillIfEmpty(interactor, "interact", GameSceneBuilder.ActionReference("Player/Interact"));
    }

    private static void LoadMaterials()
    {
        stone = Mat("Stone", new Color(0.44f, 0.44f, 0.47f), 0.12f);
        metal = Mat("Metal", new Color(0.32f, 0.34f, 0.38f), 0.65f, 0.8f);
        brass = Mat("Brass", new Color(0.72f, 0.55f, 0.22f), 0.75f, 0.9f);
        gloom = Mat("Gloom", new Color(0.03f, 0.03f, 0.04f), 0f);
        crystal = Mat("Crystal", new Color(0.42f, 0.2f, 0.55f), 0.85f);

        wood = AssetDatabase.LoadAssetAtPath<Material>(DoorMaterialPath);
        if (wood == null)
        {
            wood = Mat("Wood", new Color(0.45f, 0.28f, 0.15f), 0.3f);
        }
    }

    private static Material Mat(string name, Color colour, float smoothness, float metallic = 0f)
    {
        string path = "Assets/Materials/Showcase_" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SetFloat(Object target, string field, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetVector(Object target, string field, Vector3 value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetTargets(Object target, params Transform[] targets)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty array = serialized.FindProperty("targets");
        array.arraySize = targets.Length;
        for (int i = 0; i < targets.Length; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
