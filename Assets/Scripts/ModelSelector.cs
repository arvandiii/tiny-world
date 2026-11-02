using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Click-to-select for Models.
///
///   - Left click on a Model: select it (the CameraController starts following it).
///   - Left click on empty ground: deselect (optional).
///   - Esc: deselect (optional).
///
/// Other systems (admin panel, controlled movement) can read ModelSelector.Instance.Selected
/// or subscribe to SelectionChanged.
/// </summary>
public class ModelSelector : MonoBehaviour
{
    public static ModelSelector Instance { get; private set; }

    /// <summary>Fired whenever the selection changes. The argument is null when nothing is selected.</summary>
    public event Action<Model> SelectionChanged;

    public Model Selected { get; private set; }

    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera cam;
    [SerializeField] private CameraController cameraController;

    [Header("Picking")]
    [Tooltip("Layers the click ray can hit. Leave on Everything unless you use layers for Models.")]
    [SerializeField] private LayerMask pickLayers = ~0;
    [SerializeField] private float maxRayDistance = 500f;

    [Header("Behavior")]
    [SerializeField] private bool deselectOnEmptyClick = true;
    [SerializeField] private bool deselectOnEscape = true;
    [Tooltip("Ignore clicks while the pointer is over UI (admin panel, buttons).")]
    [SerializeField] private bool ignoreClicksOverUI = true;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ModelSelector] Duplicate instance destroyed.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (cam == null) cam = Camera.main;
        if (cameraController == null) cameraController = FindFirstObjectByType<CameraController>();

        if (cam == null)
        {
            Debug.LogError("[ModelSelector] No camera found. Tag your camera MainCamera or assign one.", this);
            enabled = false;
            return;
        }

        if (cameraController == null)
            Debug.LogWarning("[ModelSelector] No CameraController found; selection will work but the camera won't follow.", this);
    }

    private void Update()
    {
        if (deselectOnEscape && EscapePressedThisFrame())
        {
            Select(null);
            return;
        }

        if (!LeftClickPressedThisFrame()) return;
        if (ignoreClicksOverUI && IsPointerOverUI()) return;

        TrySelectAt(GetPointerPosition());
    }

    // ------------------------------------------------------------------ selection

    private void TrySelectAt(Vector2 screenPosition)
    {
        Ray ray = cam.ScreenPointToRay(screenPosition);

        // Triggers (such as Food) are ignored so they never block a click on a Model.
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, pickLayers, QueryTriggerInteraction.Ignore))
        {
            Model model = hit.collider.GetComponentInParent<Model>();
            if (model != null)
            {
                Select(model);
                return;
            }
        }

        // Clicked ground or nothing.
        if (deselectOnEmptyClick) Select(null);
    }

    public void Select(Model model)
    {
        if (model == Selected) return;
        Selected = model;

        if (cameraController != null)
        {
            if (model != null) cameraController.SelectModel(model);
            else cameraController.Deselect();
        }

        SelectionChanged?.Invoke(model);
    }

    // ------------------------------------------------------------------ input helpers (new Input System, old Input Manager, or both)

    private static bool LeftClickPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static bool EscapePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private static Vector2 GetPointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}