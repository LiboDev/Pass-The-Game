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
    /// Fills whatever the FirstPersonController and Interactor in the open scene are missing (fields
    /// added after the scene was built), adopts a camera as the pitch pivot, and adds the Body capsule
    /// if there is none. Fields that are already set are left alone, so this is safe to run again
    /// whenever a new field appears.
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

        CharacterController capsule = controller.GetComponent<CharacterController>();

        // Crouch and headroom both measure from the feet, so the capsule stands on the pivot.
        Vector3 footCentre = new Vector3(0f, capsule.height * 0.5f, 0f);
        if (capsule.center != footCentre)
        {
            Undo.RecordObject(capsule, "Centre Player Capsule");
            capsule.center = footCentre;
        }

        Transform body = controller.transform.Find("Body");
        if (body == null)
        {
            body = BuildBody(controller.transform, capsule);
            Undo.RegisterCreatedObjectUndo(body.gameObject, "Add Player Body");
        }

        Undo.RecordObject(body, "Fit Player Body");
        FirstPersonController.FitCapsule(body, capsule);

        FillIfEmpty(controller, "cameraPivot", CameraPivot(controller, capsule));
        FillIfEmpty(controller, "bodyVisual", body);
        FillIfEmpty(controller, "move", ActionReference("Player/Move"));
        FillIfEmpty(controller, "look", ActionReference("Player/Look"));
        FillIfEmpty(controller, "jump", ActionReference("Player/Jump"));
        FillIfEmpty(controller, "crouch", ActionReference("Player/Crouch"));

        if (controller.TryGetComponent(out Interactor interactor))
        {
            FillIfEmpty(interactor, "interact", ActionReference("Player/Interact"));
        }

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log("Wired " + controller.name + " in " + controller.gameObject.scene.name + " - save the scene (Ctrl+S).", controller);
    }

    /// <summary>
    /// The transform the controller pitches: a camera already parented under the player, otherwise the
    /// scene's camera adopted under it at eye height. Never creates one - a scene with no camera at all
    /// is the user's to fix.
    /// </summary>
    private static Transform CameraPivot(FirstPersonController controller, CharacterController capsule)
    {
        Camera camera = controller.GetComponentInChildren<Camera>(true);
        if (camera == null)
        {
            camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        }

        if (camera == null)
        {
            Debug.LogWarning("No camera in the open scene - parent one under " + controller.name
                + " and assign it as Camera Pivot by hand.", controller);
            return null;
        }

        if (camera.transform.parent != controller.transform)
        {
            Undo.SetTransformParent(camera.transform, controller.transform, "Parent Camera To Player");
            camera.transform.localPosition = new Vector3(0f, capsule.height - EyeDropFromTop, 0f);
            camera.transform.localRotation = Quaternion.identity;
        }

        return camera.transform;
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
        Transform pivot = serializedController.FindProperty("cameraPivot").objectReferenceValue as Transform;
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
        GameObject player = BuildPlayer(out Transform cameraPivot, out Transform body);
        BuildDoorway();
        GameObject prompt = BuildHud();

        FirstPersonController controller = player.AddComponent<FirstPersonController>();
        SetReference(controller, "cameraPivot", cameraPivot);
        SetReference(controller, "bodyVisual", body);
        SetReference(controller, "move", ActionReference("Player/Move"));
        SetReference(controller, "look", ActionReference("Player/Look"));
        SetReference(controller, "jump", ActionReference("Player/Jump"));
        SetReference(controller, "crouch", ActionReference("Player/Crouch"));

        Interactor interactor = player.AddComponent<Interactor>();
        SetReference(interactor, "eye", cameraPivot);
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
    /// A CharacterController with a capsule mesh scaled to match it (invisible from inside, but it casts
    /// the player's shadow) and the camera as a child pivot at eye height.
    /// </summary>
    private static GameObject BuildPlayer(out Transform cameraPivot, out Transform body)
    {
        GameObject player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0.1f, -4f);

        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.center = Vector3.up;

        body = BuildBody(player.transform, controller);

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        Camera camera = cameraObject.GetComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.fieldOfView = 70f;
        camera.GetUniversalAdditionalCameraData();

        cameraPivot = cameraObject.transform;
        return player;
    }

    /// <summary>A capsule mesh scaled to the controller: invisible from inside, but it casts the player's shadow.</summary>
    private static Transform BuildBody(Transform player, CharacterController controller)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player, false);
        Object.DestroyImmediate(body.GetComponent<Collider>()); // the CharacterController is the collider
        FirstPersonController.FitCapsule(body.transform, controller);
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

    /// <summary>
    /// Two posts and a lintel, plus the door hinged on the right post so it swings away from the
    /// player. The hinge object owns the tag, the collider and the Door script; the panel is only a
    /// mesh, so the raycast lands on the object that has Interact().
    /// </summary>
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
        label.text = "[E] Interact";

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
