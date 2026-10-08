using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PETDigitalTwinSetup
{
    private const string ScenePath = "Assets/Scenes/PET_Plant.unity";
    private const string MaterialsPath = "Assets/Materials";

    private static readonly string[] RequiredFolders =
    {
        "Assets/Editor",
        "Assets/Scripts",
        "Assets/Materials",
        "Assets/Prefabs",
        "Assets/Scenes",
        "Assets/Models",
        "Assets/UI"
    };

    private static readonly (string Id, string Type)[] MachineDefinitions =
    {
        ("D-101", "PET Dryer"),
        ("H-101", "Hopper"),
        ("IMM-101", "Injection Molding Machine"),
        ("CV-101", "Preform Conveyor"),
        ("BF-101", "Buffer"),
        ("OV-101", "Reheat Oven"),
        ("SBM-101", "Stretch Blow Molding Machine"),
        ("QC-101", "Quality Inspection"),
        ("PKG-101", "Packaging")
    };

    [MenuItem("Tools/PET Digital Twin/Build PET Plant")]
    public static void BuildPetPlant()
    {
        EnsureFolders();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject plantRoot = new GameObject("PET Plant");
        plantRoot.AddComponent<DigitalTwinApiClient>();
        PlantManager plantManager = plantRoot.AddComponent<PlantManager>();

        Dictionary<MachineState, Material> stateMaterials = CreateStateMaterials();
        Material floorMaterial = LoadOrCreateMaterial("MAT_IndustrialFloor", new Color(0.18f, 0.20f, 0.22f));
        Material structureMaterial = LoadOrCreateMaterial("MAT_Structure", new Color(0.23f, 0.27f, 0.31f));
        Material conveyorMaterial = LoadOrCreateMaterial("MAT_Conveyor", new Color(0.06f, 0.07f, 0.08f));
        Material productMaterial = LoadOrCreateMaterial("MAT_Preform", new Color(0.15f, 0.72f, 0.95f));

        CreateFloor(plantRoot.transform, floorMaterial);
        CreateTransportLane(plantRoot.transform, structureMaterial, conveyorMaterial);

        const float spacing = 5f;
        float firstX = -0.5f * spacing * (MachineDefinitions.Length - 1);

        for (int index = 0; index < MachineDefinitions.Length; index++)
        {
            (string id, string type) = MachineDefinitions[index];
            Vector3 position = new Vector3(firstX + index * spacing, 0f, 0.7f);
            CreateMachine(plantRoot.transform, id, type, position, stateMaterials, structureMaterial, conveyorMaterial);
        }

        Transform[] productPath = CreateProductPath(plantRoot.transform, firstX, spacing);
        CreatePreforms(plantRoot.transform, productPath, productMaterial);
        CreateLighting(plantRoot.transform);
        CreateCamera(plantRoot.transform);

        plantManager.RefreshMachines();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = plantRoot;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        if (saved)
        {
            Debug.Log($"PET Digital Twin plant created at {ScenePath}");
        }
        else
        {
            Debug.LogError($"PET Digital Twin could not save the scene at {ScenePath}");
        }
    }

    private static void EnsureFolders()
    {
        foreach (string folder in RequiredFolders)
        {
            EnsureFolder(folder);
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = System.IO.Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }

    private static Dictionary<MachineState, Material> CreateStateMaterials()
    {
        return new Dictionary<MachineState, Material>
        {
            { MachineState.IDLE, LoadOrCreateMaterial("MAT_State_IDLE", new Color(0.38f, 0.42f, 0.46f)) },
            { MachineState.WAITING, LoadOrCreateMaterial("MAT_State_WAITING", new Color(1.00f, 0.72f, 0.05f)) },
            { MachineState.RUNNING, LoadOrCreateMaterial("MAT_State_RUNNING", new Color(0.08f, 0.72f, 0.25f)) },
            { MachineState.FAULT, LoadOrCreateMaterial("MAT_State_FAULT", new Color(0.90f, 0.08f, 0.07f)) },
            { MachineState.MAINTENANCE, LoadOrCreateMaterial("MAT_State_MAINTENANCE", new Color(1.00f, 0.38f, 0.05f)) },
            { MachineState.FINISHED, LoadOrCreateMaterial("MAT_State_FINISHED", new Color(0.12f, 0.48f, 0.95f)) }
        };
    }

    private static Material LoadOrCreateMaterial(string materialName, Color color)
    {
        string assetPath = $"{MaterialsPath}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

        if (material == null)
        {
            Shader shader = FindCompatibleShader();
            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, assetPath);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Shader FindCompatibleShader()
    {
        string[] shaderNames =
        {
            "Standard",
            "Universal Render Pipeline/Lit",
            "HDRP/Lit",
            "Unlit/Color"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                return shader;
            }
        }

        return Shader.Find("Hidden/InternalErrorShader");
    }

    private static void CreateFloor(Transform parent, Material floorMaterial)
    {
        GameObject floor = CreatePrimitive(
            parent,
            "Industrial Floor",
            PrimitiveType.Plane,
            Vector3.zero,
            new Vector3(5.5f, 1f, 2.2f),
            floorMaterial);
        floor.transform.position = Vector3.zero;
    }

    private static void CreateTransportLane(Transform parent, Material structureMaterial, Material beltMaterial)
    {
        GameObject lane = new GameObject("Preform Transport Lane");
        lane.transform.SetParent(parent, false);

        CreatePrimitive(lane.transform, "Belt", PrimitiveType.Cube, new Vector3(5f, 0.65f, -1.6f), new Vector3(31f, 0.18f, 0.85f), beltMaterial);
        CreatePrimitive(lane.transform, "Near Rail", PrimitiveType.Cube, new Vector3(5f, 0.9f, -2.05f), new Vector3(31f, 0.12f, 0.12f), structureMaterial);
        CreatePrimitive(lane.transform, "Far Rail", PrimitiveType.Cube, new Vector3(5f, 0.9f, -1.15f), new Vector3(31f, 0.12f, 0.12f), structureMaterial);

        for (float x = -10f; x <= 20f; x += 5f)
        {
            CreatePrimitive(lane.transform, $"Support {x:0}", PrimitiveType.Cube, new Vector3(x, 0.3f, -1.6f), new Vector3(0.18f, 0.6f, 0.72f), structureMaterial);
        }
    }

    private static void CreateMachine(
        Transform parent,
        string id,
        string type,
        Vector3 position,
        IReadOnlyDictionary<MachineState, Material> stateMaterials,
        Material structureMaterial,
        Material conveyorMaterial)
    {
        GameObject machine = new GameObject($"{id} - {type}");
        machine.transform.SetParent(parent, false);
        machine.transform.localPosition = position;

        CreatePrimitive(machine.transform, "Base", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0f), new Vector3(3.4f, 0.24f, 2.5f), structureMaterial);
        List<Renderer> stateVisuals = new List<Renderer>();

        switch (id)
        {
            case "D-101":
                AddStatePart(machine.transform, stateVisuals, "Dryer Vessel", PrimitiveType.Cylinder, new Vector3(0f, 1.65f, 0f), new Vector3(1.05f, 1.45f, 1.05f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Dryer Top", PrimitiveType.Sphere, new Vector3(0f, 3.05f, 0f), new Vector3(1.02f, 0.45f, 1.02f), structureMaterial);
                break;
            case "H-101":
                AddStatePart(machine.transform, stateVisuals, "Hopper Body", PrimitiveType.Cylinder, new Vector3(0f, 2.1f, 0f), new Vector3(0.92f, 1.05f, 0.92f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Hopper Outlet", PrimitiveType.Cylinder, new Vector3(0f, 0.8f, 0f), new Vector3(0.38f, 0.35f, 0.38f), structureMaterial);
                break;
            case "IMM-101":
                AddStatePart(machine.transform, stateVisuals, "Injection Unit", PrimitiveType.Cube, new Vector3(-0.55f, 1.1f, 0f), new Vector3(1.7f, 1.55f, 1.45f), stateMaterials[MachineState.IDLE]);
                AddStatePart(machine.transform, stateVisuals, "Clamp Unit", PrimitiveType.Cube, new Vector3(0.85f, 1.25f, 0f), new Vector3(0.9f, 1.9f, 1.65f), stateMaterials[MachineState.IDLE]);
                break;
            case "CV-101":
                CreatePrimitive(machine.transform, "Conveyor Belt", PrimitiveType.Cube, new Vector3(0f, 0.8f, 0f), new Vector3(3.1f, 0.22f, 1.3f), conveyorMaterial);
                AddStatePart(machine.transform, stateVisuals, "Drive Motor", PrimitiveType.Cylinder, new Vector3(0f, 0.72f, 0.82f), new Vector3(0.42f, 0.48f, 0.42f), stateMaterials[MachineState.IDLE], Quaternion.Euler(90f, 0f, 0f));
                break;
            case "BF-101":
                AddStatePart(machine.transform, stateVisuals, "Buffer", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0f), new Vector3(2.25f, 2.0f, 1.9f), stateMaterials[MachineState.IDLE]);
                break;
            case "OV-101":
                AddStatePart(machine.transform, stateVisuals, "Oven Tunnel", PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(3.15f, 1.65f, 1.65f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Oven Opening", PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.86f), new Vector3(1.8f, 0.8f, 0.08f), conveyorMaterial);
                break;
            case "SBM-101":
                AddStatePart(machine.transform, stateVisuals, "Blow Molder", PrimitiveType.Cube, new Vector3(0f, 1.4f, 0f), new Vector3(2.75f, 2.45f, 1.9f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Blow Mold Head", PrimitiveType.Cylinder, new Vector3(0f, 2.85f, 0f), new Vector3(0.68f, 0.36f, 0.68f), structureMaterial);
                break;
            case "QC-101":
                AddStatePart(machine.transform, stateVisuals, "Inspection Head", PrimitiveType.Cube, new Vector3(0f, 2.25f, 0f), new Vector3(2.2f, 0.65f, 1.55f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Left Post", PrimitiveType.Cube, new Vector3(-0.9f, 1.1f, 0f), new Vector3(0.25f, 2.2f, 0.35f), structureMaterial);
                CreatePrimitive(machine.transform, "Right Post", PrimitiveType.Cube, new Vector3(0.9f, 1.1f, 0f), new Vector3(0.25f, 2.2f, 0.35f), structureMaterial);
                break;
            default:
                AddStatePart(machine.transform, stateVisuals, "Packaging Cell", PrimitiveType.Cube, new Vector3(0f, 1.25f, 0f), new Vector3(2.5f, 2f, 2f), stateMaterials[MachineState.IDLE]);
                CreatePrimitive(machine.transform, "Package", PrimitiveType.Cube, new Vector3(0f, 0.65f, -1.15f), new Vector3(0.9f, 0.9f, 0.65f), structureMaterial);
                break;
        }

        CreateMachineLabel(machine.transform, id, type);

        MachineController controller = machine.AddComponent<MachineController>();
        controller.Configure(
            id,
            type,
            stateVisuals.ToArray(),
            stateMaterials[MachineState.IDLE],
            stateMaterials[MachineState.WAITING],
            stateMaterials[MachineState.RUNNING],
            stateMaterials[MachineState.FAULT],
            stateMaterials[MachineState.MAINTENANCE],
            stateMaterials[MachineState.FINISHED]);
    }

    private static void AddStatePart(
        Transform parent,
        ICollection<Renderer> stateVisuals,
        string name,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        Quaternion? localRotation = null)
    {
        GameObject part = CreatePrimitive(parent, name, primitiveType, localPosition, localScale, material, localRotation);
        stateVisuals.Add(part.GetComponent<Renderer>());
    }

    private static void CreateMachineLabel(Transform parent, string id, string type)
    {
        GameObject labelObject = new GameObject("Machine Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = new Vector3(0f, 3.65f, -0.2f);

        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = $"{id}\n{type}";
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.055f;
        label.fontSize = 48;
        label.color = Color.white;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            label.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    private static Transform[] CreateProductPath(Transform parent, float firstX, float spacing)
    {
        GameObject pathRoot = new GameObject("Product Path");
        pathRoot.transform.SetParent(parent, false);

        List<Transform> waypoints = new List<Transform>();
        for (int index = 2; index < MachineDefinitions.Length; index++)
        {
            GameObject waypoint = new GameObject($"Waypoint {index - 1:00}");
            waypoint.transform.SetParent(pathRoot.transform, false);
            waypoint.transform.localPosition = new Vector3(firstX + index * spacing, 1.05f, -1.6f);
            waypoints.Add(waypoint.transform);
        }

        return waypoints.ToArray();
    }

    private static void CreatePreforms(Transform parent, Transform[] path, Material productMaterial)
    {
        GameObject productsRoot = new GameObject("PET Preforms");
        productsRoot.transform.SetParent(parent, false);

        const int preformCount = 6;
        for (int index = 0; index < preformCount; index++)
        {
            GameObject preform = new GameObject($"Preform {index + 1:00}");
            preform.transform.SetParent(productsRoot.transform, false);

            CreatePrimitive(preform.transform, "Body", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.16f, 0.36f, 0.16f), productMaterial);
            CreatePrimitive(preform.transform, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.21f, 0.08f, 0.21f), productMaterial);

            ProductMover mover = preform.AddComponent<ProductMover>();
            mover.Configure(path, 2.2f, index / (float)preformCount);
        }
    }

    private static void CreateLighting(Transform parent)
    {
        GameObject lightObject = new GameObject("Directional Light");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        Light directionalLight = lightObject.AddComponent<Light>();
        directionalLight.type = LightType.Directional;
        directionalLight.intensity = 1.15f;
        directionalLight.shadows = LightShadows.Soft;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.47f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.23f, 0.25f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.11f, 0.12f);
    }

    private static void CreateCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(0f, 25f, -34f);
        cameraObject.transform.LookAt(new Vector3(0f, 1.2f, 0f));

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 52f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        camera.backgroundColor = new Color(0.06f, 0.08f, 0.11f);
        cameraObject.AddComponent<AudioListener>();
    }

    private static GameObject CreatePrimitive(
        Transform parent,
        string name,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        Quaternion? localRotation = null)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = localRotation ?? Quaternion.identity;
        primitive.transform.localScale = localScale;

        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        return primitive;
    }
}
