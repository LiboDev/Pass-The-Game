using System.IO;
using PassTheGame.WeaponsEnemies;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One-click setup for the Weapons And Enemies package: creates its data assets and prefabs if they
/// don't exist yet, gives the player in the open scene a working weapon, drops an enemy spawner with
/// spawn points around them, gives the player something for enemies to damage, and bakes a NavMesh.
/// Safe to run again - existing assets and scene objects are reused and re-linked, not duplicated.
/// </summary>
public static class WeaponsEnemiesBuilder
{
    private const string DataFolder = "Assets/Data";
    private const string WeaponPrefabFolder = "Assets/Prefabs/Weapons";
    private const string EnemyPrefabFolder = "Assets/Prefabs/Enemies";
    private const string WeaponDataPath = DataFolder + "/PistolData.asset";
    private const string EnemyDataPath = DataFolder + "/GruntData.asset";
    private const string WeaponPrefabPath = WeaponPrefabFolder + "/Pistol.prefab";
    private const string EnemyPrefabPath = EnemyPrefabFolder + "/Grunt.prefab";
    private const string EnemyMaterialPath = "Assets/Materials/Enemy.mat";
    private const string EnemyLayerName = "Enemy";
    private const string WeaponHolderName = "WeaponHolder";
    private const string SpawnerName = "Enemy Spawner";
    private const int SpawnPointCount = 4;
    private const float SpawnRadius = 8f;

    [MenuItem("Tools/Add Weapons And Enemies")]
    public static void Setup()
    {
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller == null)
        {
            Debug.LogError("No FirstPersonController in the open scene - open the scene with the player first.");
            return;
        }

        int enemyLayer = EnsureLayer(EnemyLayerName);
        WeaponData weaponData = EnsureWeaponData(controller.gameObject.layer);
        EnemyData enemyData = EnsureEnemyData();
        Weapon weaponPrefab = EnsureWeaponPrefab(weaponData);
        Enemy enemyPrefab = EnsureEnemyPrefab(enemyData, enemyLayer);
        AssetDatabase.SaveAssets();

        EquipPlayer(controller, weaponPrefab);
        EnsurePlayerHealth(controller.gameObject);
        BuildSpawner(controller.transform, enemyPrefab);
        BakeNavMesh();

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log("Weapons And Enemies set up in " + controller.gameObject.scene.name
            + " - hold Fire (left mouse) to shoot, enemies spawn and chase around the player. "
            + "Save the scene (Ctrl+S).", controller);
    }

    /// <summary>Finds a free layer slot 8+ and names it, or reuses one already named this.</summary>
    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
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
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        Debug.LogWarning("No free layer slot for " + name + " - put its prefab on its own layer by hand.");
        return -1;
    }

    private static WeaponData EnsureWeaponData(int playerLayer)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponDataPath);
        if (data != null)
        {
            return data;
        }

        Directory.CreateDirectory(DataFolder);
        data = ScriptableObject.CreateInstance<WeaponData>();
        data.damage = 15f;
        data.shotsPerSecond = 3f;
        data.range = 60f;
        data.hitMask = ~(1 << playerLayer); // never hit the shooter
        AssetDatabase.CreateAsset(data, WeaponDataPath);
        return data;
    }

    private static EnemyData EnsureEnemyData()
    {
        EnemyData data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyDataPath);
        if (data != null)
        {
            return data;
        }

        Directory.CreateDirectory(DataFolder);
        data = ScriptableObject.CreateInstance<EnemyData>();
        data.maxHealth = 60f;
        data.moveSpeed = 3f;
        data.attackDamage = 8f;
        data.attackRange = 1.8f;
        data.attackCooldown = 1.2f;
        data.despawnDelay = 3f;
        AssetDatabase.CreateAsset(data, EnemyDataPath);
        return data;
    }

    /// <summary>A small held cube with a muzzle tip; enough to fire from and to see in first person.</summary>
    private static Weapon EnsureWeaponPrefab(WeaponData data)
    {
        Weapon existingPrefab = AssetDatabase.LoadAssetAtPath<Weapon>(WeaponPrefabPath);
        if (existingPrefab != null)
        {
            return existingPrefab;
        }

        GameObject temp = new GameObject("Pistol");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(temp.transform, false);
        body.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        body.transform.localScale = new Vector3(0.08f, 0.08f, 0.3f);
        Object.DestroyImmediate(body.GetComponent<Collider>()); // a held prop, not solid geometry

        Transform muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(temp.transform, false);
        muzzle.localPosition = new Vector3(0f, 0f, 0.3f);

        Weapon weapon = temp.AddComponent<Weapon>();
        SerializedObject serialized = new SerializedObject(weapon);
        serialized.FindProperty("data").objectReferenceValue = data;
        serialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(WeaponPrefabFolder);
        GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, WeaponPrefabPath);
        Object.DestroyImmediate(temp);
        return asset.GetComponent<Weapon>();
    }

    /// <summary>A capsule with a NavMeshAgent; enough to path around and be shot at.</summary>
    private static Enemy EnsureEnemyPrefab(EnemyData data, int enemyLayer)
    {
        Enemy existingPrefab = AssetDatabase.LoadAssetAtPath<Enemy>(EnemyPrefabPath);
        if (existingPrefab != null)
        {
            return existingPrefab;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        temp.name = "Grunt";
        if (enemyLayer >= 0)
        {
            temp.layer = enemyLayer;
        }

        temp.GetComponent<Renderer>().sharedMaterial = EnemyMaterial();
        temp.AddComponent<NavMeshAgent>();

        Enemy enemy = temp.AddComponent<Enemy>();
        SerializedObject serialized = new SerializedObject(enemy);
        serialized.FindProperty("data").objectReferenceValue = data;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(EnemyPrefabFolder);
        GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, EnemyPrefabPath);
        Object.DestroyImmediate(temp);
        return asset.GetComponent<Enemy>();
    }

    private static Material EnemyMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(EnemyMaterialPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.6f, 0.12f, 0.12f, 1f));
        AssetDatabase.CreateAsset(material, EnemyMaterialPath);
        return material;
    }

    /// <summary>Puts the weapon prefab on the camera rig (or the player if there is no rig yet) and
    /// makes it fire on the existing Attack action.</summary>
    private static void EquipPlayer(FirstPersonController controller, Weapon weaponPrefab)
    {
        PlayerLook look = Object.FindFirstObjectByType<PlayerLook>();
        Transform holderParent = look != null ? look.transform : controller.transform;

        Transform holder = holderParent.Find(WeaponHolderName);
        Weapon weapon;
        if (holder == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab.gameObject);
            instance.name = WeaponHolderName;
            instance.transform.SetParent(holderParent, false);
            instance.transform.localPosition = new Vector3(0.35f, -0.3f, 0.6f);
            instance.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(instance, "Equip Player Weapon");
            weapon = instance.GetComponent<Weapon>();
        }
        else
        {
            weapon = holder.GetComponent<Weapon>();
        }

        if (weapon != null && !weapon.TryGetComponent(out WeaponInput _))
        {
            WeaponInput input = Undo.AddComponent<WeaponInput>(weapon.gameObject);
            SerializedObject serialized = new SerializedObject(input);
            serialized.FindProperty("fire").objectReferenceValue = GameSceneBuilder.ActionReference("Player/Attack");
            serialized.ApplyModifiedProperties();
        }
    }

    private static void EnsurePlayerHealth(GameObject player)
    {
        if (player.GetComponent<IDamageable>() != null)
        {
            return;
        }

        Undo.AddComponent<PlayerHealth>(player);
    }

    /// <summary>An EnemySpawner ringed by spawn points around the player, reusing one already there.</summary>
    private static void BuildSpawner(Transform player, Enemy enemyPrefab)
    {
        EnemySpawner spawner = Object.FindFirstObjectByType<EnemySpawner>();
        GameObject spawnerObject;
        if (spawner == null)
        {
            spawnerObject = new GameObject(SpawnerName);
            spawnerObject.transform.position = player.position;
            spawner = spawnerObject.AddComponent<EnemySpawner>();
            Undo.RegisterCreatedObjectUndo(spawnerObject, "Add Enemy Spawner");
        }
        else
        {
            spawnerObject = spawner.gameObject;
        }

        Transform[] points = new Transform[SpawnPointCount];
        for (int i = 0; i < SpawnPointCount; i++)
        {
            string name = "Spawn Point " + i;
            Transform point = spawnerObject.transform.Find(name);
            if (point == null)
            {
                float angle = i * Mathf.PI * 2f / SpawnPointCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnRadius;
                GameObject pointObject = new GameObject(name);
                pointObject.transform.SetParent(spawnerObject.transform, false);
                pointObject.transform.position = player.position + offset;
                Undo.RegisterCreatedObjectUndo(pointObject, "Add Enemy Spawn Point");
                point = pointObject.transform;
            }

            points[i] = point;
        }

        SerializedObject serialized = new SerializedObject(spawner);
        serialized.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        serialized.FindProperty("target").objectReferenceValue = player;
        SerializedProperty spawnPoints = serialized.FindProperty("spawnPoints");
        spawnPoints.arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            spawnPoints.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        }

        serialized.ApplyModifiedProperties();
    }

    /// <summary>Bakes over whatever render geometry is in the scene so spawned enemies can path immediately.</summary>
    private static void BakeNavMesh()
    {
        NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject surfaceObject = new GameObject("NavMesh Surface");
            surface = surfaceObject.AddComponent<NavMeshSurface>();
            Undo.RegisterCreatedObjectUndo(surfaceObject, "Add NavMesh Surface");
        }

        surface.BuildNavMesh();
    }
}
