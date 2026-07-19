using UnityEditor;
using UnityEngine;

namespace DuneVector.Editor
{
    [CustomEditor(typeof(DuneVectorBootstrap))]
    public sealed class DuneVectorBootstrapEditor : UnityEditor.Editor
    {
        private static readonly string[] PresetDescriptions =
        {
            "Balanced layered dunes used by the original prototype.",
            "Low, broad, smooth dunes suited to relaxed traversal and cinematic shots.",
            "Huge sweeping dune formations with tall crests and long wavelengths.",
            "Tight, angular ridgelines with crisp crests and stronger surface definition.",
            "Heavily warped directional dunes shaped by uneven crosswinds.",
            "Soft, rounded wind-shaped dunes with broad crests and restrained fine detail.",
            "Flowing ribbon-like dune bands with strong warp and smooth rolling surfaces.",
            "Large rounded wind swells with long wavelengths and monumental soft crests.",
            "Broad rolling terrain where secondary landforms dominate the ridge pattern.",
            "Dense small ripples with high mesh detail; visually rich but more expensive.",
            "Tall, chaotic, highly warped terrain intended for dramatic or difficult routes.",
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            DuneVectorBootstrap bootstrap = (DuneVectorBootstrap)target;
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Dune Presets", EditorStyles.boldLabel);

            DuneGenerationPreset selected = (DuneGenerationPreset)EditorGUILayout.EnumPopup(
                "Preset",
                bootstrap.SelectedDunePreset);
            if (selected != bootstrap.SelectedDunePreset)
            {
                Undo.RecordObject(bootstrap, "Select Dune Preset");
                bootstrap.SelectedDunePreset = selected;
                EditorUtility.SetDirty(bootstrap);
            }

            int descriptionIndex = Mathf.Clamp((int)bootstrap.SelectedDunePreset, 0, PresetDescriptions.Length - 1);
            EditorGUILayout.HelpBox(PresetDescriptions[descriptionIndex], MessageType.Info);

            if (GUILayout.Button("Apply Preset", GUILayout.Height(30f)))
            {
                Undo.RecordObject(bootstrap, $"Apply {bootstrap.SelectedDunePreset} Preset");
                bootstrap.ApplyDunePreset(bootstrap.SelectedDunePreset);
                EditorUtility.SetDirty(bootstrap);
                serializedObject.Update();
            }

            EditorGUILayout.HelpBox(
                "Applying a preset replaces the dune-generation values but preserves the current world seed. You can fine-tune every field afterward.",
                MessageType.None);
        }
    }
}
