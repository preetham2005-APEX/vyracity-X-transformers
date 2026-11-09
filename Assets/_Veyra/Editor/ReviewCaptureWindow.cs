using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using VeyraCity.Testing;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S5 window "Veyra/Review/Capture": captures the review cameras of Assets/_Veyra/Data/ReviewCameras.asset from the
    /// open scene (Edit mode, or the runtime path in Play mode) into Screenshots/&lt;date&gt;_&lt;card&gt;/ with a 6 x 3 contact sheet.
    /// </summary>
    public sealed class ReviewCaptureWindow : EditorWindow
    {
        string card = "S5";
        string filter = "";
        string status = "";
        Vector2 scroll;

        const string CardPref = "VeyraCity.Review.Card";

        [MenuItem("Veyra/Review/Capture")]
        public static void Open() => GetWindow<ReviewCaptureWindow>("Review Capture");

        /// <summary>One click: all 18 cameras of the active scene, labelled with the card last typed in the window (default "review").</summary>
        [MenuItem("Veyra/Review/Capture All Cameras Now")]
        public static void CaptureAllNow() => UnityEngine.Debug.Log("[ReviewCapture] " + ReviewCaptureRunner.Start(EditorPrefs.GetString(CardPref, "review")));

        void OnEnable() => card = EditorPrefs.GetString(CardPref, card);

        void OnGUI()
        {
            EditorGUILayout.LabelField("Review cameras (card S5)", EditorStyles.boldLabel);
            var set = AssetDatabase.LoadAssetAtPath<ReviewCameraSet>(ReviewCameraTools.AssetPath);
            EditorGUILayout.LabelField("Camera set", set != null ? $"{ReviewCameraTools.AssetPath} ({set.Cameras.Count} cameras)" : "missing — create it below");
            EditorGUILayout.LabelField("Scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + (EditorApplication.isPlaying ? "  (Play mode: runtime capture)" : "  (Edit mode)"));
            string newCard = EditorGUILayout.TextField(new GUIContent("Card", "Output goes to Screenshots/<date>_<card>[_<scene>]"), card);
            if (newCard != card) { card = newCard; EditorPrefs.SetString(CardPref, card); }
            filter = EditorGUILayout.TextField(new GUIContent("Filter", "Comma-separated parts of camera names; empty = all 18"), filter);
            using (new EditorGUI.DisabledScope(ReviewCaptureRunner.IsRunning))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create / update cameras")) status = ReviewCameraTools.CreateAll();
                    if (GUILayout.Button(string.IsNullOrWhiteSpace(filter) ? "Capture all" : "Capture filtered")) status = ReviewCaptureRunner.Start(card, filter);
                }
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(ReviewCaptureRunner.LastOutputDir) || !Directory.Exists(ReviewCaptureRunner.LastOutputDir)))
                if (GUILayout.Button("Open output folder")) Process.Start(new ProcessStartInfo { FileName = ReviewCaptureRunner.LastOutputDir, UseShellExecute = true });
            EditorGUILayout.HelpBox(ReviewCaptureRunner.IsRunning ? "Capturing…" : status, MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(ReviewCaptureRunner.LastReport, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Update()
        {
            if (ReviewCaptureRunner.IsRunning) Repaint();
        }
    }
}
