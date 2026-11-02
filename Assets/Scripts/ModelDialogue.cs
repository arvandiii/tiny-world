using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Speech-bubble dialogue for a Model (or any character), meant for recording scenarios.
///
/// When Space is pressed, every character with hasDialogue enabled starts its next line:
///   1. The cloud shows animated typing dots ( .  ..  ... ) for the line's waitSeconds.
///   2. Then the line's text appears in the cloud (typewriter effect, or instantly).
///   3. The text stays for visibleSeconds, then the cloud hides (0 = stay until the next line).
///
/// When the Model is selected, it smoothly rotates on the Y axis to face the camera,
/// so a capsule never tips over. Selection is driven by SetSelected(bool), which
/// CameraController.SelectModel / Deselect call.
///
/// While selected, the Model is also FROZEN in place: its position is snapshotted on
/// selection and pinned every LateUpdate (plus Rigidbody constraints if one exists).
/// When deselected, it stays exactly where it was frozen — no position restore.
///
/// Scene setup: a world-space Canvas (the cloud) as a child of the character, containing
/// a background Image and a TextMeshPro text. See the setup steps for the exact layout.
/// </summary>
public class ModelDialogue : MonoBehaviour
{
    [Serializable]
    public class DialogueLine
    {
        [Tooltip("Seconds the cloud shows typing dots before the text appears.")]
        public float waitSeconds = 2f;

        [TextArea(2, 4)]
        public string text = "Hello!";

        [Tooltip("Seconds the text stays after it is fully shown. 0 = stays until the next line starts.")]
        public float visibleSeconds = 4f;
    }

    [Header("Dialogue")]
    [Tooltip("Turn off for characters that should stay silent.")]
    public bool hasDialogue = true;
    [SerializeField] private List<DialogueLine> lines = new List<DialogueLine> { new DialogueLine() };
    [Tooltip("After the last line, start over from the first.")]
    [SerializeField] private bool loopLines = false;

    [Header("Bubble (assign from the scene)")]
    [Tooltip("The world-space Canvas object that is the cloud. It is shown/hidden by this script.")]
    [SerializeField] private GameObject bubbleRoot;
    [SerializeField] private TMP_Text bubbleText;

    [Header("Typing")]
    [Tooltip("Seconds between dot changes while waiting ( . -> .. -> ... ).")]
    [SerializeField] private float dotInterval = 0.4f;
    [Tooltip("Typewriter speed for the text. 0 = show the whole text instantly.")]
    [SerializeField] private float typewriterCharsPerSecond = 30f;

    [Header("Face Camera When Selected")]
    [Tooltip("While selected, rotate on Y only to face the camera. Capsule-safe (never tips over).")]
    [SerializeField] private bool faceCameraWhenSelected = true;
    [Tooltip("SmoothDamp time for the Y rotation. Lower = snappier, higher = lazier.")]
    [SerializeField] private float faceCameraSmoothTime = 0.2f;
    [Tooltip("Extra Y rotation applied on top of facing the camera (degrees). Useful if the model's forward axis is offset.")]
    [SerializeField] private float faceCameraYawOffset = 0f;

    [Header("Freeze While Selected")]
    [Tooltip("While selected, the Model's position is pinned so it cannot drift or be pushed.")]
    [SerializeField] private bool freezePositionWhileSelected = true;
    [Tooltip("Freeze the transform you rotate to face the camera. Set this if the capsule is a child of the ModelDialogue object.")]
    [SerializeField] private Transform faceTarget;

    private int nextLineIndex;
    private bool busy;                 // true while dots/typing are in progress
    private Coroutine currentRoutine;
    private Camera cam;

    // ---- selection + facing (new) ----
    // NOTE: no original rotation is stored. When the model is deselected, it simply
    // stops facing the camera and stays where it is. That's intentional — the model
    // moves freely, so "restoring" a stale rotation would be wrong anyway.
    private bool isSelected;
    private float faceVelocity;        // SmoothDampAngular velocity for the Y-facing rotation

    // ---- freeze-while-selected ----
    // Snapshotted on SetSelected(true), applied every LateUpdate, released on
    // SetSelected(false) with NO restore (the model just stays put).
    private Vector3 frozenPosition;
    private bool hasFrozenPosition;
    private Rigidbody rb;
    private RigidbodyConstraints savedConstraints;
    private bool hasSavedConstraints;

    /// <summary>True if this character is allowed to speak and still has a line to say.</summary>
    public bool CanSpeak =>
        hasDialogue && lines != null && lines.Count > 0 && (loopLines || nextLineIndex < lines.Count);

    /// <summary>True while the cloud is showing dots or typing out a line.</summary>
    public bool IsBusy => busy;

    /// <summary>True while this Model is the selected one and (if enabled) facing the camera.</summary>
    public bool IsSelected => isSelected;

    /// <summary>Transform that gets rotated to face the camera. Defaults to this object.</summary>
    private Transform FaceTarget => faceTarget != null ? faceTarget : transform;

    // ------------------------------------------------------------------ lifecycle

    private void Start()
    {
        if (bubbleRoot == null || bubbleText == null)
        {
            Debug.LogError("[ModelDialogue] Assign Bubble Root and Bubble Text in the Inspector.", this);
            enabled = false;
            return;
        }

        cam = Camera.main;
        rb = GetComponent<Rigidbody>();
        HideBubble();
    }

    private void Update()
    {
        if (SpacePressedThisFrame()) TryStartNextLine();
    }

    // The cloud always faces the camera so the text stays readable from a top-down view.
    private void LateUpdate()
    {
        if (bubbleRoot.activeSelf)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null) bubbleRoot.transform.rotation = cam.transform.rotation;
        }

        // Freeze the model's position while selected. Runs in LateUpdate so it wins
        // against any movement done in Update / FixedUpdate that frame.
        if (isSelected && freezePositionWhileSelected && hasFrozenPosition)
            transform.position = frozenPosition;

        // Face the camera on Y only while selected. Runs after dialogue logic so it
        // never interferes with speech timing, and after the bubble billboard so
        // the bubble stays screen-aligned regardless of the model's rotation.
        if (isSelected && faceCameraWhenSelected)
            FaceCameraOnY();
    }

    private void OnDisable()
    {
        busy = false;
        currentRoutine = null;
        if (bubbleRoot != null) bubbleRoot.SetActive(false);

        // If we're disabled mid-selection, release the freeze so the object can move again.
        if (isSelected) SetSelected(false);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Starts the next line. Returns false if silent, out of lines, or still typing.</summary>
    public bool TryStartNextLine()
    {
        if (!CanSpeak || busy) return false;

        if (nextLineIndex >= lines.Count) nextLineIndex = 0; // looping

        // If the previous line is only waiting to auto-hide, cut that short.
        if (currentRoutine != null) StopCoroutine(currentRoutine);

        DialogueLine line = lines[nextLineIndex++];
        currentRoutine = StartCoroutine(PlayLine(line));
        return true;
    }

    /// <summary>
    /// Called by CameraController.SelectModel / Deselect to drive the "face camera" behavior
    /// and the position freeze. Selection is per-instance: only this GameObject is affected.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (isSelected == selected) return;
        isSelected = selected;

        // Reset the smoothing velocity on transitions so we never carry momentum across
        // selection changes (which could cause a brief overshoot / jitter).
        faceVelocity = 0f;

        if (selected)
            BeginFreeze();
        else
            EndFreeze();
    }

    [ContextMenu("Reset Dialogue")]
    public void ResetDialogue()
    {
        StopAllCoroutines();
        currentRoutine = null;
        nextLineIndex = 0;
        busy = false;
        if (bubbleRoot != null) HideBubble();
    }

    // ------------------------------------------------------------------ freeze helpers

    private void BeginFreeze()
    {
        if (!freezePositionWhileSelected) return;

        frozenPosition = transform.position;
        hasFrozenPosition = true;

        // Rigidbody (if any): stop physics from moving us. Constraints are restored on deselect,
        // so we don't permanently alter the object's setup.
        if (rb != null)
        {
            savedConstraints = rb.constraints;
            hasSavedConstraints = true;
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void EndFreeze()
    {
        hasFrozenPosition = false;

        if (rb != null && hasSavedConstraints)
        {
            rb.constraints = savedConstraints;
            hasSavedConstraints = false;
        }
    }

    // ------------------------------------------------------------------ facing the camera

    /// <summary>
    /// Rotates the face target around Y only, toward the camera's horizontal position.
    /// Using Y-only means the capsule stays upright no matter where the camera is.
    /// Only affects the transform this component is attached to (or faceTarget).
    /// </summary>
    private void FaceCameraOnY()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Transform target = FaceTarget;
        if (target == null) return;

        Vector3 toCamera = cam.transform.position - target.position;
        toCamera.y = 0f; // Y-only: ignore the height difference so we don't tip.

        // Dead-zone: if the camera is directly above/below us (or coincides),
        // toCamera is near-zero and atan2 is noisy. Skip this frame instead.
        if (toCamera.sqrMagnitude < 0.0001f) return;

        float targetYaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg + faceCameraYawOffset;

        float currentYaw = target.eulerAngles.y;
        float smoothedYaw = Mathf.SmoothDampAngle(
            currentYaw, targetYaw, ref faceVelocity, Mathf.Max(0.001f, faceCameraSmoothTime));

        // Preserve any non-Y rotation that other systems might have set.
        Vector3 euler = target.eulerAngles;
        euler.y = smoothedYaw;
        target.eulerAngles = euler;
    }

    // ------------------------------------------------------------------ the line sequence

    private IEnumerator PlayLine(DialogueLine line)
    {
        busy = true;
        bubbleRoot.SetActive(true);
        bubbleText.maxVisibleCharacters = 99999;

        // 1. Waiting: animated typing dots.
        float elapsed = 0f;
        int dots = 0;
        while (elapsed < line.waitSeconds)
        {
            dots = dots % 3 + 1;
            bubbleText.text = new string('.', dots);

            float step = Mathf.Min(Mathf.Max(0.05f, dotInterval), line.waitSeconds - elapsed);
            yield return new WaitForSeconds(step);
            elapsed += step;
        }

        // 2. Show the text, typewriter style (or instantly).
        bubbleText.text = line.text;

        if (typewriterCharsPerSecond > 0f)
        {
            bubbleText.ForceMeshUpdate();
            int total = bubbleText.textInfo.characterCount;
            float delay = 1f / typewriterCharsPerSecond;

            for (int i = 0; i <= total; i++)
            {
                bubbleText.maxVisibleCharacters = i;
                yield return new WaitForSeconds(delay);
            }
            bubbleText.maxVisibleCharacters = 99999;
        }

        busy = false; // the next line may start from now on

        // 3. Keep the text on screen, then hide (0 = stay until the next line).
        if (line.visibleSeconds > 0f)
        {
            yield return new WaitForSeconds(line.visibleSeconds);
            HideBubble();
        }

        currentRoutine = null;
    }

    private void HideBubble()
    {
        bubbleRoot.SetActive(false);
    }

    // ------------------------------------------------------------------ input

    // Works with the new Input System, the old Input Manager, or both enabled.
    private static bool SpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
}