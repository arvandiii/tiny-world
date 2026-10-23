using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A consumable food item. Uses a trigger collider: when a Model that is hungry
/// touches it, the Model eats it (hunger and a little energy go up) and it is destroyed.
///
/// All active Food objects register themselves in Food.All so Models can look for
/// the nearest one without physics queries.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class Food : MonoBehaviour
{
    public static readonly List<Food> All = new List<Food>();

    [Tooltip("Hunger restored (hunger is 0-100, 100 = full).")]
    public float hungerRestore = 30f;
    [Tooltip("Energy restored, a small bonus compared to sleeping.")]
    public float energyRestore = 5f;

    // TODO: different food kinds (berries, fish, meat, apples) with different values

    public bool IsConsumed { get; private set; }

    // Keeps the static list clean when "Enter Play Mode Options" has domain reload disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    private void Awake()
    {
        GetComponent<SphereCollider>().isTrigger = true;
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void OnTriggerEnter(Collider other) => TryConsumeFrom(other);
    private void OnTriggerStay(Collider other) => TryConsumeFrom(other);

    private void TryConsumeFrom(Collider other)
    {
        if (IsConsumed) return;
        Model model = other.GetComponentInParent<Model>();
        if (model != null) TryConsume(model);
    }

    /// <summary>Returns true if the Model ate this food.</summary>
    public bool TryConsume(Model model)
    {
        if (IsConsumed || model == null || !model.CanEat) return false;

        IsConsumed = true;   // blocks double-eating in the same physics step
        model.Eat(this);
        Destroy(gameObject);
        return true;
    }
}