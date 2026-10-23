#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class LightningStaffEventProbe : MonoBehaviour
{
    public int fires, ends;
    public Vector3 firePosition;
    public void FireMagic()
    {
        fires++;
        firePosition = GetComponentsInChildren<Transform>().Single(t => t.name == "FirePoint").position;
    }
    public void EndAttack() { ends++; }
}

// Editor-only verification. Nothing from this harness is serialized into the player or scenes.
public sealed class LightningStaffPlayProbe : MonoBehaviour
{
    public static Action<Camera, string> CaptureFrame;
    const string Folder = "Docs/LightningStaff/";
    readonly StringBuilder report = new StringBuilder();
    readonly HashSet<int> bolts = new HashSet<int>();
    Keyboard keyboard;
    Mouse mouse;
    InputSettings oldInputSettings, temporaryInputSettings;
    int oldCaptureRate;
    bool oldBackground;
    new Camera camera;
    GameObject player;
    LightningStaffEventProbe events;
    bool captureBolt;
    int shot;
    bool failed;
    Vector3 gripLocal, fireLocal;
    Transform model, fire;
    float maxGripError;

    void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        report.AppendLine("RUNTIME ERROR " + message);
        failed = true;
    }
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory("Logs/LightningStaff");
        Application.logMessageReceived += Log;
        oldCaptureRate = Time.captureFramerate; Time.captureFramerate = 60;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        oldInputSettings = InputSystem.settings;
        temporaryInputSettings = Instantiate(oldInputSettings);
        InputSystem.settings = temporaryInputSettings;
        temporaryInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporaryInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.gameObject.AddComponent<AudioListener>();
        camera.transform.position = new Vector3(0, 5, -5); camera.transform.LookAt(Vector3.zero);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.13f, 0.15f, 0.19f);
        camera.nearClipPlane = 0.01f;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var volume = new GameObject("Existing PlayerTest Bloom").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Scenes/PlayerTest/Global Volume Profile.asset");
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);
        var light = new GameObject("Key Light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(45, -35, 0);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 10;
        var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMaterial.SetColor("_BaseColor", new Color(0.16f, 0.18f, 0.21f)); groundMaterial.SetFloat("_Smoothness", 0.1f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        player = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Player.prefab"));
        player.transform.SetPositionAndRotation(new Vector3(0, 0.05f, 0), Quaternion.identity);
        player.GetComponent<PlayerAim>().enabled = false;
        var animator = player.transform.Find("Wizard").GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        events = animator.gameObject.AddComponent<LightningStaffEventProbe>();
        var attack = player.GetComponent<PlayerAttack>();
        fire = player.GetComponentsInChildren<Transform>().Single(t => t.name == "FirePoint");
        model = fire.parent.Find("Model");
        fireLocal = model.InverseTransformPoint(fire.position);
        gripLocal = model.InverseTransformPoint(Grip());
        for (int i = 0; i < 30; i++) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Idle state reached");
        Capture("Idle", false);
        foreach (var key in new[] { Key.W, Key.S, Key.A, Key.D })
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            Vector3 start = player.transform.position;
            for (int i = 0; i < 24; i++) yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"), "Walk state " + key);
            Check(Vector3.Distance(start, player.transform.position) > 0.5f, "Actual movement " + key);
            if (key == Key.W) Capture("Walk", false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int i = 0; i < 16; i++) yield return null;
        }
        for (shot = 0; shot < 2; shot++)
        {
            player.transform.rotation = Quaternion.Euler(0, shot * 90, 0);
            int before = bolts.Count;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            for (int i = 0; i < (shot == 0 ? 30 : 120); i++) yield return null;
            Check(!attack.IsAttacking, "Charging waits for release " + shot);
            captureBolt = true;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            for (int i = 0; i < 70; i++) yield return null;
            Check(events.fires == shot + 1 && events.ends == shot + 1, "One FireMagic/EndAttack event " + shot);
            Check(bolts.Count == before + 1, "One actual LightningBolt " + shot);
            Check(!attack.IsAttacking, "EndAttack clears attack flag " + shot);
        }
        Check(maxGripError < 0.025f, "Maximum hand/shaft grip deviation = " + maxGripError.ToString("F6") + " m");
        File.WriteAllText("Logs/LightningStaff/play-results.txt", report + (failed ? "PLAY_VALIDATION_FAILED\n" : "PLAY_VALIDATION_OK\n"));
        EditorApplication.ExitPlaymode();
    }
    Vector3 Grip()
    {
        var names = new[] { "RightIndexProximal", "RightIndexDistal", "RightMiddleProximal", "RightMiddleDistal", "RightRingProximal", "RightRingDistal", "RightLittleProximal", "RightLittleDistal" };
        Vector3 p = Vector3.zero;
        var bones = player.GetComponentsInChildren<Transform>();
        foreach (var name in names) p += bones.Single(t => t.name == name).position;
        return p / names.Length;
    }
    void LateUpdate()
    {
        if (player == null || model == null) return;
        maxGripError = Mathf.Max(maxGripError, Vector3.Distance(Grip(), model.TransformPoint(gripLocal)));
        foreach (var bolt in FindObjectsByType<LightningBolt>(FindObjectsSortMode.None))
        {
            if (!bolts.Add(bolt.GetInstanceID())) continue;
            Check(Vector3.Dot(bolt.transform.forward, player.transform.forward) > 0.9999f, "Bolt follows horizontal player aim " + shot);
            Check(Mathf.Abs(bolt.transform.forward.y) < 0.0001f, "Bolt remains horizontal " + shot);
            Check(Vector3.Distance(bolt.transform.position, events.firePosition) < 0.001f, "Bolt originates at animated FirePoint " + shot);
            Check(bolt.transform.parent == null, "Bolt detaches from animated hand " + shot);
            Check(Vector3.Distance(fire.position, model.TransformPoint(fireLocal)) < 0.0001f, "FirePoint remains attached to crystal " + shot);
            report.AppendLine($"SHOT {shot} origin={bolt.transform.position:F6}, direction={bolt.transform.forward:F6}, grip={Grip():F6}");
            if (captureBolt) { captureBolt = false; StartCoroutine(CaptureAttack()); }
        }
    }
    IEnumerator CaptureAttack()
    {
        yield return null; // Allow the real LightningEffect coroutine to build its mesh.
        Capture("Attack-" + shot, true);
    }
    void Capture(string name, bool attack)
    {
        var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
        camera.orthographic = true;
        Vector3 target = player.transform.position + Vector3.up;
        if (attack) target += player.transform.forward * (shot == 0 ? 2.4f : 4.5f);
        camera.transform.position = target + player.transform.rotation * new Vector3(6, 3.5f, 7);
        camera.transform.LookAt(target);
        camera.orthographicSize = attack ? (shot == 0 ? 3f : 5f) : 1.3f;
        CaptureFrame(camera, Folder + name + "-play.png");
        if (!attack)
        {
            camera.transform.position = Grip() + player.transform.rotation * new Vector3(1.3f, 0.8f, 1.8f);
            camera.transform.LookAt(Grip()); camera.orthographicSize = 0.3f;
            CaptureFrame(camera, Folder + name + "-grip-play.png");
        }
        camera.orthographic = false;
        camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
    }
    void Check(bool value, string message)
    {
        report.AppendLine((value ? "PASS " : "FAIL ") + message);
        if (!value) failed = true;
    }
    void OnDestroy()
    {
        Application.logMessageReceived -= Log;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (oldInputSettings != null) InputSystem.settings = oldInputSettings;
        if (temporaryInputSettings != null) DestroyImmediate(temporaryInputSettings);
        Time.captureFramerate = oldCaptureRate;
        Application.runInBackground = oldBackground;
    }
}
#endif
