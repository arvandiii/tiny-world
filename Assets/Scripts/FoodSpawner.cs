using UnityEngine;

/// <summary>
/// Spawns Food on the grid. Kept separate from GridManager on purpose:
/// GridManager owns terrain data, this owns food placement.
///
/// Automatic mode: a batch of foodPerBatch items on distinct cells every intervalSeconds.
/// The public Spawn* methods are what a future admin panel can call (click on the map to place food).
/// </summary>
public class FoodSpawner : MonoBehaviour
{
    [Header("Prefab (optional)")]
    [Tooltip("Leave empty to spawn a small default sphere created in code.")]
    [SerializeField] private GameObject foodPrefab;
    [Tooltip("Optional parent in the Hierarchy to keep things tidy.")]
    [SerializeField] private Transform foodParent;

    [Header("Automatic spawning")]
    [SerializeField] private bool autoSpawn = true;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private int foodPerBatch = 5;
    [SerializeField] private float intervalSeconds = 60f;
    [Tooltip("Stops auto-spawning when this many Food objects exist. 0 = unlimited.")]
    [SerializeField] private int maxFoodInWorld = 50;

    [Header("Placement")]
    [Tooltip("Height above the ground (Y) where food is placed.")]
    [SerializeField] private float spawnHeight = 0.25f;
    [SerializeField] private int maxPlacementAttempts = 50;

    private GridManager grid;
    private float timer;

    // ------------------------------------------------------------------ lifecycle

    private void Start()
    {
        grid = GridManager.Instance;
        if (grid == null)
        {
            Debug.LogError("[FoodSpawner] No GridManager in the scene. Disabling.", this);
            enabled = false;
            return;
        }

        if (autoSpawn && spawnOnStart) SpawnRandom(foodPerBatch);
    }

    private void OnValidate()
    {
        foodPerBatch = Mathf.Max(0, foodPerBatch);
        intervalSeconds = Mathf.Max(0.1f, intervalSeconds);
        maxFoodInWorld = Mathf.Max(0, maxFoodInWorld);
        maxPlacementAttempts = Mathf.Max(1, maxPlacementAttempts);
    }

    private void Update()
    {
        if (!autoSpawn) return;

        timer += Time.deltaTime;
        if (timer >= intervalSeconds)
        {
            timer = 0f;
            SpawnRandom(foodPerBatch);
        }
    }

    [ContextMenu("Spawn Batch Now (Play Mode)")]
    private void SpawnBatchNow()
    {
        if (!Application.isPlaying) return;
        SpawnRandom(foodPerBatch);
    }

    // ------------------------------------------------------------------ public API (admin panel later)

    /// <summary>Spawns up to count food items on distinct random valid cells. Returns how many were spawned.</summary>
    public int SpawnRandom(int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (maxFoodInWorld > 0 && Food.All.Count >= maxFoodInWorld) break;

            if (TryFindRandomSpawnCell(out Vector2Int cell) && SpawnAtCell(cell) != null)
                spawned++;
        }
        return spawned;
    }

    public Food SpawnAtWorld(Vector3 worldPos, GameObject prefabOverride = null)
    {
        EnsureGrid();
        if (grid == null) return null;
        return SpawnAtCell(grid.WorldToCell(worldPos), prefabOverride);
    }

    /// <summary>Returns the spawned Food, or null if the cell is invalid or already has food.</summary>
    public Food SpawnAtCell(Vector2Int cell, GameObject prefabOverride = null)
    {
        EnsureGrid();
        if (grid == null || !IsValidSpawnCell(cell)) return null;

        Vector3 pos = grid.CellCenterToWorld(cell.x, cell.y);
        pos.y = spawnHeight;

        GameObject source = prefabOverride != null ? prefabOverride : foodPrefab;
        GameObject go;
        if (source != null)
        {
            go = Instantiate(source, pos, Quaternion.identity, foodParent);
        }
        else
        {
            go = CreateDefaultFoodObject();
            go.transform.SetParent(foodParent);
            go.transform.position = pos;
        }

        if (!go.TryGetComponent(out Food food)) food = go.AddComponent<Food>();
        return food;
    }

    // ------------------------------------------------------------------ helpers

    private void EnsureGrid()
    {
        if (grid == null) grid = GridManager.Instance;
    }

    private bool TryFindRandomSpawnCell(out Vector2Int cell)
    {
        for (int i = 0; i < maxPlacementAttempts; i++)
        {
            cell = new Vector2Int(Random.Range(0, grid.Width), Random.Range(0, grid.Height));
            if (IsValidSpawnCell(cell)) return true;
        }
        cell = default;
        return false;
    }

    /// <summary>In bounds, not water, and no other food already on that cell.</summary>
    private bool IsValidSpawnCell(Vector2Int cell)
    {
        TerrainType t = grid.GetTerrainAt(cell);
        if (t == TerrainType.OutOfBounds || t == TerrainType.Water) return false; // TODO: fish in water later

        foreach (Food f in Food.All)
        {
            if (f == null || f.IsConsumed) continue;
            if (grid.WorldToCell(f.transform.position) == cell) return false;
        }
        return true;
    }

    private static GameObject CreateDefaultFoodObject()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Food";
        go.transform.localScale = Vector3.one * 0.4f;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(0.9f, 0.2f, 0.2f);
        return go;
    }
}