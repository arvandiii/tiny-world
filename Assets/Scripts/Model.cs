using UnityEngine;

/// <summary>
/// Rule-based prototype NPC. Random-walks to walkable cells within sightRange,
/// pays terrain-dependent energy, sleeps when exhausted, and passes out / dies
/// if it runs out of energy in water.
/// Long-term this behavior is meant to be replaced by a trained AI.
///
/// Movement is driven by code (terrain speed multipliers), not by physics tricks.
/// The Rigidbody only provides gravity and collision; we set its horizontal
/// velocity and leave the vertical velocity to gravity.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Model : MonoBehaviour
{
    [Header("Vitals")]
    [Range(0f, 100f)] public float energy = 100f;
    public float maxEnergy = 100f;
    [Range(0f, 100f)] public float hunger = 100f;   // 100 = full, 0 = starving
    [Range(0f, 100f)] public float health = 100f;

    [Header("Movement")]
    public float moveSpeed = 2f;                    // world units / second on Plain at full energy
    [Tooltip("In cells (1 cell = 1 m at cellSize 1).")]
    public float sightRange = 15f;
    public bool canSwim = false;

    [Header("Identity (behavior not implemented yet)")]
    public Gender gender = Gender.Unspecified;      // TODO: gender effects
    public bool wantsMate = false;                  // TODO: mating behavior
    public ModelRole role = ModelRole.None;         // TODO: roles once a community forms
    public Personality personality = Personality.None; // TODO: personality-driven priorities

    [Header("State")]
    public bool isSleeping = false;
    public bool isPassedOut = false;                // out of energy in water
    public bool isDead = false;

    [Header("Tuning")]
    [Tooltip("Hunger lost per second while moving.")]
    public float hungerDrainPerSecondMoving = 0.2f;
    [Tooltip("Distance (world units) to target center that counts as reached.")]
    public float arrivalDistance = 0.1f;
    [Tooltip("Random samples per pick before giving up this tick.")]
    public int maxTargetAttempts = 20;
    [Tooltip("Below this fraction of maxEnergy the Model starts slowing down.")]
    [Range(0f, 1f)] public float lowEnergyThreshold = 0.25f;
    [Tooltip("Speed factor at 0 energy (linearly scaled up to 1 at the threshold).")]
    [Range(0f, 1f)] public float minSpeedFactor = 0.3f;

    [Header("Pause between destinations")]
    public float pauseMin = 0.3f;
    public float pauseMax = 3f;

    [Header("Sleep")]
    [Tooltip("Seconds to refill 0 -> maxEnergy on ideal terrain (quality 1.0).")]
    public float sleepSecondsToFull = 60f;
    [Tooltip("Falls asleep voluntarily at or below this energy (never in water). 0 = only when fully drained.")]
    public float voluntarySleepEnergy = 15f;

    [Header("Passing out in water")]
    [Tooltip("Health lost per second while passed out. 5 => dies in 20 s from full health.")]
    public float passedOutHealthDrainPerSecond = 5f;

    [Header("Spawn")]
    public bool snapToCellCenterOnStart = true;

    private Rigidbody rb;
    private GridManager grid;
    private Vector3 target;
    private bool hasTarget;
    private float pauseTimer;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation; // keep the capsule upright
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        grid = GridManager.Instance;
        if (grid == null)
        {
            Debug.LogError("[Model] No GridManager in the scene. Disabling.", this);
            enabled = false;
            return;
        }

        Vector2Int c = grid.WorldToCell(transform.position);
        if (snapToCellCenterOnStart)
        {
            Vector3 p = grid.CellCenterToWorld(c.x, c.y);
            transform.position = new Vector3(p.x, transform.position.y, p.z);
        }

        if (!grid.IsInBounds(c.x, c.y))
            Debug.LogWarning("[Model] Spawned outside the grid; it cannot pick walkable targets from here.", this);
    }

    private void OnValidate()
    {
        maxEnergy = Mathf.Max(1f, maxEnergy);
        energy = Mathf.Clamp(energy, 0f, maxEnergy);
        sightRange = Mathf.Max(0f, sightRange);
        sleepSecondsToFull = Mathf.Max(0.01f, sleepSecondsToFull);
        pauseMin = Mathf.Max(0f, pauseMin);
        pauseMax = Mathf.Max(pauseMin, pauseMax);
    }

    // All logic runs in FixedUpdate because movement is applied through the Rigidbody.
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (isDead)
        {
            StopHorizontal();
            return;
        }

        TerrainType here = grid.GetTerrainAt(rb.position);

        if (isPassedOut)
        {
            StopHorizontal();
            UpdatePassedOut(dt);
            return;
        }

        if (isSleeping)
        {
            StopHorizontal();
            UpdateSleep(dt, here);
            return;
        }

        // Completely drained: instant sleep on land, pass out in water.
        if (energy <= 0f)
        {
            StopHorizontal();
            if (here == TerrainType.Water) PassOut();
            else StartSleep();
            return;
        }

        // Voluntary sleep when low on energy, but never in water.
        if (voluntarySleepEnergy > 0f && energy <= voluntarySleepEnergy && grid.CanSleepOn(here))
        {
            StopHorizontal();
            StartSleep();
            return;
        }
        // TODO: if low on energy while in water, head for the nearest shore

        // Pause after reaching a destination.
        if (pauseTimer > 0f)
        {
            pauseTimer -= dt;
            StopHorizontal();
            return;
        }

        if (!hasTarget) PickTarget();
        if (hasTarget) MoveTowardTarget(dt, here);
        else StopHorizontal();
    }

    // ------------------------------------------------------------------ sleep

    private void StartSleep()
    {
        isSleeping = true;
        hasTarget = false;
    }

    private void UpdateSleep(float dt, TerrainType here)
    {
        float quality = grid.GetProfile(here).sleepQuality;
        float regenPerSecond = (maxEnergy / sleepSecondsToFull) * quality;
        energy = Mathf.Min(maxEnergy, energy + regenPerSecond * dt);

        if (energy >= maxEnergy) isSleeping = false;

        // TODO: food restores a small amount of energy (not implemented, no food yet)
        // TODO: temperature affects sleep quality (cold spots)
    }

    // ------------------------------------------------------------------ passing out / death

    private void PassOut()
    {
        isPassedOut = true;
        hasTarget = false;
    }

    private void UpdatePassedOut(float dt)
    {
        health = Mathf.Max(0f, health - passedOutHealthDrainPerSecond * dt);
        if (health <= 0f) Die();

        // TODO: allow rescue (another Model or the current dragging the body out of the water)
    }

    private void Die()
    {
        isDead = true;
        isPassedOut = false;
        isSleeping = false;
        hasTarget = false;
        health = 0f;
        Debug.Log($"[Model] {name} died (ran out of energy in water).", this);

        // TODO: death handling (corpse, removal, notify community)
    }

    // ------------------------------------------------------------------ target picking

    private void PickTarget()
    {
        Vector2Int origin = grid.WorldToCell(rb.position);

        for (int i = 0; i < maxTargetAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * sightRange;
            Vector2Int cell = origin + new Vector2Int(Mathf.RoundToInt(offset.x), Mathf.RoundToInt(offset.y));

            if (cell == origin) continue;
            if (!grid.IsWalkable(cell, canSwim)) continue;

            target = grid.CellCenterToWorld(cell.x, cell.y);
            hasTarget = true;
            return;
        }
        // No valid target found this tick; try again next tick.

        // TODO: sight limit drops to 7 / 5 m at night or in dark environments
    }

    // ------------------------------------------------------------------ movement

    private void MoveTowardTarget(float dt, TerrainType standingOn)
    {
        Vector3 pos = rb.position;
        Vector3 toTarget = target - pos;
        toTarget.y = 0f; // horizontal only; gravity handles Y
        float dist = toTarget.magnitude;

        if (dist <= arrivalDistance)
        {
            hasTarget = false;
            pauseTimer = Random.Range(pauseMin, pauseMax);
            StopHorizontal();
            return;
        }

        // Speed depends on the terrain under the Model and on its energy level.
        GridManager.TerrainProfile profile = grid.GetProfile(standingOn);
        float speed = moveSpeed * profile.speedMultiplier * EnergySpeedFactor();
        float step = Mathf.Min(speed * dt, dist);
        Vector3 dir = toTarget / dist;
        Vector3 next = pos + dir * step;

        // Straight-line walk, no pathfinding: if the next step enters an
        // unwalkable cell, abandon this target and re-pick immediately (no pause).
        if (!grid.IsWalkable(next, canSwim))
        {
            hasTarget = false;
            StopHorizontal();
            return;
        }

        Vector3 horizontalVelocity = dir * (step / dt);
        rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);

        // Costs depend on the terrain the Model is standing on (current cell).
        energy = Mathf.Max(0f, energy - profile.moveEnergyPerSecond * dt);
        hunger = Mathf.Max(0f, hunger - hungerDrainPerSecondMoving * dt);

        // TODO: low hunger makes energy drain faster and decreases health
        // TODO: hunger at 0 should make finding food the top priority
        // TODO: swimming — non-swimmers may learn to swim at an energy cost
        // TODO: other health changes (food, dangers)
    }

    private void StopHorizontal()
    {
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(0f, v.y, 0f);
    }

    private float EnergySpeedFactor()
    {
        if (lowEnergyThreshold <= 0f) return 1f;
        float ratio = energy / maxEnergy;
        float t = Mathf.Clamp01(ratio / lowEnergyThreshold);
        return Mathf.Lerp(minSpeedFactor, 1f, t);
    }

    // ------------------------------------------------------------------ debug

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, sightRange);

        if (Application.isPlaying && hasTarget)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, target);
            Gizmos.DrawWireSphere(target, 0.25f);
        }
    }
}