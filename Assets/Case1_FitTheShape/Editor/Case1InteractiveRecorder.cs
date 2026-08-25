using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Case1;

/// <summary>
/// Records a full interactive gameplay session for Case 1 demonstrating:
/// 1. Tapping an unmatched front-row piece (Green Square) -> in-place wobble rejection without moving.
/// 2. Tapping a matched front-row piece (Red Hexagon) -> dynamic arc flight into live slot, shockwave/sparkle, column 2 reflow.
/// 3. Tapping a matched front-row piece (Orange Diamond) -> flight into live slot, ripple reaction, column 0 reflow.
/// 4. Tapping a newly advanced piece in Column 2 (Green Square) -> in-place shake rejection (no square in live row).
/// 5. Tapping a newly advanced piece in Column 0 (Purple Triangle) -> dynamic arc flight into live triangle slot & reflow!
/// 
/// Captures 45 fps high-res frames to /tmp/c1_interactive_frames and encodes MP4 video.
/// </summary>
[InitializeOnLoad]
public static class Case1InteractiveRecorder
{
    const string KeyActive = "Case1InteractiveRecorder.Active";
    const int FrameWidth = 1080;
    const int FrameHeight = 1728;
    const string OutDir = "/tmp/c1_interactive_frames";

    static bool _hooked;
    static bool _sessionInit;
    static int _frameIndex;
    static double _startTime;
    static int _actionStage;
    static double _actionTimer;
    static RenderTexture _rt;
    static Texture2D _tex;

    static Case1InteractiveRecorder()
    {
        if (SessionState.GetInt(KeyActive, 0) == 1) Hook();
    }

    public static void Record()
    {
        Case1SceneSetup.Build();
        if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
        Directory.CreateDirectory(OutDir);

        SessionState.SetInt(KeyActive, 1);
        _sessionInit = false;
        Hook();
        Debug.Log("[Case1Recorder] RECORD_START entering play mode");
        EditorApplication.EnterPlaymode();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.update += Drive;
    }

    static void Drive()
    {
        if (SessionState.GetInt(KeyActive, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;

        Case1Director director = UnityEngine.Object.FindFirstObjectByType<Case1Director>(FindObjectsInactive.Include);
        ShapeTapInput tapInput = UnityEngine.Object.FindFirstObjectByType<ShapeTapInput>(FindObjectsInactive.Include);
        DeckReflow deck = UnityEngine.Object.FindFirstObjectByType<DeckReflow>(FindObjectsInactive.Include);
        DrumSlotReaction drum = UnityEngine.Object.FindFirstObjectByType<DrumSlotReaction>(FindObjectsInactive.Include);
        Camera cam = Camera.main;

        if (director == null || deck == null || drum == null || cam == null)
        {
            Debug.LogError("[Case1Recorder] Missing components!");
            Finish(1);
            return;
        }

        if (!_sessionInit)
        {
            _sessionInit = true;
            _frameIndex = 0;
            _actionStage = 0;
            _actionTimer = 0.0;
            _startTime = EditorApplication.timeSinceStartup;
            _rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32);
            _tex = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGB24, false);
            director.AllowPlayWithoutInput();
            Time.captureFramerate = 45;
            Debug.Log("[Case1Recorder] PlayMode session initialized at fixed 45 fps");
        }

        double elapsed = EditorApplication.timeSinceStartup - _startTime;

        // Stage state machine
        switch (_actionStage)
        {
            case 0: // Wait for warm-up
                if (director.Ready || elapsed > 1.2)
                {
                    Debug.Log("[Case1Recorder] Scene ready, starting idle lead-in");
                    _actionStage = 1;
                    _actionTimer = 0;
                }
                break;

            case 1: // Idle lead-in (0.5s = ~22 frames)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 0.5)
                {
                    // Action 1: Tap Square (Slot 1) -> Expect in-place rejection wobble
                    Transform square = FindShapeInSlot(deck, 1);
                    Debug.Log("[Case1Recorder] ACTION 1: Tapping Square in Slot 1 (no live match -> expect shake)");
                    if (square != null) director.HandlePieceTap(square);
                    _actionStage = 2;
                    _actionTimer = 0;
                }
                break;

            case 2: // Wait for shake (0.6s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 0.6)
                {
                    // Action 2: Tap Hexagon (Slot 2) -> Expect match to live slot 3 & flight & column 2 reflow
                    Transform hex = FindShapeInSlot(deck, 2);
                    Debug.Log("[Case1Recorder] ACTION 2: Tapping Hexagon in Slot 2 (matches live Hexagon -> flight + reflow)");
                    if (hex != null) director.HandlePieceTap(hex);
                    _actionStage = 3;
                    _actionTimer = 0;
                }
                break;

            case 3: // Wait for Hexagon flight & ripple & reflow (1.3s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 1.3 && !director.IsPlaying)
                {
                    // Action 3: Tap Diamond (Slot 0) -> Expect match to live slot 0 & flight & column 0 reflow
                    Transform diamond = FindShapeInSlot(deck, 0);
                    Debug.Log("[Case1Recorder] ACTION 3: Tapping Diamond in Slot 0 (matches live Diamond -> flight + reflow)");
                    if (diamond != null) director.HandlePieceTap(diamond);
                    _actionStage = 4;
                    _actionTimer = 0;
                }
                break;

            case 4: // Wait for Diamond flight & ripple & reflow (1.3s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 1.3 && !director.IsPlaying)
                {
                    // Action 4: Tap newly arrived piece in Column 2 (Slot 2) - which is Green Square! Expect rejection shake!
                    Transform squareCol2 = FindShapeInSlot(deck, 2);
                    Debug.Log("[Case1Recorder] ACTION 4: Tapping newly advanced Square in Slot 2 -> Expect in-place shake: " + (squareCol2 != null ? squareCol2.name : "null"));
                    if (squareCol2 != null) director.HandlePieceTap(squareCol2);
                    _actionStage = 5;
                    _actionTimer = 0;
                }
                break;

            case 5: // Wait for shake (0.6s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 0.6)
                {
                    // Action 5: Tap newly arrived Triangle in Column 0 (Slot 0)! Expect flight into live Triangle slot!
                    Transform triangle = FindShapeInSlot(deck, 0);
                    Debug.Log("[Case1Recorder] ACTION 5: Tapping newly advanced Triangle in Slot 0 (matches live Triangle -> flight + reflow): " + (triangle != null ? triangle.name : "null"));
                    if (triangle != null) director.HandlePieceTap(triangle);
                    _actionStage = 6;
                    _actionTimer = 0;
                }
                break;

            case 6: // Wait for Triangle flight & ripple & reflow (1.3s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 1.3 && !director.IsPlaying)
                {
                    // Action 6: Tap newly arrived Hexagon in Column 0 (Slot 0)! Hexagon is already filled -> Expect in-place shake!
                    Transform hexCol0 = FindShapeInSlot(deck, 0);
                    Debug.Log("[Case1Recorder] ACTION 6: Tapping Hexagon in Slot 0 (Live Hexagon slot already filled -> Expect in-place shake): " + (hexCol0 != null ? hexCol0.name : "null"));
                    if (hexCol0 != null) director.HandlePieceTap(hexCol0);
                    _actionStage = 7;
                    _actionTimer = 0;
                }
                break;

            case 7: // Settle tail (1.2s)
                _actionTimer += 1.0 / 45.0;
                if (_actionTimer >= 1.2 && !director.IsPlaying)
                {
                    Debug.Log("[Case1Recorder] All actions complete! Finishing recording.");
                    Finish(0);
                    return;
                }
                break;
        }

        // Capture current frame
        if (_actionStage >= 1)
        {
            CaptureCurrentFrame(cam);
        }
    }

    static Transform FindShapeInSlot(DeckReflow deck, int slot)
    {
        if (deck == null || deck.entries == null) return null;
        for (int i = 0; i < deck.entries.Length; i++)
        {
            DeckReflow.Entry e = deck.entries[i];
            if (e != null && !e.Gone && e.slot == slot && e.shape != null && e.shape.gameObject.activeInHierarchy)
            {
                return e.shape;
            }
        }
        return null;
    }

    static void CaptureCurrentFrame(Camera cam)
    {
        if (cam == null || _rt == null || _tex == null) return;

        RenderTexture prevTarget = cam.targetTexture;
        cam.targetTexture = _rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
        _tex.Apply(false);
        RenderTexture.active = prevActive;

        string path = Path.Combine(OutDir, string.Format("frame_{0:D4}.png", _frameIndex++));
        File.WriteAllBytes(path, _tex.EncodeToPNG());
    }

    static void Finish(int exitCode)
    {
        Time.captureFramerate = 0;
        SessionState.SetInt(KeyActive, 0);
        EditorApplication.update -= Drive;
        _hooked = false;

        if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
        if (_rt != null) UnityEngine.Object.DestroyImmediate(_rt);

        Debug.Log(string.Format("[Case1Recorder] RECORDING_COMPLETE frames={0} exitCode={1}", _frameIndex, exitCode));

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
