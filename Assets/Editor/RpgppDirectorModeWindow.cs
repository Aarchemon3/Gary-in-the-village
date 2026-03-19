using Snippets.Sdk;
using UnityEditor;
using UnityEngine;

public class RpgppDirectorModeWindow : EditorWindow
{
    DirectorModeRecipe _recipe;
    Editor _recipeEditor;
    Vector2 _scroll;

    [MenuItem("Tools/RPGPP/Director Mode")]
    static void OpenWindow()
    {
        var window = GetWindow<RpgppDirectorModeWindow>("Director Mode");
        window.minSize = new Vector2(520f, 620f);
        window.Show();
    }

    [MenuItem("Tools/RPGPP/Create Default Director Recipe")]
    static void CreateDefaultRecipeMenu()
    {
        var recipe = LoadOrCreateDefaultRecipe(selectAsset: true);
        EditorGUIUtility.PingObject(recipe);
    }

    void OnEnable()
    {
        if (_recipe == null)
            _recipe = RpgppDirectorModeDefaults.LoadDefaultRecipe();
    }

    void OnDisable()
    {
        if (_recipeEditor != null)
            DestroyImmediate(_recipeEditor);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Director Mode builds a reusable staged snippet performance from a recipe: snippet set, scene anchors, movement, gaze, and text display mode.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            var newRecipe = (DirectorModeRecipe)EditorGUILayout.ObjectField("Recipe", _recipe, typeof(DirectorModeRecipe), false);
            if (newRecipe != _recipe)
            {
                _recipe = newRecipe;
                RecreateEditor();
            }

            if (GUILayout.Button("Load Default", GUILayout.Width(110f)))
            {
                _recipe = RpgppDirectorModeDefaults.LoadDefaultRecipe();
                RecreateEditor();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Default Recipe"))
            {
                _recipe = LoadOrCreateDefaultRecipe(selectAsset: true);
                RecreateEditor();
            }

            GUI.enabled = _recipe != null;
            if (GUILayout.Button("Reset To RPGPP Defaults"))
            {
                Undo.RecordObject(_recipe, "Reset Director Recipe");
                _recipe.ResetToRpgppDefaults();
                EditorUtility.SetDirty(_recipe);
                AssetDatabase.SaveAssets();
            }
            GUI.enabled = true;
        }

        if (_recipe == null)
        {
            EditorGUILayout.HelpBox("Create or assign a Director Mode recipe to continue.", MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_recipeEditor == null)
            RecreateEditor();
        _recipeEditor?.OnInspectorGUI();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build Open Scene", GUILayout.Height(32f)))
                RpgppDirectorModeBuilder.BuildOpenScene(_recipe);

            if (GUILayout.Button("Open Recipe Scene + Build", GUILayout.Height(32f)))
                RpgppDirectorModeBuilder.BuildRecipeSceneAndSave(_recipe);
        }
    }

    void RecreateEditor()
    {
        if (_recipeEditor != null)
            DestroyImmediate(_recipeEditor);

        if (_recipe != null)
            _recipeEditor = Editor.CreateEditor(_recipe);
    }

    static DirectorModeRecipe LoadOrCreateDefaultRecipe(bool selectAsset)
    {
        var recipe = RpgppDirectorModeDefaults.LoadOrCreateDefaultRecipe();

        if (selectAsset)
            Selection.activeObject = recipe;

        return recipe;
    }
}

public static class RpgppDirectorModeDefaults
{
    public const string DefaultRecipePath = "Assets/_Snippets/Config/RpgppWarriorDirectorModeRecipe.asset";

    public static DirectorModeRecipe LoadDefaultRecipe()
    {
        return AssetDatabase.LoadAssetAtPath<DirectorModeRecipe>(DefaultRecipePath);
    }

    public static DirectorModeRecipe LoadOrCreateDefaultRecipe()
    {
        var recipe = LoadDefaultRecipe();
        if (recipe != null)
            return recipe;

        if (System.IO.File.Exists(DefaultRecipePath))
            AssetDatabase.DeleteAsset(DefaultRecipePath);

        recipe = ScriptableObject.CreateInstance<DirectorModeRecipe>();
        recipe.ResetToRpgppDefaults();

        var folder = System.IO.Path.GetDirectoryName(DefaultRecipePath)?.Replace("\\", "/");
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            var parent = "Assets";
            foreach (var part in folder.Split('/'))
            {
                if (part == "Assets")
                    continue;

                var candidate = $"{parent}/{part}";
                if (!AssetDatabase.IsValidFolder(candidate))
                    AssetDatabase.CreateFolder(parent, part);
                parent = candidate;
            }
        }

        AssetDatabase.CreateAsset(recipe, DefaultRecipePath);
        AssetDatabase.SaveAssets();
        return recipe;
    }
}

[CustomEditor(typeof(DirectorModeRecipe))]
public class DirectorModeRecipeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("scenePath"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("snippetFolder"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("snippetFolderPath"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("actorDisplayName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("textDisplayMode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("createFirstPersonController"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playOnStart"));
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnNearObjectName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnFacingObjectName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnFacingObjectPrefix"));
        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("stops"), true);

        serializedObject.ApplyModifiedProperties();
    }
}
