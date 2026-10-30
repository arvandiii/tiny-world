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

    private int nextLineIndex;
    private bool busy;                 // true while dots/typing are in progress
    private Coroutine currentRoutine;
    private Camera cam;

    /// <summary>True if this character is allowed to speak and still has a line to say.</summary>
    public bool CanSpeak =>
        hasDialogue && lines != null && lines.Count > 0 && (loopLines || nextLineIndex < lines.Count);

    /// <summary>True while the cloud is showing dots or typing out a line.</summary>
    public bool IsBusy => busy;

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
        HideBubble();
    }

    private void Update()
    {
        if (SpacePressedThisFrame()) TryStartNextLine();
    }

    // The cloud always faces the camera so the text stays readable from a top-down view.
    private void LateUpdate()
    {
        if (!bubbleRoot.activeSelf) return;
        if (cam == null) cam = Camera.main;
        if (cam != null) bubbleRoot.transform.rotation = cam.transform.rotation;
    }

    private void OnDisable()
    {
        busy = false;
        currentRoutine = null;
        if (bubbleRoot != null) bubbleRoot.SetActive(false);
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

    [ContextMenu("Reset Dialogue")]
    public void ResetDialogue()
    {
        StopAllCoroutines();
        currentRoutine = null;
        nextLineIndex = 0;
        busy = false;
        if (bubbleRoot != null) HideBubble();
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
