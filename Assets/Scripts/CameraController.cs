using UnityEngine;

/// <summary>
/// Orbit camera that smoothly follows the selected Model.
/// Attach to the Main Camera.
///
/// Controls:
///   - Hold Right Mouse Button and move the mouse: orbit around the focus point
///     (yaw is free, pitch is limited to minPitch..maxPitch).
///   - Mouse wheel: zoom in / out (distance limited to minDistance..maxDistance).
///   - WASD (no model selected): fly the focus point in the direction the camera
///     is facing (pitch included), so looking down + W moves you down/forward.
///     Q/E raise/lower in world space.
///
/// Following: while a Model is selected, the focus point glides toward it
/// (SmoothDamp), so selecting a Model causes a soft transition rather than a cut.
///
/// Pitch is the elevation angle: 0 = camera level with the focus point, 90 = straight down.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Set automatically by ModelSelector when you click a Model. Can still be ticked by hand for testing.")]
    [SerializeField] private bool isModelSelected = false;
    [SerializeField] private Model selectedModel;
    [Tooltip("Offset from the Model's position to the point the camera looks at.")]
    [SerializeField] private Vector3 focusOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Orbit (hold Right Mouse Button)")]
    [Range(0f, 89f)] [SerializeField] private float minPitch = 10f;
    [Range(0f, 89f)] [SerializeField] private float maxPitch = 80f;
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField] private float rotateSensitivity = 0.2f;
    [SerializeField] private bool invertY = false;

    [Header("Zoom (Mouse Wheel)")]
    [SerializeField] private float minDistance = 4f;
    [SerializeField] private float maxDistance = 30f;
    [Tooltip("Fraction of the current distance changed per wheel notch. 0.1 = 10%.")]
    [SerializeField] private float zoomStrength = 0.1f;

    [Header("Fly (WASD, no Model selected)")]
    [Tooltip("Target fly speed in world units per second. Scales with zoom distance.")]
    [SerializeField] private float flySpeed = 10f;
    [Tooltip("How quickly fly velocity ramps toward the target speed. Higher = snappier.")]
    [SerializeField] private float flyAcceleration = 8f;
    [Tooltip("How quickly fly velocity decays when no key is held. Lower = more glide.")]
    [SerializeField] private float flyDamping = 6f;
    [SerializeField] private KeyCode forwardKey  = KeyCode.W;
    [SerializeField] private KeyCode backKey     = KeyCode.S;
    [SerializeField] private KeyCode leftKey     = KeyCode.A;
    [SerializeField] private KeyCode rightKey    = KeyCode.D;
    [SerializeField] private KeyCode upKey       = KeyCode.E;
    [SerializeField] private KeyCode downKey     = KeyCode.Q;
    [Tooltip("Speed multiplier while holding Left Shift.")]
    [SerializeField] private float flySprintMultiplier = 3f;
    [Tooltip("When true, Q/E move in world up/down. When false, they move along the camera's up vector (feels like flying).")]
    [SerializeField] private bool worldUpForQe = true;

    [Header("Smoothing (seconds, lower = snappier)")]
    [SerializeField] private float followSmoothTime = 0.25f;
    [SerializeField] private float rotationSmoothTime = 0.08f;
    [SerializeField] private float zoomSmoothTime = 0.12f;

    // Current (smoothed) state
    private Vector3 focusPoint;
    private float yaw, pitch, distance;
    // Desired state (what input changes)
    private float targetYaw, targetPitch, targetDistance;
    // SmoothDamp velocities
    private Vector3 focusVelocity;
    private float yawVelocity, pitchVelocity, distanceVelocity;

    // Fly velocity (smoothed, in world units/second)
    private Vector3 flyVelocity;

#if !ENABLE_INPUT_SYSTEM
    private Vector2 lastMousePosition;
#endif

    // ------------------------------------------------------------------ public API

    public void SelectModel(Model model)
    {
        // If a previous model is still selected, tell its dialogue component to stop facing.
        if (selectedModel != null)
        {
            var prevDialogue = selectedModel.GetComponent<ModelDialogue>();
            if (prevDialogue != null) prevDialogue.SetSelected(false);
        }

        selectedModel = model;
        isModelSelected = model != null;

        // Tell the newly-selected model's dialogue component to face the camera.
        if (selectedModel != null)
        {
            var nextDialogue = selectedModel.GetComponent<ModelDialogue>();
            if (nextDialogue != null) nextDialogue.SetSelected(true);
        }
    }

    public void Deselect()
    {
        if (selectedModel != null)
        {
            var prevDialogue = selectedModel.GetComponent<ModelDialogue>();
            if (prevDialogue != null) prevDialogue.SetSelected(false);
        }

        selectedModel = null;
        isModelSelected = false;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = targetYaw = euler.y;
        pitch = targetPitch = Mathf.Clamp(NormalizeAngle(euler.x), minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        Plane ground = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(transform.position, rot * Vector3.forward);
        float startDistance = 12f;
        if (ground.Raycast(ray, out float hit) && hit > 0.01f) startDistance = hit;

        distance = targetDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
        focusPoint = transform.position + rot * Vector3.forward * distance;

#if !ENABLE_INPUT_SYSTEM
        lastMousePosition = Input.mousePosition;
#endif
    }

    private void OnValidate()
    {
        minPitch = Mathf.Clamp(minPitch, 0f, 89f);
        maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
        minDistance = Mathf.Max(0.5f, minDistance);
        maxDistance = Mathf.Max(minDistance, maxDistance);
        followSmoothTime = Mathf.Max(0.001f, followSmoothTime);
        rotationSmoothTime = Mathf.Max(0.001f, rotationSmoothTime);
        zoomSmoothTime = Mathf.Max(0.001f, zoomSmoothTime);
        flySpeed = Mathf.Max(0f, flySpeed);
        flyAcceleration = Mathf.Max(0.01f, flyAcceleration);
        flyDamping = Mathf.Max(0.01f, flyDamping);
        flySprintMultiplier = Mathf.Max(1f, flySprintMultiplier);
    }

    private void LateUpdate()
    {
        HandleInput();
        UpdateFocus();
        ApplyCamera();
    }

    // ------------------------------------------------------------------ input

    private void HandleInput()
    {
        Vector2 mouseDelta = GetMouseDelta();

        if (IsOrbitHeld())
        {
            targetYaw += mouseDelta.x * rotateSensitivity;
            float pitchDelta = mouseDelta.y * rotateSensitivity * (invertY ? 1f : -1f);
            targetPitch = Mathf.Clamp(targetPitch + pitchDelta, minPitch, maxPitch);
        }

        float scroll = GetScrollNotches();
        if (Mathf.Abs(scroll) > 0.001f)
        {
            targetDistance *= 1f - scroll * zoomStrength;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }

        UpdateFlyVelocity();
    }

    private void UpdateFlyVelocity()
    {
        // Build the desired direction in *camera space*, including pitch,
        // so W flies you where the lens is pointing, not just flat forward.
        Vector3 inputDir = Vector3.zero;

        // Only accept WASD input when no model is selected.
        if (!isModelSelected)
        {
            if (IsKeyHeld(forwardKey))  inputDir += transform.forward;
            if (IsKeyHeld(backKey))     inputDir -= transform.forward;
            if (IsKeyHeld(rightKey))    inputDir += transform.right;
            if (IsKeyHeld(leftKey))     inputDir -= transform.right;

            Vector3 upVec = worldUpForQe ? Vector3.up : transform.up;
            if (IsKeyHeld(upKey))   inputDir += upVec;
            if (IsKeyHeld(downKey)) inputDir -= upVec;
        }

        // Speed scales smoothly with zoom distance (no hard floor/step).
        float speedScale = Mathf.Max(distance, minDistance) / Mathf.Max(minDistance, 0.01f);
        float targetSpeed = flySpeed * speedScale;
        if (IsSprintHeld()) targetSpeed *= flySprintMultiplier;

        // Target velocity: normalized direction * target speed.
        // If no key held, target = zero → damping brings us to a smooth stop.
        Vector3 targetVelocity = inputDir.sqrMagnitude > 0.0001f
            ? inputDir.normalized * targetSpeed
            : Vector3.zero;

        // Smooth toward target velocity. Different rates for accelerate vs. release
        // means "pressing feels responsive, releasing glides".
        float rate = inputDir.sqrMagnitude > 0.0001f ? flyAcceleration : flyDamping;
        flyVelocity = Vector3.Lerp(flyVelocity, targetVelocity, 1f - Mathf.Exp(-rate * Time.deltaTime));

        // Apply to focus point. Small residual velocity is harmless — it just glides to a stop.
        if (flyVelocity.sqrMagnitude > 1e-6f)
            focusPoint += flyVelocity * Time.deltaTime;
    }

    // ------------------------------------------------------------------ camera update

    private void UpdateFocus()
    {
        Vector3 desired = focusPoint; // nothing selected: stay put
        if (isModelSelected && selectedModel != null)
            desired = selectedModel.transform.position + focusOffset;

        // When we're flying, don't let SmoothDamp fight the fly velocity:
        // if a model isn't selected, we already moved focusPoint this frame.
        if (isModelSelected && selectedModel != null)
            focusPoint = Vector3.SmoothDamp(focusPoint, desired, ref focusVelocity, followSmoothTime);
        else
            focusVelocity = Vector3.zero; // no leftover follow momentum
    }

    private void ApplyCamera()
    {
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, rotationSmoothTime);
        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime);
        distance = Mathf.SmoothDamp(distance, targetDistance, ref distanceVelocity, zoomSmoothTime);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = focusPoint - rot * Vector3.forward * distance;
        transform.SetPositionAndRotation(position, rot);
    }

    private static float NormalizeAngle(float degrees)
    {
        return degrees > 180f ? degrees - 360f : degrees;
    }

    // ------------------------------------------------------------------ input helpers

    private bool IsOrbitHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null && mouse.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    private bool IsSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    private bool IsKeyHeld(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var control = MapKey(key);
        return control != null && control.isPressed;
#else
        return Input.GetKey(key);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static UnityEngine.InputSystem.Controls.KeyControl MapKey(KeyCode key)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return null;
        switch (key)
        {
            case KeyCode.W: return kb.wKey;
            case KeyCode.A: return kb.aKey;
            case KeyCode.S: return kb.sKey;
            case KeyCode.D: return kb.dKey;
            case KeyCode.Q: return kb.qKey;
            case KeyCode.E: return kb.eKey;
            case KeyCode.R: return kb.rKey;
            case KeyCode.F: return kb.fKey;
            case KeyCode.Space: return kb.spaceKey;
            default: return null;
        }
    }
#endif

    private Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
#else
        Vector2 position = Input.mousePosition;
        Vector2 delta = position - lastMousePosition;
        lastMousePosition = position;
        return delta;
#endif
    }

    private float GetScrollNotches()
    {
#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return 0f;
        float y = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(y) >= 10f) y /= 120f;
        return y;
#else
        return Input.mouseScrollDelta.y;
#endif
    }
}