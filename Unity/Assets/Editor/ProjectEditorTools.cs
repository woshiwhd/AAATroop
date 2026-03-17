using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 项目级编辑器杂项工具集（仅 Editor）。
/// - 适合放置：打开常用场景、定位常用资源、批量小工具等
/// </summary>
public static class ProjectEditorTools
{
    public const string MainScenePath = "Assets/Scenes/Main.scene";

    [MenuItem("Tools/Project/Open Main Scene")]
    public static void OpenMainSceneMenu()
    {
        OpenMainScene();
    }

    public static void OpenMainScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var active = EditorSceneManager.GetActiveScene();
        if (active.IsValid() && active.path == MainScenePath) return;

        EditorSceneManager.OpenScene(MainScenePath);
    }
}

