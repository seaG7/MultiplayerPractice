#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PracticeSetupUtility
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string DefaultServerBuildPath = "Builds/LinuxServer/MultiplayerPracticeServer.x86_64";

    [MenuItem("Tools/Practice/Configure FishNet Practice")]
    public static void ConfigureFishNetPractice()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        ConfigureBuildScenes();
        ConfigureNetworkManager();
        ConfigureGameSessionManager();
        ReserializeSceneNetworkObjects(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Practice] FishNet scene configuration complete.");
    }

    [MenuItem("Tools/Practice/Build Linux Dedicated Server")]
    public static void BuildLinuxDedicatedServer()
    {
        ConfigureFishNetPractice();

        string outputPath = GetCommandLineValue("-buildPath", DefaultServerBuildPath);
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        BuildPlayerOptions options = new()
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None
        };

        BuildPipeline.BuildPlayer(options);
    }

    private static void ConfigureNetworkManager()
    {
        GameObject networkManagerObject = GameObject.Find("NetworkManager");
        if (networkManagerObject == null)
        {
            Debug.LogWarning("[Practice] NetworkManager object was not found in the scene.");
            return;
        }

        Tugboat tugboat = networkManagerObject.GetComponent<Tugboat>();
        if (tugboat != null)
        {
            tugboat.SetPort(NetworkLaunchOptions.DefaultPort);
            tugboat.SetClientAddress("127.0.0.1");
            EditorUtility.SetDirty(tugboat);
        }

        ServerManager serverManager = networkManagerObject.GetComponent<ServerManager>();
        if (serverManager != null)
        {
            SerializedObject serializedServerManager = new(serverManager);
            SerializedProperty startOnHeadless = serializedServerManager.FindProperty("_startOnHeadless");
            if (startOnHeadless != null)
            {
                startOnHeadless.boolValue = false;
                serializedServerManager.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureBuildScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(GameScenePath, true)
        };
    }

    private static void ConfigureGameSessionManager()
    {
        GameSessionManager sessionManager = UnityEngine.Object.FindFirstObjectByType<GameSessionManager>();
        GameObject sessionObject = sessionManager == null ? null : sessionManager.gameObject;
        if (sessionObject == null)
        {
            sessionObject = new GameObject("GameSessionManager");
            sessionObject.transform.position = Vector3.zero;
        }

        NetworkObject networkObject = sessionObject.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            UnityEngine.Object.DestroyImmediate(networkObject, true);
        }

        if (sessionObject.GetComponent<GameSessionManager>() == null)
        {
            sessionObject.AddComponent<GameSessionManager>();
        }

        EditorUtility.SetDirty(sessionObject);
    }

    private static void ReserializeSceneNetworkObjects(Scene scene)
    {
        MethodInfo createSceneId = typeof(NetworkObject).GetMethod("CreateSceneId", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo reserializeValues = typeof(NetworkObject).GetMethod("ReserializeEditorSetValues", BindingFlags.Instance | BindingFlags.NonPublic);
        if (createSceneId == null || reserializeValues == null)
        {
            Debug.LogWarning("[Practice] FishNet reserialize reflection hooks were not found.");
            return;
        }

        object[] createArgs = { scene, true, 0 };
        createSceneId.Invoke(null, createArgs);

        foreach (NetworkObject networkObject in UnityEngine.Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            reserializeValues.Invoke(networkObject, new object[] { true, false });
            EditorUtility.SetDirty(networkObject);
        }
    }

    private static string GetCommandLineValue(string key, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }
}
#endif
