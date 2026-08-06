using UnityEditor;
using UnityEngine;

namespace DuneVector.Editor
{
    public sealed class DuneVectorRetroCrtScanlineShaderGUI : ShaderGUI
    {
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material material = materialEditor.target as Material;
            DuneVectorRuntimeSettings settings = AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(
                RuntimeSettingsPath);

            if (material == null
                || settings == null
                || settings.RetroCrtScanlines == null
                || settings.RetroCrtScanlines.Material != material)
            {
                DrawFallbackInspector(materialEditor, properties);
                EditorGUILayout.HelpBox(
                    "This material is not assigned to Retro CRT Scanlines in the Dune Vector Runtime Settings asset. " +
                    "Material values may be replaced by the active runtime settings.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "These controls edit Dune Vector Runtime Settings > WORLD > Retro CRT Scanlines, " +
                "which is the persistent source used in Play Mode.",
                MessageType.Info);

            SerializedObject settingsObject = new SerializedObject(settings);
            SerializedProperty scanlines = settingsObject.FindProperty("RetroCrtScanlines");
            SerializedProperty enabled = scanlines.FindPropertyRelative("Enabled");
            SerializedProperty height = scanlines.FindPropertyRelative("ScanlineHeight");
            SerializedProperty strength = scanlines.FindPropertyRelative("ScanlineStrength");

            settingsObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(enabled);
            EditorGUILayout.PropertyField(height);
            EditorGUILayout.PropertyField(strength);
            if (EditorGUI.EndChangeCheck())
            {
                settingsObject.ApplyModifiedProperties();
                ApplySettingsToMaterial(material, settings.RetroCrtScanlines);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Runtime Settings", GUILayout.Width(170f)))
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }

            EditorGUILayout.Space();
            materialEditor.RenderQueueField();
            materialEditor.DoubleSidedGIField();
        }

        private static void DrawFallbackInspector(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            MaterialProperty height = FindProperty("_ScanlineHeight", properties);
            MaterialProperty strength = FindProperty("_ScanlineStrength", properties);
            materialEditor.ShaderProperty(height, height.displayName);
            materialEditor.ShaderProperty(strength, strength.displayName);
            materialEditor.RenderQueueField();
            materialEditor.DoubleSidedGIField();
        }

        private static void ApplySettingsToMaterial(Material material, RetroCrtScanlineTuning scanlines)
        {
            Undo.RecordObject(material, "Change Retro CRT Scanlines");
            material.SetFloat("_ScanlineHeight", Mathf.Max(1f, scanlines.ScanlineHeight));
            material.SetFloat(
                "_ScanlineStrength",
                scanlines.Enabled ? Mathf.Clamp01(scanlines.ScanlineStrength) : 0f);
            EditorUtility.SetDirty(material);
        }
    }
}
