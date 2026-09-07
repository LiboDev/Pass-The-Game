using UnityEngine;

/// <summary>
/// Watches the player's height. Dropping below the threshold means they fell out of the current level:
/// the level counter goes up, the player is teleported to the next entry's spawn point and the pet is
/// handed that entry's lines. Entry 0 is where the first fall lands; the starting area is not listed.
/// Falling out of the last entry respawns at its spawn point without changing anything else.
/// </summary>
[DisallowMultipleComponent]
public class DungeonLevelManager : MonoBehaviour
{
    [System.Serializable]
    private class Level
    {
        [Tooltip("Where the player lands when this level begins (facing its forward)")]
        public Transform spawnPoint;
        [TextArea, Tooltip("What the pet says on this level; it is re-tagged Interactable when the level begins")]
        public string[] petLines;
    }

    [Header("References")]
    [SerializeField] private Rigidbody player;
    [SerializeField, Tooltip("Turned to face each spawn point; the player's own rotation is never read")]
    private PlayerLook look;
    [SerializeField, Tooltip("Optional; its tethers are dropped before the player is moved")]
    private Grapple grapple;
    [SerializeField, Tooltip("Optional; gets each level's lines")] private PetFollower pet;

    [Header("Levels")]
    [SerializeField, Tooltip("World Y the player must fall below to drop to the next level")]
    private float fallThresholdY = -10f;
    [SerializeField, Tooltip("In falling order: element 0 is reached by the first fall, element 1 by the second...")]
    private Level[] levels;

    /// <summary>How many times the player has fallen: 0 in the starting area, 1 on levels[0], and so on.</summary>
    public int CurrentLevel { get; private set; }

    private void Awake()
    {
        if (!player || levels == null || levels.Length == 0)
        {
            Debug.LogError($"[{nameof(DungeonLevelManager)}] needs player and at least one level", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < levels.Length; i++)
        {
            if (!levels[i].spawnPoint)
            {
                Debug.LogError($"[{nameof(DungeonLevelManager)}] level entry {i} has no spawn point", this);
                enabled = false;
            }
            else if (levels[i].spawnPoint.position.y <= fallThresholdY)
            {
                Debug.LogError($"[{nameof(DungeonLevelManager)}] level entry {i} spawn point sits below the fall threshold; the player would drop straight through again", this);
                enabled = false;
            }
        }
    }

    private void Update()
    {
        if (player.transform.position.y > fallThresholdY)
        {
            return;
        }

        if (CurrentLevel >= levels.Length)
        {
            Teleport(levels[levels.Length - 1].spawnPoint); // fell out of the last level: back to its start
            return;
        }

        CurrentLevel++;
        Level level = levels[CurrentLevel - 1];
        Teleport(level.spawnPoint);
        if (pet)
        {
            pet.SetLines(level.petLines); // also re-tags the pet Interactable
        }
    }

    private void Teleport(Transform spawnPoint)
    {
        // A tether still attached across the teleport would snap the player straight back to it.
        if (grapple)
        {
            grapple.ReleaseAll();
        }

        // Interpolation would draw the jump as a streak across the level, so it is off for the write.
        RigidbodyInterpolation interpolation = player.interpolation;
        player.interpolation = RigidbodyInterpolation.None;
        player.position = spawnPoint.position;
        player.interpolation = interpolation;

        // A long fall carries a lot of speed; without this it continues into the next level.
        player.linearVelocity = Vector3.zero;
        player.angularVelocity = Vector3.zero;

        // Facing lives on the camera rig now - writing this body's rotation would do nothing visible.
        if (look)
        {
            look.SetYaw(spawnPoint.eulerAngles.y);
        }
    }
}
