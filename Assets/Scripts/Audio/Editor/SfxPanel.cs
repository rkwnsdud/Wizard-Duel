using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 효과음을 하나씩 들으면서 크기·속도·음 높이를 맞추는 창.
// 값은 손을 떼는 대로 SfxLibrary 에셋 파일에 저장된다. 게임은 소리를 낼 때마다 그 에셋을 읽으므로 플레이 중에 고쳐도 다음 소리부터 들린다.
public sealed class SfxPanel : EditorWindow
{
    // 연달아 들을 때 소리 사이에 두는 간격(초).
    const float Gap = .3f;
    const float NameWidth = 120f, ClipWidth = 190f, SliderWidth = 150f, PlayWidth = 28f, PickWidth = 18f;

    static readonly SfxId[] Ids = (SfxId[])Enum.GetValues(typeof(SfxId));

    SerializedObject serialized;
    AudioSource preview;
    AudioListener ownListener;
    Vector2 scroll;
    readonly HashSet<SfxId> picked = new HashSet<SfxId>();
    readonly Queue<SfxId> queue = new Queue<SfxId>();
    double nextAt;
    SfxId current;
    bool wasPlaying;

    [MenuItem("Tools/Wizard Duel/효과음 패널")]
    static void Open() => GetWindow<SfxPanel>("효과음 패널");

    void OnEnable()
    {
        minSize = new Vector2(1020f, 260f);
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (preview != null) DestroyImmediate(preview.gameObject);
    }

    bool Playing => preview != null && preview.isPlaying;

    void OnGUI()
    {
        SfxLibrary library = Sfx.Library;
        if (library == null)
        {
            EditorGUILayout.HelpBox("Assets/Resources/SfxLibrary.asset이 없습니다.", MessageType.Error);
            return;
        }
        if (serialized == null || serialized.targetObject != library) serialized = new SerializedObject(library);
        serialized.Update();
        SerializedProperty entries = serialized.FindProperty("entries");

        SfxId? play = null;
        bool playPicked = false, playAll = false;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            playPicked = GUILayout.Button("선택한 소리 연달아 듣기", EditorStyles.toolbarButton);
            playAll = GUILayout.Button("전부 연달아 듣기", EditorStyles.toolbarButton);
            if (GUILayout.Button("정지", EditorStyles.toolbarButton)) StopPreview();
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.HelpBox("값은 바로 저장되고, 플레이 중인 게임에도 다음 소리부터 반영됩니다.", MessageType.None);
        if (EditorUtility.audioMasterMute)
            EditorGUILayout.HelpBox("Game 창의 Mute Audio가 켜져 있어 소리가 나지 않습니다.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(PickWidth + 4f);
            GUILayout.Label("소리", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
            GUILayout.Label("클립", EditorStyles.miniBoldLabel, GUILayout.Width(ClipWidth));
            GUILayout.Label("크기", EditorStyles.miniBoldLabel, GUILayout.Width(SliderWidth));
            GUILayout.Label("속도", EditorStyles.miniBoldLabel, GUILayout.Width(SliderWidth));
            GUILayout.Label("음 높이", EditorStyles.miniBoldLabel, GUILayout.Width(SliderWidth));
            GUILayout.Label("변주(±)", EditorStyles.miniBoldLabel, GUILayout.Width(SliderWidth));
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (SfxId id in Ids)
        {
            SerializedProperty entry = EntryFor(entries, id);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool on = EditorGUILayout.Toggle(picked.Contains(id), GUILayout.Width(PickWidth));
                if (on) picked.Add(id); else picked.Remove(id);
                GUIStyle style = Playing && current == id ? EditorStyles.boldLabel : EditorStyles.label;
                GUILayout.Label(Label(id), style, GUILayout.Width(NameWidth));
                Field(entry, "clip", ClipWidth);
                Field(entry, "volume", SliderWidth);
                Field(entry, "speed", SliderWidth);
                Field(entry, "pitch", SliderWidth);
                Field(entry, "pitchVariation", SliderWidth);
                if (GUILayout.Button("▶", GUILayout.Width(PlayWidth))) play = id;
            }
        }
        EditorGUILayout.EndScrollView();
        serialized.ApplyModifiedProperties();

        // 고친 값이 에셋에 들어간 뒤에 재생한다.
        if (play.HasValue) PlaySequence(new[] { play.Value });
        else if (playAll) PlaySequence(Ids);
        else if (playPicked) PlaySequence(Array.FindAll(Ids, picked.Contains));
    }

    static void Field(SerializedProperty entry, string name, float width) =>
        EditorGUILayout.PropertyField(entry.FindPropertyRelative(name), GUIContent.none, GUILayout.Width(width));

    // SfxId에 새 소리를 추가하면 표에 빈 줄이 생긴다. 클립만 끌어다 놓으면 된다.
    static SerializedProperty EntryFor(SerializedProperty entries, SfxId id)
    {
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty existing = entries.GetArrayElementAtIndex(i);
            if (existing.FindPropertyRelative("id").intValue == (int)id) return existing;
        }
        entries.arraySize++;
        SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
        added.FindPropertyRelative("id").intValue = (int)id;
        added.FindPropertyRelative("clip").objectReferenceValue = null;
        added.FindPropertyRelative("volume").floatValue = 1f;
        added.FindPropertyRelative("speed").floatValue = 1f;
        added.FindPropertyRelative("pitch").floatValue = 1f;
        added.FindPropertyRelative("pitchVariation").floatValue = 0f;
        return added;
    }

    static string Label(SfxId id)
    {
        FieldInfo field = typeof(SfxId).GetField(id.ToString());
        InspectorNameAttribute name = field != null ? field.GetCustomAttribute<InspectorNameAttribute>() : null;
        return name != null ? name.displayName : id.ToString();
    }

    void PlaySequence(IEnumerable<SfxId> ids)
    {
        queue.Clear();
        foreach (SfxId id in ids) queue.Enqueue(id);
        nextAt = 0;
    }

    // 열린 씬에 AudioListener가 없으면 미리 듣기가 무음이 된다. 그때만 숨긴 리스너를 붙이고, 씬에 리스너가 있으면 뗀다.
    // 숨긴 오브젝트는 FindFirstObjectByType에 잡히지 않으므로 붙인 리스너는 따로 기억한다.
    void EnsureListener()
    {
        bool sceneHasListener = FindFirstObjectByType<AudioListener>() != null;
        if (sceneHasListener && ownListener != null) DestroyImmediate(ownListener);
        else if (!sceneHasListener && ownListener == null) ownListener = preview.gameObject.AddComponent<AudioListener>();
    }

    void StopPreview()
    {
        queue.Clear();
        if (preview != null) preview.Stop();
    }

    void Tick()
    {
        // 고친 값은 슬라이더에서 손을 뗐을 때 파일로 쓴다. 끄는 동안 매번 쓰지 않는다.
        SfxLibrary library = Sfx.Library;
        if (library != null && GUIUtility.hotControl == 0 && EditorUtility.IsDirty(library)) AssetDatabase.SaveAssetIfDirty(library);
        if (queue.Count > 0 && EditorApplication.timeSinceStartup >= nextAt)
        {
            current = queue.Dequeue();
            SfxLibrary.Entry entry = library != null ? library.Find(current) : null;
            if (entry != null && entry.clip != null)
            {
                if (preview == null)
                    preview = EditorUtility.CreateGameObjectWithHideFlags("Sfx preview", HideFlags.HideAndDontSave,
                        typeof(AudioSource)).GetComponent<AudioSource>();
                preview.Stop();
                EnsureListener();
                Sfx.Apply(preview, entry);
                preview.Play();
                nextAt = EditorApplication.timeSinceStartup + preview.clip.length / preview.pitch + Gap;
            }
        }
        // 재생 중인 줄의 굵은 글씨를 켜고 끈다.
        if (wasPlaying != Playing)
        {
            wasPlaying = Playing;
            Repaint();
        }
    }
}
