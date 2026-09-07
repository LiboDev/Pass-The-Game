using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Authors Assets/Scenes/game.unity once, as real serialized GameObjects: player + camera, ground, an
/// interactable door, and the HUD (crosshair + interact prompt). Nothing here runs at play time - the
/// scene it writes is the artifact. Edit the scene from then on; re-running replaces it, which is why
/// it asks first.
/// </summary>
public static class GameSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/game.unity";
    private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string DoorMaterialPath = "Assets/Materials/Door.mat";
    private const string InteractableTag = "Interactable";
    private const string PetObjectName = "Pet";
    private const string PetSpeechObjectName = "Pet Speech";
    private const float EyeDropFromTop = 0.4f; // eye height = capsule height - this, so 1.6 m on a 2 m player
    private const string FrictionlessPath = "Assets/Settings/PlayerFrictionless.physicsMaterial";
    private const string CameraRigName = "Camera Rig";
    private const string PlayerLayerName = "Player";
    private const string LeftTetherMaterialPath = "Assets/Materials/TetherLeft.mat";
    private const string RightTetherMaterialPath = "Assets/Materials/TetherRight.mat";
    // Warm on the left, cool on the right: which line is which has to be readable at a glance mid-swing.
    private static readonly Color LeftTetherColour = new Color(1f, 0.55f, 0.15f, 1f);
    private static readonly Color RightTetherColour = new Color(0.25f, 0.78f, 1f, 1f);
    private const string InteractPromptText = "[F] Interact"; // E belongs to the right hook now
    private const string OrientationName = "Orientation";
    private const string EyeTargetName = "EyeTarget";

    [MenuItem("Tools/Build Game Scene")]
    public static void Build()
    {
        if (!Application.isBatchMode
            && System.IO.File.Exists(ScenePath)
            && !EditorUtility.DisplayDialog(
                "Rebuild Game Scene",
                ScenePath + " already exists. Rebuilding replaces it and discards any edits "
                + "made to it in the editor.",
                "Replace it",
                "Cancel"))
        {
            return;
        }

        if (!InternalEditorUtility.tags.Contains(InteractableTag))
        {
            InternalEditorUtility.AddTag(InteractableTag);
        }

        BuildScene();
    }

    /// <summary>
    /// Brings the player in the open scene up to the Rigidbody rig: swaps a leftover CharacterController
    /// for a Rigidbody and CapsuleCollider, adds the Orientation and EyeTarget children, lifts the camera
    /// out from under the player into its own rig, and fills whatever references are still empty. Fields
    /// that are already set are left alone, so this is safe to run again whenever a new field appears.
    /// </summary>
    [MenuItem("Tools/Wire Player In Open Scene")]
    public static void WirePlayer()
    {
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller == null)
        {
            Debug.LogError("No FirstPersonController in the open scene.");
            return;
        }

        Transform player = controller.transform;
        CapsuleCollider capsule = MigrateCapsule(controller);
        ConfigureBody(controller.gameObject);
        ApplyPlayerLayer(controller);

        Transform body = player.Find("Body");
        if (body == null)
        {
            body = BuildBody(player, capsule);
            Undo.RegisterCreatedObjectUndo(body.gameObject, "Add Player Body");
        }

        Undo.RecordObject(body, "Fit Player Body");
        FirstPersonController.FitCapsule(body, capsule);

        Transform orientation = Child(player, OrientationName, Vector3.zero);
        Transform eyeTarget = Child(player, EyeTargetName, new Vector3(0f, capsule.height - EyeDropFromTop, 0f));
        PlayerLook look = BuildCameraRig(controller, eyeTarget);

        FillIfEmpty(controller, "orientation", orientation);
        FillIfEmpty(controller, "eyeTarget", eyeTarget);
        FillIfEmpty(controller, "bodyVisual", body);
        FillIfEmpty(controller, "move", ActionReference("Player/Move"));
        FillIfEmpty(controller, "jump", ActionReference("Player/Jump"));
        FillIfEmpty(controller, "crouch", ActionReference("Player/Crouch"));

        if (look != null)
        {
            FillIfEmpty(look, "orientation", orientation);
            FillIfEmpty(look, "eyeTarget", eyeTarget);
            FillIfEmpty(look, "look", ActionReference("Player/Look"));
        }

        Grapple grapple = BuildGrapple(controller, look != null ? look.transform : null);
        RelabelInteractPrompt();

        if (controller.TryGetComponent(out Interactor interactor))
        {
            FillIfEmpty(interactor, "eye", look != null ? look.transform : null);
            FillIfEmpty(interactor, "interact", ActionReference("Player/Interact"));
        }

        // Its player field used to be the CharacterController, so the type change emptied it.
        DungeonLevelManager levels = Object.FindFirstObjectByType<DungeonLevelManager>();
        if (levels != null)
        {
            FillIfEmpty(levels, "player", controller.GetComponent<Rigidbody>());
            FillIfEmpty(levels, "look", look);
            FillIfEmpty(levels, "grapple", grapple);
        }

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log("Wired " + controller.name + " in " + controller.gameObject.scene.name + " - save the scene (Ctrl+S).", controller);
    }

    /// <summary>
    /// Replaces a CharacterController with a CapsuleCollider of the same shape, keeping the height and
    /// radius the scene was tuned with. Crouch and headroom both measure from the feet, so the capsule
    /// stands on the pivot.
    /// </summary>
    private static CapsuleCollider MigrateCapsule(FirstPersonController controller)
    {
        float height = 2f;
        float radius = 0.4f;
        if (controller.TryGetComponent(out CharacterController legacy))
        {
            height = legacy.height;
            radius = legacy.radius;
            Undo.DestroyObjectImmediate(legacy);
        }

        if (!controller.TryGetComponent(out CapsuleCollider capsule))
        {
            capsule = Undo.AddComponent<CapsuleCollider>(controller.gameObject);
        }

        Undo.RecordObject(capsule, "Shape Player Capsule");
        capsule.height = height;
        capsule.radius = radius;
        capsule.center = new Vector3(0f, height * 0.5f, 0f);
        capsule.sharedMaterial = Frictionless();
        return capsule;
    }

    /// <summary>
    /// Zero friction on the player, combined by Minimum so no surface can override it. Without this the
    /// capsule grabs every wall it touches in the air and refuses to slide off slopes.
    /// </summary>
    private static PhysicsMaterial Frictionless()
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(FrictionlessPath);
        if (material != null)
        {
            return material;
        }

        material = new PhysicsMaterial("PlayerFrictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };
        System.IO.Directory.CreateDirectory("Assets/Settings");
        AssetDatabase.CreateAsset(material, FrictionlessPath);
        return material;
    }

    /// <summary>The controller sets these at play time too; doing it here makes the scene readable.</summary>
    private static Rigidbody ConfigureBody(GameObject player)
    {
        if (!player.TryGetComponent(out Rigidbody body))
        {
            body = Undo.AddComponent<Rigidbody>(player);
        }

        Undo.RecordObject(body, "Configure Player Body");
        body.mass = 70f;
        body.useGravity = false;      // FirstPersonController applies its own
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.freezeRotation = true;   // PlayerLook owns the facing
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return body;
    }

    /// <summary>
    /// Puts the player on its own layer and takes that layer out of the ground mask. This is not a nicety:
    /// the ground probe is a sphere cast that starts inside the player's own capsule, so a mask that still
    /// contains the player hits it every step and the controller never registers as grounded.
    /// </summary>
    private static void ApplyPlayerLayer(FirstPersonController controller)
    {
        int layer = EnsurePlayerLayer();
        if (layer < 0)
        {
            Debug.LogWarning("No free layer slot for " + PlayerLayerName + " - put the player on its own "
                + "layer and clear that layer from the controller's Ground Mask by hand.", controller);
            return;
        }

        Undo.RecordObject(controller.gameObject, "Set Player Layer");
        controller.gameObject.layer = layer;

        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty mask = serialized.FindProperty("groundMask");
        if (mask.intValue == 0 || mask.intValue == -1) // Nothing or Everything: never configured
        {
            mask.intValue = ~(1 << layer);
            serialized.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Adds the two tethers: a muzzle at each shoulder for the lines to leave from, a world-space
    /// LineRenderer each, and the action references. Aiming is wired to the camera rig rather than a
    /// muzzle, so hooks go where the crosshair is.
    /// </summary>
    private static Grapple BuildGrapple(FirstPersonController controller, Transform rig)
    {
        if (!controller.TryGetComponent(out Grapple grapple))
        {
            grapple = Undo.AddComponent<Grapple>(controller.gameObject);
        }

        Transform player = controller.transform;
        float shoulder = 0.25f;
        float shoulderHeight = 1.4f;

        FillIfEmpty(grapple, "eye", rig);
        FillIfEmpty(grapple, "launch", ActionReference("Player/GrappleLaunch"));
        FillIfEmpty(grapple, "left.fire", ActionReference("Player/GrappleLeft"));
        FillIfEmpty(grapple, "right.fire", ActionReference("Player/GrappleRight"));
        FillIfEmpty(grapple, "left.muzzle", Child(player, "MuzzleLeft", new Vector3(-shoulder, shoulderHeight, 0f)));
        FillIfEmpty(grapple, "right.muzzle", Child(player, "MuzzleRight", new Vector3(shoulder, shoulderHeight, 0f)));
        FillIfEmpty(grapple, "left.line", TetherLine(player, "Tether Left", LeftTetherMaterialPath, LeftTetherColour));
        FillIfEmpty(grapple, "right.line", TetherLine(player, "Tether Right", RightTetherMaterialPath, RightTetherColour));

        // The hooks must not bite the player they are fired from.
        SerializedObject serialized = new SerializedObject(grapple);
        SerializedProperty mask = serialized.FindProperty("grappleMask");
        if (mask.intValue == 0 || mask.intValue == -1)
        {
            mask.intValue = ~(1 << controller.gameObject.layer);
            serialized.ApplyModifiedProperties();
        }

        return grapple;
    }

    /// <summary>
    /// The HUD prompt is authored once and then lives in the scene, so moving Interact off E leaves it
    /// advertising a key that no longer does anything. Only the stale text is touched.
    /// </summary>
    private static void RelabelInteractPrompt()
    {
        foreach (Text label in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label.text == "[E] Interact")
            {
                Undo.RecordObject(label, "Relabel Interact Prompt");
                label.text = InteractPromptText;
            }
        }
    }

    /// <summary>
    /// A thin world-space line, off until a hook is out. The colour is reapplied every run rather than
    /// only on creation, so re-wiring a scene built before the two tethers were told apart recolours it.
    /// </summary>
    private static LineRenderer TetherLine(Transform player, string name, string materialPath, Color colour)
    {
        Transform existing = player.Find(name);
        LineRenderer line;
        if (existing != null && existing.TryGetComponent(out LineRenderer found))
        {
            line = found;
            Undo.RecordObject(line, "Colour " + name);
        }
        else
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(player, false);
            Undo.RegisterCreatedObjectUndo(host, "Add " + name);

            line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true; // the anchor is a world point and must not follow the player
            line.positionCount = 2;
            line.widthMultiplier = 0.04f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
        }

        line.sharedMaterial = TetherMaterial(materialPath, colour);
        line.startColor = colour;
        line.endColor = colour;
        return line;
    }

    private static Material TetherMaterial(string path, Color colour)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", colour);
        System.IO.Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>The index of the "Player" layer, adding it to the first free user slot if it is missing.</summary>
    private static int EnsurePlayerLayer()
    {
        int existing = LayerMask.NameToLayer(PlayerLayerName);
        if (existing >= 0)
        {
            return existing;
        }

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets.Length == 0)
        {
            return -1;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++) // 0-7 are Unity's own and cannot be renamed
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = PlayerLayerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        return -1;
    }

    /// <summary>An empty child at a known local position, created only if it is not there already.</summary>
    private static Transform Child(Transform parent, string name, Vector3 localPosition)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child;
        }

        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = localPosition;
        Undo.RegisterCreatedObjectUndo(child.gameObject, "Add " + name);
        return child;
    }

    /// <summary>
    /// The camera lives outside the player: parented under a Rigidbody it would inherit the interpolated
    /// pose and jitter. This lifts an existing camera out to a root rig and puts PlayerLook on it. Never
    /// creates a camera - a scene with none at all is the user's to fix.
    /// </summary>
    private static PlayerLook BuildCameraRig(FirstPersonController controller, Transform eyeTarget)
    {
        PlayerLook existing = Object.FindFirstObjectByType<PlayerLook>();
        if (existing != null)
        {
            return existing;
        }

        Camera camera = controller.GetComponentInChildren<Camera>(true);
        if (camera == null)
        {
            camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        }

        if (camera == null)
        {
            Debug.LogWarning("No camera in the open scene - add one, name it " + CameraRigName
                + ", and put PlayerLook on it by hand.", controller);
            return null;
        }

        if (camera.transform.parent != null)
        {
            Undo.SetTransformParent(camera.transform, null, "Detach Camera From Player");
        }

        camera.name = CameraRigName;
        camera.transform.position = eyeTarget.position;
        return Undo.AddComponent<PlayerLook>(camera.gameObject);
    }

    /// <summary>
    /// Gives an object in the open scene its PetFollower and points it at the player's camera: the
    /// selected object if there is one (select joe_0 to make joe the pet), otherwise a root named
    /// "Pet", created if missing. An empty Frames array is filled from the sprite sheet the object's
    /// SpriteRenderer already shows. Safe to re-run; set fields are left alone.
    /// </summary>
    [MenuItem("Tools/Wire Pet In Open Scene")]
    public static void WirePet()
    {
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller == null)
        {
            Debug.LogError("No FirstPersonController in the open scene - open the level scene first.");
            return;
        }

        GameObject host = Selection.activeGameObject;
        if (host != null && !host.scene.IsValid())
        {
            Debug.LogError("The selection is a project asset, not a scene object. Select the scene "
                + "object to turn into the pet, or nothing at all.", host);
            return;
        }

        if (host == null)
        {
            host = Object.FindObjectsByType<PetFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault()?.gameObject;
        }

        if (host == null)
        {
            host = SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(root => root.name.Equals(PetObjectName, System.StringComparison.OrdinalIgnoreCase));
        }

        if (host == null)
        {
            host = new GameObject(PetObjectName);
            host.transform.position = controller.transform.position + new Vector3(0.8f, 1.4f, -0.6f);
            Undo.RegisterCreatedObjectUndo(host, "Add Pet");
        }

        PetFollower pet = host.GetComponent<PetFollower>();
        if (pet == null)
        {
            pet = Undo.AddComponent<PetFollower>(host); // brings the SpriteRenderer with it
        }

        SerializedObject serializedController = new SerializedObject(controller);
        Transform pivot = serializedController.FindProperty("eyeTarget").objectReferenceValue as Transform;
        FillIfEmpty(pet, "target", pivot != null ? pivot : controller.transform);
        FillIfEmpty(pet, "speechLabel", SpeechLabel());
        int frames = FillFramesFromSheet(pet);
        SeedLines(pet);
        MakeInteractable(pet.gameObject);

        EditorSceneManager.MarkSceneDirty(pet.gameObject.scene);
        Selection.activeGameObject = pet.gameObject;
        Debug.Log("Wired " + pet.name + " in " + pet.gameObject.scene.name + " with " + frames
            + " frame(s) - save the scene (Ctrl+S).", pet);
    }

    /// <summary>Placeholder chatter so the pet has something to say before the lines are written.</summary>
    private static void SeedLines(PetFollower pet)
    {
        string[] seed =
        {
            "There you are. I was starting to worry.",
            "Don't look behind you. Not yet.",
            "This place remembers everyone who passes through.",
            "I'll keep watch. Mostly.",
        };

        SerializedObject serialized = new SerializedObject(pet);
        SerializedProperty lines = serialized.FindProperty("lines");
        if (lines.arraySize > 0)
        {
            return;
        }

        lines.arraySize = seed.Length;
        for (int i = 0; i < seed.Length; i++)
        {
            lines.GetArrayElementAtIndex(i).stringValue = seed[i];
        }

        serialized.ApplyModifiedProperties();
    }

    /// <summary>
    /// The Interactor raycasts for a collider carrying the interactable tag, so the pet needs both to
    /// be talked to. The box is sized to the sprite; an existing collider is left alone.
    /// </summary>
    private static void MakeInteractable(GameObject host)
    {
        if (!InternalEditorUtility.tags.Contains(InteractableTag))
        {
            InternalEditorUtility.AddTag(InteractableTag);
        }

        host.tag = InteractableTag;

        if (host.GetComponent<Collider>() == null)
        {
            BoxCollider box = Undo.AddComponent<BoxCollider>(host);
            SpriteRenderer renderer = host.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Vector3 size = renderer.sprite.bounds.size;
                box.size = new Vector3(size.x, size.y, 0.1f);
            }
        }
    }

    /// <summary>
    /// The HUD label the pet speaks through: an existing "Pet Speech" object, or a new one under the
    /// scene's canvas, matching the interact prompt's look. Returns null if the scene has no canvas.
    /// </summary>
    private static Text SpeechLabel()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("No Canvas in the open scene - assign the pet's Speech Label by hand.");
            return null;
        }

        Transform existing = canvas.transform.Find(PetSpeechObjectName);
        if (existing != null)
        {
            return existing.GetComponent<Text>();
        }

        Text label = NewRect(PetSpeechObjectName, canvas.transform).AddComponent<Text>();
        Anchor(label.rectTransform, new Vector2(0f, -160f), new Vector2(900f, 60f));
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.gameObject.SetActive(false);
        Undo.RegisterCreatedObjectUndo(label.gameObject, "Add Pet Speech");
        return label;
    }

    /// <summary>
    /// Fills an empty Frames array from the sheet the object's SpriteRenderer already shows - joe_0
    /// pulls in joe_0..joe_4 - so the slices need not be dragged in one by one. Returns how many
    /// frames the component ends up cycling through.
    /// </summary>
    private static int FillFramesFromSheet(PetFollower pet)
    {
        SerializedObject serialized = new SerializedObject(pet);
        SerializedProperty frames = serialized.FindProperty("frames");
        if (frames.arraySize > 0)
        {
            return serialized.FindProperty("frameCount").intValue;
        }

        SpriteRenderer renderer = pet.GetComponent<SpriteRenderer>();
        Sprite[] sheet = renderer != null && renderer.sprite != null
            ? AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer.sprite))
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name, Comparer<string>.Create(EditorUtility.NaturalCompare))
                .ToArray()
            : new Sprite[0];

        if (sheet.Length == 0)
        {
            Debug.LogWarning("No sprite sheet on " + pet.name + " to take frames from - assign its "
                + "Frames by hand.", pet);
            return 0;
        }

        frames.arraySize = sheet.Length;
        for (int i = 0; i < sheet.Length; i++)
        {
            frames.GetArrayElementAtIndex(i).objectReferenceValue = sheet[i];
        }

        serialized.FindProperty("frameCount").intValue = sheet.Length;
        serialized.ApplyModifiedProperties();
        return sheet.Length;
    }

    /// <summary>
    /// Built into an additively loaded scene and closed again, so whatever the editor already has
    /// open is neither saved nor unloaded on the way past (same approach as MainMenuBuilder).
    /// </summary>
    private static void BuildScene()
    {
        Scene previous = SceneManager.GetActiveScene();
        bool additive = !string.IsNullOrEmpty(previous.path);

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);

        if (additive)
        {
            SceneManager.SetActiveScene(scene);
        }

        BuildLight();
        Cube("Ground", null, new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
        GameObject player = BuildPlayer(out Transform orientation, out Transform eyeTarget, out Transform body);
        Transform rig = BuildCameraRigObject(eyeTarget);
        BuildDoorway();
        GameObject prompt = BuildHud();

        FirstPersonController controller = player.AddComponent<FirstPersonController>();
        SetReference(controller, "orientation", orientation);
        SetReference(controller, "eyeTarget", eyeTarget);
        SetReference(controller, "bodyVisual", body);
        SetReference(controller, "move", ActionReference("Player/Move"));
        SetReference(controller, "jump", ActionReference("Player/Jump"));
        SetReference(controller, "crouch", ActionReference("Player/Crouch"));

        PlayerLook look = rig.gameObject.AddComponent<PlayerLook>();
        SetReference(look, "orientation", orientation);
        SetReference(look, "eyeTarget", eyeTarget);
        SetReference(look, "look", ActionReference("Player/Look"));

        ApplyPlayerLayer(controller);
        BuildGrapple(controller, rig);

        Interactor interactor = player.AddComponent<Interactor>();
        SetReference(interactor, "eye", rig);
        SetReference(interactor, "prompt", prompt);
        SetReference(interactor, "interact", ActionReference("Player/Interact"));

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        if (additive)
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }

        AddToBuildSettings();
        AssetDatabase.SaveAssets();

        Debug.Log("Built " + ScenePath + " and added it to the build scene list.");
    }

    private static void BuildLight()
    {
        Light light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.GetUniversalAdditionalLightData();
        RenderSettings.sun = light;
    }

    /// <summary>
    /// The player body: a Rigidbody and CapsuleCollider, a capsule mesh scaled to match, and the two
    /// empties the rest of the rig hangs off - Orientation (the heading movement follows) and EyeTarget
    /// (where the camera rig sits). The camera itself is built outside the player by
    /// <see cref="BuildCameraRigObject"/>.
    /// </summary>
    private static GameObject BuildPlayer(out Transform orientation, out Transform eyeTarget, out Transform body)
    {
        GameObject player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.1f, -4f);

        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.4f;
        capsule.center = Vector3.up; // feet on the pivot: crouch and headroom both measure from there
        capsule.sharedMaterial = Frictionless();

        ConfigureBody(player);

        body = BuildBody(player.transform, capsule);
        orientation = Child(player.transform, OrientationName, Vector3.zero);
        eyeTarget = Child(player.transform, EyeTargetName, new Vector3(0f, capsule.height - EyeDropFromTop, 0f));
        return player;
    }

    /// <summary>
    /// The camera, deliberately a root object rather than a child of the player: PlayerLook follows the
    /// player's EyeTarget in LateUpdate instead, which is what keeps the view off the Rigidbody's
    /// interpolated transform and free of jitter.
    /// </summary>
    private static Transform BuildCameraRigObject(Transform eyeTarget)
    {
        GameObject rig = new GameObject(CameraRigName, typeof(Camera), typeof(AudioListener));
        rig.tag = "MainCamera";
        rig.transform.position = eyeTarget.position;

        Camera camera = rig.GetComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.fieldOfView = 70f;
        camera.GetUniversalAdditionalCameraData();
        return rig.transform;
    }

    /// <summary>A capsule mesh scaled to the collider: invisible from inside, but it casts the player's shadow.</summary>
    private static Transform BuildBody(Transform player, CapsuleCollider capsule)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player, false);
        Object.DestroyImmediate(body.GetComponent<Collider>()); // the CapsuleCollider above is the collider
        FirstPersonController.FitCapsule(body.transform, capsule);
        return body.transform;
    }

    /// <summary>Sets a serialized reference only when it is currently empty (undoable, marks the object dirty).</summary>
    internal static void FillIfEmpty(Object target, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property.objectReferenceValue == null && value != null)
        {
            property.objectReferenceValue = value;
            serialized.ApplyModifiedProperties();
        }
    }

    private static void BuildDoorway()
    {
        GameObject doorway = new GameObject("Doorway");
        doorway.transform.position = new Vector3(0f, 0f, 2f);

        Cube("Post L", doorway.transform, new Vector3(-0.575f, 1.1f, 0f), new Vector3(0.15f, 2.2f, 0.15f));
        Cube("Post R", doorway.transform, new Vector3(0.575f, 1.1f, 0f), new Vector3(0.15f, 2.2f, 0.15f));
        Cube("Lintel", doorway.transform, new Vector3(0f, 2.175f, 0f), new Vector3(1.3f, 0.15f, 0.15f));

        GameObject door = new GameObject("Door");
        door.tag = InteractableTag;
        door.transform.SetParent(doorway.transform, false);
        door.transform.localPosition = new Vector3(0.5f, 0f, 0f);

        BoxCollider collider = door.AddComponent<BoxCollider>();
        collider.center = new Vector3(-0.49f, 1.04f, 0f);
        collider.size = new Vector3(0.98f, 2.08f, 0.1f);
        door.AddComponent<Door>();

        GameObject panel = Cube(
            "Panel", door.transform, new Vector3(-0.49f, 1.04f, 0f), new Vector3(0.98f, 2.08f, 0.08f));
        Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.GetComponent<MeshRenderer>().sharedMaterial = DoorMaterial();
    }

    /// <summary>
    /// Crosshair dot plus the interact prompt. The prompt starts inactive; Interactor switches it on
    /// while an interactable is in view.
    /// </summary>
    internal static GameObject BuildHud()
    {
        GameObject hud = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        hud.layer = LayerMask.NameToLayer("UI");
        hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image crosshair = Icon("Crosshair", hud.transform, Vector2.zero, new Vector2(6f, 6f),
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"));
        crosshair.color = new Color(1f, 1f, 1f, 0.8f);

        GameObject prompt = NewRect("Interact Prompt", hud.transform);
        Anchor(prompt.GetComponent<RectTransform>(), new Vector2(0f, -70f), new Vector2(220f, 56f));

        Text label = NewRect("Label", prompt.transform).AddComponent<Text>();
        Anchor(label.rectTransform, Vector2.zero, new Vector2(220f, 48f));
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 26;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = InteractPromptText;

        prompt.SetActive(false);
        return prompt;
    }

    private static GameObject Cube(string name, Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = localScale;
        return cube;
    }

    private static GameObject NewRect(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.layer = LayerMask.NameToLayer("UI");
        result.transform.SetParent(parent, false);
        return result;
    }

    private static Image Icon(string name, Transform parent, Vector2 position, Vector2 size, Sprite sprite)
    {
        Image image = NewRect(name, parent).AddComponent<Image>();
        Anchor(image.rectTransform, position, size);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static void Anchor(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>The importer stores one InputActionReference sub-asset per action, named "Map/Action".</summary>
    internal static InputActionReference ActionReference(string mapSlashAction)
    {
        InputActionReference reference = AssetDatabase.LoadAllAssetsAtPath(ActionsPath)
            .OfType<InputActionReference>()
            .FirstOrDefault(candidate => candidate.name == mapSlashAction);

        if (reference == null)
        {
            Debug.LogError("Action '" + mapSlashAction + "' not found in " + ActionsPath + "; assign it by hand.");
        }

        return reference;
    }

    private static Material DoorMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(DoorMaterialPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.45f, 0.28f, 0.15f, 1f));
        material.SetFloat("_Smoothness", 0.3f);
        System.IO.Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(material, DoorMaterialPath);
        return material;
    }

    private static void SetReference(Object target, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(entry => entry.path == ScenePath);
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
