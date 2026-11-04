using System.IO;
using UnityEditor;
using UnityEngine;

namespace CharacterLab.EditorTools
{
    /// <summary>
    /// Editor helpers that come with your Character Lab player:
    ///  - If your model is a .glb/.gltf file, installs Unity's glTFast package
    ///    (needed to import those files) the first time the project opens.
    ///  - GameObject > Character Lab > Test Ground: a big floor + sun to try your player on.
    ///  - GameObject > Character Lab > Bounce Pad / Moving Platform / Grapple Point.
    /// </summary>
    [InitializeOnLoad]
    static class CharacterLabEditor
    {
        static CharacterLabEditor()
        {
            EditorApplication.delayCall += CheckGltf;
        }

        static void CheckGltf()
        {
            if (System.Type.GetType("GLTFast.GltfImport, glTFast") != null) return;
            if (SessionState.GetBool("CharacterLab.gltfRequested", false)) return;
            if (!HasGltfModel()) return;
            SessionState.SetBool("CharacterLab.gltfRequested", true);
            string package = "com.unity.cloud.gltfast@" + GltfVersion();
            Debug.Log("[Character Lab] Your model is a .glb/.gltf file, so the glTFast package (" + package + ") is being installed to import it. This takes a minute - the model appears when it's done.");
            UnityEditor.PackageManager.Client.Add(package);
        }

        /// <summary>Is there a .glb/.gltf in any .../Resources/CharacterLab/Models folder? Not only
        /// Assets/CharacterLab: Windows' "Extract All" wraps the zip in a CharacterLab_name folder, and
        /// that whole folder often gets dragged into Assets (Resources folders work anywhere).</summary>
        static bool HasGltfModel()
        {
            try
            {
                foreach (var dir in Directory.GetDirectories("Assets", "Models", SearchOption.AllDirectories))
                {
                    if (!dir.Replace('\\', '/').EndsWith("/Resources/CharacterLab/Models")) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".glb" || ext == ".gltf") return true;
                    }
                }
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException) { }
            return false;
        }

        /// <summary>glTFast 6.15 and newer call Texture2D.LoadImage(ReadOnlySpan) on every Unity 6, but
        /// early Unity 6.0 patches (and 2022.3) don't have it, so those versions wouldn't compile there
        /// (and the Package Manager offers them anyway). 6.14.1 is the newest that works everywhere.</summary>
        static string GltfVersion()
        {
            var span = typeof(System.ReadOnlySpan<byte>);
            bool hasSpanLoad = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), span, typeof(bool) }) != null;
            return hasSpanLoad ? "6.20.0" : "6.14.1";
        }

        [MenuItem("GameObject/Character Lab/Test Ground", false, 10)]
        static void TestGround()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Test Ground";
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.transform.localScale = new Vector3(80, 1, 80);
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                sun.shadows = LightShadows.Soft;
            }
            Undo.RegisterCreatedObjectUndo(floor, "Test Ground");
            Selection.activeGameObject = floor;
        }

        [MenuItem("GameObject/Character Lab/Bounce Pad", false, 11)]
        static void BouncePad()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Bounce Pad";
            pad.transform.localScale = new Vector3(2.5f, 0.15f, 2.5f);
            pad.AddComponent<CharacterLabBouncePad>().launchHeight = 6f;
            Place(pad);
        }

        [MenuItem("GameObject/Character Lab/Moving Platform", false, 12)]
        static void MovingPlatform()
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Moving Platform";
            p.transform.localScale = new Vector3(4, 0.5f, 4);
            p.AddComponent<CharacterLabMovingPlatform>();
            Place(p);
        }

        [MenuItem("GameObject/Character Lab/Grapple Point", false, 13)]
        static void GrapplePoint()
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = "Grapple Point";
            p.AddComponent<CharacterLabGrapplePoint>();
            Place(p);
            p.transform.position += Vector3.up * 6f;
        }

        static void Place(GameObject go)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) go.transform.position = view.pivot;
            Undo.RegisterCreatedObjectUndo(go, go.name);
            Selection.activeGameObject = go;
        }
    }

    /// <summary>
    /// A second Character Lab player that lands in its OWN folder (Windows' "Extract All" makes
    /// CharacterLab_fox next to CharacterLab_rat) brings a second copy of the shared scripts, and two
    /// copies don't compile. Unity still runs this (already compiled) copy while it imports the new
    /// files, so it merges the scripts into one copy: the one with the original ids, which every
    /// character prefab points at, keeping the newer text. The new character's prefab, model and
    /// sounds stay where they are (Resources folders work anywhere).
    /// </summary>
    class CharacterLabDuplicateScripts : AssetPostprocessor
    {
        const string PlayerScriptGuid = "f248e098c0da77741984ad39945ec8fd";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.EndsWith("/Runtime/CharacterLabPlayer.cs")) { Merge(); return; }
        }

        static void Merge()
        {
            var roots = new System.Collections.Generic.List<string>();
            foreach (var f in Directory.GetFiles("Assets", "CharacterLabPlayer.cs", SearchOption.AllDirectories))
            {
                string p = f.Replace('\\', '/');
                if (p.EndsWith("/Runtime/CharacterLabPlayer.cs")) roots.Add(p.Substring(0, p.Length - "/Runtime/CharacterLabPlayer.cs".Length));
            }
            if (roots.Count < 2) return;
            string keep = roots.Find(r => AssetDatabase.AssetPathToGUID(r + "/Runtime/CharacterLabPlayer.cs") == PlayerScriptGuid) ?? roots[0];
            foreach (var other in roots)
            {
                if (other == keep) continue;
                foreach (var sub in new[] { "Runtime", "Editor" })
                {
                    string from = other + "/" + sub;
                    if (!Directory.Exists(from)) continue;
                    foreach (var f in Directory.GetFiles(from, "*.cs"))
                    {
                        string src = f.Replace('\\', '/');
                        string dst = keep + "/" + sub + "/" + Path.GetFileName(src);
                        if (!File.Exists(dst)) { AssetDatabase.MoveAsset(src, dst); continue; }   // a script only the newer copy has
                        if (File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst)) File.Copy(src, dst, true);
                        AssetDatabase.DeleteAsset(src);
                    }
                    if (Directory.GetFileSystemEntries(from).Length == 0) AssetDatabase.DeleteAsset(from);
                }
                Debug.Log("[Character Lab] \"" + other + "\" had its own copy of the Character Lab scripts (two copies can't compile), "
                          + "so it now uses the ones in \"" + keep + "\". All your characters are still there.");
            }
            AssetDatabase.Refresh();
        }
    }
}
