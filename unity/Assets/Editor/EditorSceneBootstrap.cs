using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Whisper.Runtime;

namespace Whisper.Editor
{
    /// <summary>
    /// Boot 场景**代码生成**（V9 §19.1 C1「场景零手工」）。
    ///
    /// 为什么要有它：本机没有 Unity 编辑器，`.unity` 场景文件无法手工拖拽生成；
    /// 而场景里其实只需要**一个**挂着 <see cref="GameBootstrap"/> 的空物体 ——
    /// 几何、UI、三接口注入全部由代码完成。既然如此，场景就该由脚本生成，
    /// 顺带满足 No-Editor 纪律：任何人 clone 下来执行一次即得到可构建的工程。
    /// </summary>
    public static class EditorSceneBootstrap
    {
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = SceneDir + "/Boot.unity";

        [MenuItem("Whisper/生成 Boot 场景")]
        public static void CreateBootScene()
        {
            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Whisper] 已生成 {ScenePath}（单物体场景：仅挂 GameBootstrap，其余全部代码化）");

            // 写进 Build Settings，保证命令行构建能拿到场景
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        /// <summary>CI 入口：确保存在 Boot 场景与构建场景列表。</summary>
        public static void EnsureBootScene()
        {
            if (!File.Exists(ScenePath)) CreateBootScene();
            else EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
