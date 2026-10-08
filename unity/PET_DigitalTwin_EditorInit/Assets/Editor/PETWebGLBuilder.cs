using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Compila la escena PET_Plant a WebAssembly y publica el resultado en
/// <c>unity/webgl</c>, la carpeta que FastAPI sirve en <c>/unity/</c> y que el
/// dashboard incrusta en el panel de vinculacion.
/// </summary>
public static class PETWebGLBuilder
{
    private const string MenuPath = "Tools/PET Digital Twin/Build WebGL";
    private const string Title = "PET Digital Twin";
    private const string OutputFolderName = "webgl";

    [MenuItem(MenuPath)]
    public static void BuildWebGL()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDirectory = Path.GetFullPath(Path.Combine(projectRoot, "..", OutputFolderName));

        if (Path.GetFileName(outputDirectory) != OutputFolderName)
        {
            Debug.LogError($"PET Digital Twin: ruta de salida inesperada: {outputDirectory}");
            return;
        }

        string[] scenes = CollectScenes();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog(
                Title,
                "No hay escenas habilitadas en Build Settings.\n\n" +
                "Ejecuta primero Tools > PET Digital Twin > Build PET Plant.",
                "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // Unity compila en una carpeta temporal: unity/webgl ya contiene un README
        // y la copia final no debe arrastrar artefactos de corridas anteriores.
        string staging = outputDirectory + ".staging";

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);

            BuildReport report = BuildPipeline.BuildPlayer(scenes, staging, BuildTarget.WebGL, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    Title,
                    $"El build WebGL termino en {report.summary.result}.\n\n" +
                    "Revisa la Console. Si el error menciona soporte WebGL ausente, instala " +
                    "'WebGL Build Support' desde Unity Hub > Installed > Add modules.",
                    "OK");
                return;
            }

            Publish(staging, outputDirectory);

            string index = Path.Combine(outputDirectory, "index.html");
            Debug.Log($"PET Digital Twin WebGL publicado en {outputDirectory}");

            EditorUtility.RevealInFinder(index);
            EditorUtility.DisplayDialog(
                Title,
                "Build WebGL publicado en unity/webgl.\n\n" +
                "Abre http://127.0.0.1:8000/ : el panel de vinculacion detecta el visor " +
                "en el siguiente sondeo, sin reiniciar la API.",
                "OK");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorUtility.DisplayDialog(
                Title,
                $"No se pudo generar el build WebGL:\n\n{error.Message}\n\n" +
                "Si el mensaje menciona WebGL, instala 'WebGL Build Support' desde " +
                "Unity Hub > Installed > Add modules.",
                "OK");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    private static string[] CollectScenes()
    {
        List<string> scenes = new List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                scenes.Add(scene.path);
            }
        }

        if (scenes.Count == 0 && File.Exists("Assets/Scenes/PET_Plant.unity"))
        {
            scenes.Add("Assets/Scenes/PET_Plant.unity");
        }

        return scenes.ToArray();
    }

    /// <summary>
    /// Copia el contenido compilado al destino conservando el README de la carpeta.
    /// </summary>
    private static void Publish(string staging, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        foreach (string entry in Directory.GetFileSystemEntries(staging))
        {
            string target = Path.Combine(outputDirectory, Path.GetFileName(entry));

            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }

            FileUtil.CopyFileOrDirectory(entry, target);
        }
    }
}
