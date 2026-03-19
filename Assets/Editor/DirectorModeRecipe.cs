using System;
using System.Collections.Generic;
using Snippets.Sdk;
using UnityEditor;
using UnityEngine;

[Serializable]
public class DirectorModeStop
{
    public string label = "Stop";
    public int snippetIndex;
    public string destinationObjectName;
    public string faceObjectName;
    public string sideGlanceObjectName;
    public float standDistance = 0.85f;
    [Range(0f, 1f)] public float sideGlancePercent = 0.64f;
}

public class DirectorModeRecipe : ScriptableObject
{
    public string scenePath = "Assets/RPGPP_LT/Scene/rpgpp_lt_scene_1.0.unity";
    public DefaultAsset snippetFolder;
    public string snippetFolderPath = "Assets/My Snippets/Make a snippet of a medieval warrior talking";
    public string actorDisplayName = "Medieval Warrior";
    public SnippetTextDisplayMode textDisplayMode = SnippetTextDisplayMode.HighlightAsSpoken;
    public bool createFirstPersonController = true;
    public bool playOnStart = true;
    public string spawnNearObjectName = "rpgpp_lt_building_02 (1)";
    public string spawnFacingObjectName;
    public string spawnFacingObjectPrefix = "rpgpp_lt_terrain_path_01b";
    public List<DirectorModeStop> stops = new List<DirectorModeStop>();

    public string SnippetFolderPath
    {
        get
        {
            if (snippetFolder != null)
                return AssetDatabase.GetAssetPath(snippetFolder);

            return snippetFolderPath ?? string.Empty;
        }
    }

    public void ResetToRpgppDefaults()
    {
        scenePath = "Assets/RPGPP_LT/Scene/rpgpp_lt_scene_1.0.unity";
        snippetFolderPath = "Assets/My Snippets/Make a snippet of a medieval warrior talking";
        snippetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
            snippetFolderPath);
        actorDisplayName = "Medieval Warrior";
        textDisplayMode = SnippetTextDisplayMode.HighlightAsSpoken;
        createFirstPersonController = true;
        playOnStart = true;
        spawnNearObjectName = "rpgpp_lt_building_02 (1)";
        spawnFacingObjectName = string.Empty;
        spawnFacingObjectPrefix = "rpgpp_lt_terrain_path_01b";

        stops = new List<DirectorModeStop>
        {
            new DirectorModeStop
            {
                label = "Snippet 1",
                snippetIndex = 0,
                faceObjectName = string.Empty,
                sideGlanceObjectName = string.Empty,
                standDistance = 0.85f,
                sideGlancePercent = 0f
            },
            new DirectorModeStop
            {
                label = "Well",
                snippetIndex = 1,
                destinationObjectName = "rpgpp_lt_well_01",
                faceObjectName = "rpgpp_lt_bird_house_01",
                sideGlanceObjectName = "rpgpp_lt_well_01",
                standDistance = 0.85f,
                sideGlancePercent = 0.64f
            },
            new DirectorModeStop
            {
                label = "Stones",
                snippetIndex = 2,
                destinationObjectName = "rpgpp_lt_stones_01",
                faceObjectName = "rpgpp_lt_well_01",
                sideGlanceObjectName = "rpgpp_lt_stones_01",
                standDistance = 0.85f,
                sideGlancePercent = 0.63f
            },
            new DirectorModeStop
            {
                label = "Table",
                snippetIndex = 3,
                destinationObjectName = "rpgpp_lt_table_01",
                faceObjectName = "rpgpp_lt_stones_01",
                sideGlanceObjectName = "rpgpp_lt_table_01",
                standDistance = 1.05f,
                sideGlancePercent = 0.66f
            }
        };
    }
}
