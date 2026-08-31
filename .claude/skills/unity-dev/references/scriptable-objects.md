# ScriptableObjects

Use for: designer-tunable data (stats, weapons, loot/spawn tables), shared runtime references, event
channels, runtime sets. Not for: per-instance mutable state (that lives on the MonoBehaviour).

## Data asset

```csharp
[CreateAssetMenu(fileName = "EnemyStats", menuName = "Game/Enemy Stats")]
public class EnemyStats : ScriptableObject
{
    [Min(1f)] public float maxHealth = 100f;
    [Min(0f)] public float moveSpeed = 3.5f;
    [Min(0f)] public float damage = 10f;
    [Min(0f)] public float attackRange = 1.5f;
    public AudioClip[] growls;
}

// Enemy.cs
[SerializeField] private EnemyStats stats;
private float health;
private void Awake() => health = stats.maxHealth;   // copy; never write back into stats
```
- Public fields are fine on pure data SOs. Add `OnValidate` for clamping if needed.
- **Never mutate SO fields at runtime for per-instance state.** The asset is shared by every instance and,
  in the editor, the change persists after Play Mode ends.
- Prefab variant + different `EnemyStats` asset = new enemy type with zero code.

## Event channel (decoupled messaging)

```csharp
[CreateAssetMenu(menuName = "Game/Events/Void Event", fileName = "OnSomething")]
public class VoidEventChannelSO : ScriptableObject
{
    public event Action Raised;
    public void Raise() => Raised?.Invoke();
}

[CreateAssetMenu(menuName = "Game/Events/Float Event", fileName = "OnFloatChanged")]
public class FloatEventChannelSO : ScriptableObject
{
    public event Action<float> Raised;
    public void Raise(float value) => Raised?.Invoke(value);
}

// Raiser:   [SerializeField] private VoidEventChannelSO onDoorOpened;   ...   onDoorOpened.Raise();
// Listener: OnEnable  → onDoorOpened.Raised += HandleDoorOpened;
//           OnDisable → onDoorOpened.Raised -= HandleDoorOpened;
```
Use for things many systems react to without knowing each other: player died, objective done, sanity
changed, noise made. Scene objects and prefabs can both hold the same asset.
For designer-wired responses, pair it with a listener component:
`VoidEventListener : MonoBehaviour { [SerializeField] VoidEventChannelSO channel; [SerializeField] UnityEvent response; }`.

## Runtime set (replaces `FindObjectsOfType`)

```csharp
[CreateAssetMenu(menuName = "Game/Runtime Sets/Enemy Set", fileName = "EnemySet")]
public class EnemyRuntimeSetSO : ScriptableObject
{
    [NonSerialized] public readonly List<Enemy> Items = new();
    public void Add(Enemy e)    { if (!Items.Contains(e)) Items.Add(e); }
    public void Remove(Enemy e) => Items.Remove(e);
}
// Enemy: OnEnable → set.Add(this); OnDisable → set.Remove(this).
// Anyone holding the asset reads set.Items ("how many alive", "nearest enemy").
```
If Enter Play Mode settings disable domain reload, clear runtime state at play start:
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetState() { ... }`
or clear the list from a scene-placed manager's `Awake`.

## Weighted spawn table

```csharp
[CreateAssetMenu(menuName = "Game/Spawn Table", fileName = "SpawnTable")]
public class SpawnTableSO : ScriptableObject
{
    [Serializable] public struct Entry { public Enemy prefab; [Min(0f)] public float weight; }
    public Entry[] entries;

    public Enemy Pick()
    {
        float total = 0f;
        foreach (var e in entries) total += e.weight;
        float roll = Random.Range(0f, total);
        foreach (var e in entries) { roll -= e.weight; if (roll <= 0f) return e.prefab; }
        return entries[entries.Length - 1].prefab;
    }
}
```

## Creating the asset

The user creates it: Project window → right-click → Create → *menuName path*. Put the exact menu path, the
location (`Assets/Data/...`), and the fields to fill in the Wire in Unity checklist.
Many assets at once → a one-off `[MenuItem]` editor script using `AssetDatabase.CreateAsset`
(see `editor-and-assets.md`) that the user runs from the menu bar.

Writing a `.asset` YAML file by hand is possible once the script's `.meta` exists (refresh first;
`m_Script: {fileID: 11500000, guid: <script guid>, type: 3}`), but prefer the menu.
