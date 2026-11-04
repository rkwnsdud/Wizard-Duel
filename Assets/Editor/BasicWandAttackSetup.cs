using UnityEditor;

// Kept as the menu entry used by existing project documentation and shortcuts.
public static class BasicWandAttackSetup
{
    [MenuItem("Tools/Basic Wand/Build Attack %&#k")]
    public static void Build() => LightningBasedWandAttackSetup.Build();

    public static void BuildBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity");
        Build();
    }
}
