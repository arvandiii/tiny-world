using UnityEngine;

/// <summary>
/// Global temperature (degrees Celsius) and its effects on Models.
/// For now the temperature is one value for the whole world. Models ask
/// GetTemperatureAt(position), so local temperatures (cold spots, day/night,
/// weather) can be plugged in later without touching Model code.
///
/// Cold effects (all start below their threshold and grow as it gets colder):
///   - Sleep quality: multiplied down, up to minSleepQualityFactor at maxColdTemperature.
///   - Movement: a flat speed-multiplier penalty on EVERY terrain (same absolute amount on all tiles).
///   - Energy cost: movement energy drain is multiplied up per degree below energyCostColdThreshold.
/// </summary>
[DefaultExecutionOrder(-90)]
public class TemperatureManager : MonoBehaviour
{
    public static TemperatureManager Instance { get; private set; }

    [Header("Current")]
    [Tooltip("World temperature in degrees Celsius. Editable at runtime for testing.")]
    [SerializeField] private float globalTemperature = 20f;

    [Header("Cold range (sleep + movement)")]
    [Tooltip("Cold effects on sleep and movement start below this temperature.")]
    [SerializeField] private float coldThreshold = 10f;
    [Tooltip("At or below this temperature the sleep and movement penalties are at their maximum.")]
    [SerializeField] private float maxColdTemperature = -10f;

    [Header("Cold: sleep")]
    [Tooltip("Sleep regeneration multiplier at maximum cold (1 = no penalty).")]
    [Range(0f, 1f)]
    [SerializeField] private float minSleepQualityFactor = 0.4f;

    [Header("Cold: movement")]
    [Tooltip("Flat amount subtracted from every terrain's speedMultiplier at maximum cold. 0.2 => Plain 1.0 becomes 0.8, Stony 0.85 becomes 0.65.")]
    [SerializeField] private float maxColdSpeedPenalty = 0.2f;
    [Tooltip("Speed multiplier will never be pushed below this by cold.")]
    [SerializeField] private float minSpeedMultiplier = 0.1f;

    [Header("Cold: energy cost")]
    [Tooltip("Movement energy cost starts increasing below this temperature.")]
    [SerializeField] private float energyCostColdThreshold = 5f;
    [Tooltip("Extra energy cost per degree below the threshold. 0.02 => +2% per degree.")]
    [SerializeField] private float energyCostIncreasePerDegree = 0.02f;
    [Tooltip("Upper limit for the energy cost multiplier.")]
    [SerializeField] private float maxEnergyCostMultiplier = 2f;

    // TODO: hot temperatures (heat exhaustion: faster energy drain, thirst/dehydration, health damage, worse sleep)
    // TODO: water temperature (separate from air temperature; affects swimming energy cost and health, cold water is dangerous)
    // TODO: temperature affects how soon food rots (cold = lasts longer, hot = rots sooner). See Food.cs
    // TODO: corpse decomposition speed depends on temperature (future corpse feature)
    // TODO: water freezes in low temperatures, forming frozen lakes / rivers / seas (walkable ice). See GridManager
    // TODO: time of day (cold nights, hot days), weather (rain lowers temperature) and local cold spots driving GetTemperatureAt

    public float GlobalTemperature => globalTemperature;

    public void SetGlobalTemperature(float celsius)
    {
        globalTemperature = celsius;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TemperatureManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        maxColdTemperature = Mathf.Min(maxColdTemperature, coldThreshold - 0.1f);
        maxColdSpeedPenalty = Mathf.Max(0f, maxColdSpeedPenalty);
        minSpeedMultiplier = Mathf.Max(0f, minSpeedMultiplier);
        energyCostIncreasePerDegree = Mathf.Max(0f, energyCostIncreasePerDegree);
        maxEnergyCostMultiplier = Mathf.Max(1f, maxEnergyCostMultiplier);
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Temperature at a world position. Global for now; this is the hook for local temperatures later.</summary>
    public float GetTemperatureAt(Vector3 worldPos)
    {
        return globalTemperature;
    }

    /// <summary>0 at or above coldThreshold, 1 at or below maxColdTemperature, linear in between.</summary>
    public float GetColdFactor(float temperature)
    {
        float range = Mathf.Max(0.01f, coldThreshold - maxColdTemperature);
        return Mathf.Clamp01((coldThreshold - temperature) / range);
    }

    /// <summary>Multiplier (0..1) applied on top of the terrain's sleep quality.</summary>
    public float GetSleepQualityMultiplier(float temperature)
    {
        return Mathf.Lerp(1f, minSleepQualityFactor, GetColdFactor(temperature));
    }

    /// <summary>Flat penalty applied equally to the speed multiplier of every terrain.</summary>
    public float AdjustSpeedMultiplier(float terrainSpeedMultiplier, float temperature)
    {
        float penalty = GetColdFactor(temperature) * maxColdSpeedPenalty;
        float floor = Mathf.Min(terrainSpeedMultiplier, minSpeedMultiplier);
        return Mathf.Max(floor, terrainSpeedMultiplier - penalty);
    }

    /// <summary>Multiplier (>= 1) applied to movement energy drain.</summary>
    public float GetEnergyCostMultiplier(float temperature)
    {
        float degreesBelow = Mathf.Max(0f, energyCostColdThreshold - temperature);
        return Mathf.Min(maxEnergyCostMultiplier, 1f + degreesBelow * energyCostIncreasePerDegree);
    }
}