#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Editor 工具：在当前打开的场景中创建或修复主相机与 UI 结构。
/// 使用：菜单 Tools -> Setup -> Create Main Camera + UI
/// 说明：运行后脚本会修改并保存当前场景（请在版本控制下使用）。
/// </summary>
public static class SceneSetup_UIAndCamera
{
    [MenuItem("Tools/Setup/Create Main Camera + UI")]
    public static void CreateMainCameraAndUI()
    {
        try
        {
            // --- Main Camera ---
            GameObject camGO = Camera.main != null ? Camera.main.gameObject : GameObject.FindWithTag("MainCamera");
            bool createdCam = false;
            if (camGO == null)
            {
                camGO = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(camGO, "Create Main Camera");
                camGO.AddComponent<Camera>();
                createdCam = true;
            }
            Camera cam = camGO.GetComponent<Camera>();
            Undo.RecordObject(cam, "Configure Main Camera");

            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.depth = 0;
            cam.gameObject.tag = "MainCamera";

            // Add CameraFollow if missing (Script namespace)
            var follow = camGO.GetComponent<Script.CameraFollow>();
            if (follow == null)
            {
                follow = Undo.AddComponent<Script.CameraFollow>(camGO);
            }
            Undo.RecordObject(follow, "Configure CameraFollow");
            follow.offset = new Vector3(0f, 0f, -10f);
            follow.smoothTime = 0.12f;

            // Try to find Player (by PlayerController or name/tag)
            Transform playerTransform = null;
            var playerController = Object.FindObjectOfType<Script.PlayerController>();
            if (playerController != null) playerTransform = playerController.transform;
            if (playerTransform == null)
            {
                var byTag = GameObject.FindWithTag("Player");
                if (byTag != null) playerTransform = byTag.transform;
            }
            if (playerTransform == null)
            {
                var byName = GameObject.Find("Player");
                if (byName != null) playerTransform = byName.transform;
            }
            if (playerTransform != null)
            {
                Undo.RecordObject(follow, "Assign CameraFollow target");
                follow.target = playerTransform;
            }

            // --- UI Root ---
            GameObject uiRoot = GameObject.Find("UI");
            if (uiRoot == null)
            {
                uiRoot = new GameObject("UI");
                Undo.RegisterCreatedObjectUndo(uiRoot, "Create UI Root");
            }

            // Canvas
            Canvas canvas = uiRoot.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("Canvas");
                Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
                canvasGO.transform.SetParent(uiRoot.transform, false);
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<GraphicRaycaster>();
                var cs = canvasGO.AddComponent<CanvasScaler>();
                cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                cs.referenceResolution = new Vector2(1920, 1080);
                cs.matchWidthOrHeight = 0.5f;
            }

            // EventSystem
            var es = Object.FindObjectOfType<EventSystem>();
            if (es == null)
            {
                GameObject esGO = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
            }

            // HUD, Menus, Popups, Minimap
            CreateChildIfMissing(uiRoot.transform, "HUD");
            CreateChildIfMissing(uiRoot.transform, "Menus");
            CreateChildIfMissing(uiRoot.transform, "Popups");

            // Minimap container with RawImage placeholder
            Transform minimap = uiRoot.transform.Find("Minimap");
            if (minimap == null)
            {
                GameObject mm = new GameObject("Minimap");
                Undo.RegisterCreatedObjectUndo(mm, "Create Minimap");
                mm.transform.SetParent(uiRoot.transform, false);
                var raw = Undo.AddComponent<RawImage>(mm);
                raw.color = new Color(1f, 1f, 1f, 1f);
                var rt = mm.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.78f, 0.78f);
                rt.anchorMax = new Vector2(0.98f, 0.98f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }

            // Mark scene dirty and save
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("Scene setup: Main Camera and UI created/configured.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Scene setup failed: " + ex.Message);
        }
    }

    private static void CreateChildIfMissing(Transform parent, string name)
    {
        if (parent.Find(name) == null)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
        }
    }
}
#endif
