using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source of truth for terrain: a plain 2D array. No pathfinding.
/// In Play mode it also spawns one tile object per cell (from the per-terrain prefabs you assign)
/// as a display layer. The tiles are visuals only; the array stays the source of truth.
///
/// Layout: XZ plane, Y up. Cell (0,0) has its corner at world (0,0,0) and the grid
/// extends toward +X and +Z. The grid is NOT centered on the origin.
///
/// Map build order (later steps overwrite earlier ones):
///   1. Fill everything Plain
///   2. Hardcoded test patches (toggle: useDefaultTestPatches)
///   3. Seeded random scatter (toggle: randomScatter.enabled)
///   4. Custom patches list (Inspector)
/// </summary>
[DefaultExecutionOrder(-100)]
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Serializable]
    public struct TerrainProfile
    {
        [Tooltip("Energy drained per second while moving on this terrain.")]
        public float moveEnergyPerSecond;
        [Tooltip("Multiplier on moveSpeed while standing on this terrain.")]
        [Range(0f, 2f)] public float speedMultiplier;
        [Tooltip("Multiplier on sleep regeneration. Ignored for Water (nobody sleeps in water).")]
        [Range(0f, 1f)] public float sleepQuality;
    }

    /// <summary>Inclusive rectangle of cells set to one terrain type. from/to can be in any order.</summary>
    [Serializable]
    public struct TerrainPatch
    {
        public TerrainType type;
        public Vector2Int from;
        public Vector2Int to;
    }

    [Serializable]
    public class RandomScatter
    {
        public bool enabled = false;
        public int seed = 12345;
        public int muddyPatches = 6;
        public int stonyPatches = 6;
        public int waterPatches = 3;
        public Vector2Int minPatchSize = new Vector2Int(2, 2);
        public Vector2Int maxPatchSize = new Vector2Int(6, 6);
    }

    [Header("Grid")]
    [SerializeField] private int width = 32;
    [SerializeField] private int height = 32;
    [SerializeField] private float cellSize = 1f;

    [Header("Map editing (Inspector)")]
    [Tooltip("Hardcoded reproducible patches near the origin. Untick when using your own map.")]
    [SerializeField] private bool useDefaultTestPatches = true;
    [SerializeField] private RandomScatter randomScatter = new RandomScatter();
    [Tooltip("Applied last, so these override everything else. Inclusive cell coordinates.")]
    [SerializeField] private TerrainPatch[] customPatches = new TerrainPatch[0];

    [Header("Tile prefabs (one per terrain type)")]
    [Tooltip("Expected footprint: 1x1 unit, pivot at the center of the tile's top surface.")]
    [SerializeField] private GameObject plainTilePrefab;
    [SerializeField] private GameObject muddyTilePrefab;
    [SerializeField] private GameObject stonyTilePrefab;
    [SerializeField] private GameObject waterTilePrefab;

    [Header("Tile spawning")]
    [Tooltip("If a prefab slot is empty, spawn a flat colored cube instead so the map is still visible.")]
    [SerializeField] private bool useFallbackTiles = true;
    [Tooltip("Multiply each tile's X/Z scale by cellSize (assumes prefabs are 1x1 units).")]
    [SerializeField] private bool scaleTilesToCellSize = true;
    [Tooltip("Lifts tiles slightly to avoid flickering with a ground plane at Y = 0.")]
    [SerializeField] private float tileYOffset = 0.01f;
    [Tooltip("Off = tile colliders are removed and the ground plane handles physics. On = keep prefab colliders.")]
    [SerializeField] private bool keepTileColliders = false;

    [Header("Ground collider")]
    [Tooltip("One invisible box collider covering the whole grid, top surface at Y = 0. Models stand on this, so no ground plane or per-tile colliders are needed.")]
    [SerializeField] private bool generateGroundCollider = true;
    [SerializeField] private float groundColliderThickness = 1f;

    [Header("Ground plane (optional, visual only now)")]
    [Tooltip("Assumes Unity's built-in Plane (10x10 units at scale 1).")]
    [SerializeField] private Transform groundPlane;
    [Tooltip("Fits the plane on Awake and whenever the map is rebuilt.")]
    [SerializeField] private bool fitGroundPlaneOnAwake = true;

    [Header("Terrain tuning (Inspector)")]
    [SerializeField] private TerrainProfile plain = new TerrainProfile { moveEnergyPerSecond = 1f, speedMultiplier = 1.0f, sleepQuality = 1.0f };
    [SerializeField] private TerrainProfile muddy = new TerrainProfile { moveEnergyPerSecond = 2f, speedMultiplier = 0.7f, sleepQuality = 0.7f };
    [SerializeField] private TerrainProfile stony = new TerrainProfile { moveEnergyPerSecond = 2f, speedMultiplier = 0.85f, sleepQuality = 0.5f };
    [SerializeField] private TerrainProfile water = new TerrainProfile { moveEnergyPerSecond = 4f, speedMultiplier = 0.5f, sleepQuality = 0f };

    [Header("Debug")]
    [Tooltip("Edit-mode preview only (colored squares in the Scene view). In Play mode the spawned tiles are used instead.")]
    [SerializeField] private bool DebugDrawGrid = true;

    private const float FallbackThickness = 0.1f;

    private TerrainType[,] terrain;
    private Transform tileRoot;
    private GameObject groundColliderObject;
    private bool tilesDirty;
    private readonly List<Material> fallbackMaterials = new List<Material>();
    private readonly List<Collider> colliderBuffer = new List<Collider>();

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GridManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildMap();
        RefreshVisuals();
        tilesDirty = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DestroyTiles();
        if (groundColliderObject != null) Destroy(groundColliderObject);
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        cellSize = Mathf.Max(0.01f, cellSize);
        terrain = null; // rebuild lazily so Inspector edits show up immediately

        // Tiles can't be spawned/destroyed inside OnValidate; flag it and let Update do it.
        if (Application.isPlaying) tilesDirty = true;
    }

    private void Update()
    {
        if (!tilesDirty) return;
        tilesDirty = false;
        RefreshVisuals();
    }

    private void EnsureMap()
    {
        if (terrain == null || terrain.GetLength(0) != width || terrain.GetLength(1) != height)
            BuildMap();
    }

    private void RefreshVisuals()
    {
        RebuildGroundCollider();
        RebuildTiles();
        if (fitGroundPlaneOnAwake && groundPlane != null) FitGroundPlane();
    }

    /// <summary>One box collider for the whole grid; far cheaper and smoother than one per tile.</summary>
    private void RebuildGroundCollider()
    {
        if (groundColliderObject != null) Destroy(groundColliderObject);
        groundColliderObject = null;
        if (!generateGroundCollider) return;

        float sx = width * cellSize;
        float sz = height * cellSize;
        float thickness = Mathf.Max(0.1f, groundColliderThickness);

        GameObject go = new GameObject("Ground Collider (generated)");
        go.transform.position = new Vector3(sx * 0.5f, -thickness * 0.5f, sz * 0.5f); // top surface at Y = 0
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(sx, thickness, sz);
        groundColliderObject = go;
    }

    [ContextMenu("Fit Ground Plane")]
    public void FitGroundPlane()
    {
        if (groundPlane == null)
        {
            Debug.LogWarning("[GridManager] No ground plane assigned.");
            return;
        }
        float sx = width * cellSize;
        float sz = height * cellSize;
        Vector3 p = groundPlane.position;
        groundPlane.position = new Vector3(sx * 0.5f, p.y, sz * 0.5f);
        groundPlane.localScale = new Vector3(sx / 10f, groundPlane.localScale.y, sz / 10f);
    }

    // ------------------------------------------------------------------ map building

    private void BuildMap()
    {
        terrain = new TerrainType[width, height];
        FillRect(0, 0, width - 1, height - 1, TerrainType.Plain);

        if (useDefaultTestPatches) ApplyDefaultTestPatches();
        if (randomScatter != null && randomScatter.enabled) ApplyRandomScatter();

        if (customPatches != null)
        {
            foreach (TerrainPatch p in customPatches)
            {
                FillRect(
                    Mathf.Min(p.from.x, p.to.x), Mathf.Min(p.from.y, p.to.y),
                    Mathf.Max(p.from.x, p.to.x), Mathf.Max(p.from.y, p.to.y),
                    p.type);
            }
        }
    }

    /// <summary>Hardcoded, reproducible patches; each non-plain type is within 15 cells of (0,0).</summary>
    private void ApplyDefaultTestPatches()
    {
        FillRect(3, 3, 6, 5, TerrainType.Muddy);
        FillRect(10, 3, 12, 5, TerrainType.Stony);
        FillRect(6, 8, 9, 10, TerrainType.Water);

        FillRect(18, 4, 22, 7, TerrainType.Muddy);
        FillRect(4, 20, 8, 24, TerrainType.Stony);
        FillRect(20, 20, 26, 26, TerrainType.Water);
    }

    private void ApplyRandomScatter()
    {
        var rng = new System.Random(randomScatter.seed); // seeded => reproducible
        ScatterType(rng, TerrainType.Muddy, randomScatter.muddyPatches);
        ScatterType(rng, TerrainType.Stony, randomScatter.stonyPatches);
        ScatterType(rng, TerrainType.Water, randomScatter.waterPatches);
    }

    private void ScatterType(System.Random rng, TerrainType type, int count)
    {
        int minX = Mathf.Max(1, randomScatter.minPatchSize.x);
        int minZ = Mathf.Max(1, randomScatter.minPatchSize.y);
        int maxX = Mathf.Max(minX, randomScatter.maxPatchSize.x);
        int maxZ = Mathf.Max(minZ, randomScatter.maxPatchSize.y);

        for (int i = 0; i < count; i++)
        {
            int sx = Mathf.Min(rng.Next(minX, maxX + 1), width);
            int sz = Mathf.Min(rng.Next(minZ, maxZ + 1), height);
            int x0 = rng.Next(0, width - sx + 1);
            int z0 = rng.Next(0, height - sz + 1);
            FillRect(x0, z0, x0 + sx - 1, z0 + sz - 1, type);
        }
    }

    private void FillRect(int x0, int z0, int x1, int z1, TerrainType type)
    {
        if (type == TerrainType.OutOfBounds) return; // OOB is computed, never stored

        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                if (x >= 0 && x < width && z >= 0 && z < height)
                    terrain[x, z] = type;
    }

    // ------------------------------------------------------------------ tile spawning (display layer)

    /// <summary>Destroys and respawns all tiles from the current terrain array. Play mode only.</summary>
    [ContextMenu("Rebuild Tiles (Play Mode)")]
    public void RebuildTiles()
    {
        if (!Application.isPlaying) return;

        EnsureMap();
        DestroyTiles();

        tileRoot = new GameObject("Tiles (generated)").transform;

        // Resolve one source object per storable terrain type (Plain..Water).
        const int typeCount = 4;
        GameObject[] sources = new GameObject[typeCount];
        float[] yShift = new float[typeCount];
        List<GameObject> templates = new List<GameObject>();

        for (int i = 0; i < typeCount; i++)
        {
            TerrainType type = (TerrainType)i;
            GameObject prefab = GetTilePrefab(type);

            if (prefab != null)
            {
                sources[i] = prefab;
            }
            else if (useFallbackTiles)
            {
                GameObject template = CreateFallbackTemplate(type);
                template.transform.SetParent(tileRoot, false);
                sources[i] = template;
                yShift[i] = -FallbackThickness * 0.5f; // keep the cube's top surface at tileYOffset
                templates.Add(template);
            }
            else
            {
                Debug.LogWarning($"[GridManager] No tile prefab assigned for {type}; those cells will be empty.", this);
            }
        }

        Vector3 sizeScale = scaleTilesToCellSize ? new Vector3(cellSize, 1f, cellSize) : Vector3.one;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int typeIndex = (int)terrain[x, z];
                GameObject src = sources[typeIndex];
                if (src == null) continue;

                Vector3 pos = CellCenterToWorld(x, z);
                pos.y = tileYOffset + yShift[typeIndex];

                GameObject tile = Instantiate(src, pos, src.transform.rotation, tileRoot);
                tile.name = $"Tile {x},{z}";
                tile.transform.localScale = Vector3.Scale(src.transform.localScale, sizeScale);

                if (!keepTileColliders) StripColliders(tile);
            }
        }

        // Clones are independent, so the fallback templates are no longer needed.
        foreach (GameObject t in templates) Destroy(t);
    }

    private void DestroyTiles()
    {
        if (tileRoot != null) Destroy(tileRoot.gameObject);
        tileRoot = null;

        foreach (Material m in fallbackMaterials)
            if (m != null) Destroy(m);
        fallbackMaterials.Clear();
    }

    private void StripColliders(GameObject tile)
    {
        tile.GetComponentsInChildren(true, colliderBuffer);
        foreach (Collider c in colliderBuffer) Destroy(c);
    }

    private GameObject GetTilePrefab(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.Plain: return plainTilePrefab;
            case TerrainType.Muddy: return muddyTilePrefab;
            case TerrainType.Stony: return stonyTilePrefab;
            case TerrainType.Water: return waterTilePrefab;
            default: return null;
        }
    }

    private GameObject CreateFallbackTemplate(TerrainType type)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"FallbackTile_{type}";
        go.transform.localScale = new Vector3(1f, FallbackThickness, 1f);

        Collider col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col); // immediate, so clones made this frame don't inherit it

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = ColorFor(type);
            c.a = 1f;
            Material m = new Material(r.sharedMaterial) { color = c };
            r.sharedMaterial = m;
            fallbackMaterials.Add(m);
        }
        return go;
    }

    // ------------------------------------------------------------------ coordinate conversion

    /// <summary>World position of the cell's CORNER (min X, min Z).</summary>
    public Vector3 CellToWorld(int cx, int cz)
    {
        return new Vector3(cx * cellSize, 0f, cz * cellSize);
    }

    /// <summary>World position of the cell's center. Use this for movement targets.</summary>
    public Vector3 CellCenterToWorld(int cx, int cz)
    {
        return new Vector3((cx + 0.5f) * cellSize, 0f, (cz + 0.5f) * cellSize);
    }

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / cellSize),
            Mathf.FloorToInt(worldPos.z / cellSize));
    }

    // ------------------------------------------------------------------ queries

    public bool IsInBounds(int cx, int cz)
    {
        return cx >= 0 && cx < width && cz >= 0 && cz < height;
    }

    public TerrainType GetTerrainAt(int cx, int cz)
    {
        if (!IsInBounds(cx, cz)) return TerrainType.OutOfBounds;
        EnsureMap();
        return terrain[cx, cz];
    }

    public TerrainType GetTerrainAt(Vector2Int cell) => GetTerrainAt(cell.x, cell.y);

    public TerrainType GetTerrainAt(Vector3 worldPos)
    {
        Vector2Int c = WorldToCell(worldPos);
        return GetTerrainAt(c.x, c.y);
    }

    public bool IsWalkable(int cx, int cz, bool canSwim)
    {
        TerrainType t = GetTerrainAt(cx, cz);

        // TODO: GDD says water can be used as a border — confirm OOB should always be impassable
        if (t == TerrainType.OutOfBounds) return false;

        if (t == TerrainType.Water) return canSwim;
        return true;
    }

    public bool IsWalkable(Vector2Int cell, bool canSwim) => IsWalkable(cell.x, cell.y, canSwim);

    public bool IsWalkable(Vector3 worldPos, bool canSwim)
    {
        Vector2Int c = WorldToCell(worldPos);
        return IsWalkable(c.x, c.y, canSwim);
    }

    /// <summary>Nobody sleeps in Water (they pass out instead) or outside the map.</summary>
    public bool CanSleepOn(TerrainType type)
    {
        return type != TerrainType.Water && type != TerrainType.OutOfBounds;
    }

    public TerrainProfile GetProfile(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.Plain: return plain;
            case TerrainType.Muddy: return muddy;
            case TerrainType.Stony: return stony;
            case TerrainType.Water: return water;
            default: return plain; // OutOfBounds: fall back to plain so nothing gets stuck
        }
    }

    // ------------------------------------------------------------------ edit-mode preview

    // EDIT-MODE PREVIEW ONLY. In Play mode the spawned tiles replace this.
    // The 2D array above is the source of truth.
    private void OnDrawGizmos()
    {
        if (!DebugDrawGrid || Application.isPlaying) return;
        EnsureMap();

        Vector3 size = new Vector3(cellSize * 0.95f, 0.02f, cellSize * 0.95f);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Gizmos.color = ColorFor(terrain[x, z]);
                Gizmos.DrawCube(CellCenterToWorld(x, z), size);
            }
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(
            new Vector3(width * cellSize * 0.5f, 0f, height * cellSize * 0.5f),
            new Vector3(width * cellSize, 0f, height * cellSize));
    }

    private static Color ColorFor(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Plain: return new Color(0.45f, 0.80f, 0.40f, 0.45f);
            case TerrainType.Muddy: return new Color(0.50f, 0.32f, 0.15f, 0.70f);
            case TerrainType.Stony: return new Color(0.55f, 0.55f, 0.58f, 0.70f);
            case TerrainType.Water: return new Color(0.20f, 0.45f, 0.90f, 0.70f);
            default: return Color.magenta;
        }
    }
}