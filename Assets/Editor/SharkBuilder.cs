using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SharkBuilder
{
    private const string ScenePath = "Assets/Scenes/dungeon.unity";
    private const string ModelPath = "Assets/Models/Shark/SharkFullBody.fbx";
    private const string PrefabPath = "Assets/Prefabs/Enemies/RelentlessShark.prefab";
    private const string InstanceName = "Relentless Shark";
    private const string SessionKey = "PassTheGame.RelentlessSharkInstalled";

    static SharkBuilder()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += InstallIfDungeonIsOpen;
    }

    [MenuItem("Tools/Add Relentless Shark")]
    public static void AddToOpenScene()
    {
        FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!player || !model)
        {
            Debug.LogError("Relentless Shark needs an open player scene and SharkFullBody.fbx at " + ModelPath);
            return;
        }

        GameObject prefab = BuildPrefab(model);
        SharkPursuer existing = Object.FindFirstObjectByType<SharkPursuer>();
        if (!existing)
        {
            GameObject shark = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            shark.name = InstanceName;
            shark.transform.position = player.transform.position + new Vector3(-25f, 6f, -25f);
            Undo.RegisterCreatedObjectUndo(shark, "Add Relentless Shark");
        }

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
    }

    public static void InstallInDungeon()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AddToOpenScene();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Relentless Shark installed in dungeon.");
    }

    private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath)
        {
            InstallIfDungeonIsOpen();
        }
    }

    private static void InstallIfDungeonIsOpen()
    {
        if (SessionState.GetBool(SessionKey, false)
            || EditorSceneManager.GetActiveScene().path != ScenePath
            || Object.FindFirstObjectByType<SharkPursuer>() != null
            || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
        {
            return;
        }

        AddToOpenScene();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        SessionState.SetBool(SessionKey, true);
        Debug.Log("Relentless Shark installed in the open dungeon scene.");
    }

    private static GameObject BuildPrefab(GameObject model)
    {
        GameObject root = new GameObject(InstanceName);
        root.AddComponent<Rigidbody>();
        SphereCollider collider = root.AddComponent<SphereCollider>();
        collider.radius = 1.5f;
        root.AddComponent<SharkPursuer>();

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Shark Model";
        visual.transform.SetParent(root.transform, false);

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float longestSide = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longestSide > 0.001f)
            {
                visual.transform.localScale = Vector3.one * (5f / longestSide);
            }
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }
}
