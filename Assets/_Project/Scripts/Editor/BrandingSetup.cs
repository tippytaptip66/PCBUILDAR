using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildAR.EditorTools
{
    /// <summary>
    /// Uses Art/Branding/Resources/BuildAR_AppIcon.png as the app icon (default + Android legacy, round and adaptive)
    /// and on the splash screen. Runs automatically when that PNG is added or changed.
    /// </summary>
    public class BrandingSetup : AssetPostprocessor
    {
        /// <summary>Name under the app icon (Player Settings > Product Name). Change it here, not in Player Settings.</summary>
        public const string AppName = "PCBuildAR";
        /// <summary>Android package name. Keep it fixed once installed on phones, or updates install as a separate app.</summary>
        public const string AndroidPackage = "com.pcbuildar.app";
        public const string IconPath = "Assets/_Project/Art/Branding/Resources/BuildAR_AppIcon.png";

        [InitializeOnLoadMethod]
        static void ApplyAppName()
        {
            EditorApplication.delayCall += () =>
            {
                string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                if (string.IsNullOrEmpty(id) || id.Contains("unity.template"))
                {
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackage);
                    Debug.Log($"BuildAR: Android package name set to {AndroidPackage}.");
                }
                if (PlayerSettings.productName == AppName) return;
                PlayerSettings.productName = AppName;
                AssetDatabase.SaveAssets();
                Debug.Log($"BuildAR: product name set to {AppName}.");
            };
        }
        const string EmptyForegroundPath = "Assets/_Project/Art/Branding/AppIcon_EmptyForeground.png";
        static readonly Color SplashBackground = new Color32(0x05, 0x13, 0x32, 0xFF);

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith("Assets/_Project/Resources/Icons/") || assetPath.StartsWith("Assets/_Project/Resources/Avatars/")
                || assetPath == "Assets/_Project/Art/Branding/Resources/BuildAR_Logo.png")
            {
                var ui = (TextureImporter)assetImporter;
                ui.textureType = TextureImporterType.Default;
                ui.alphaIsTransparency = true;
                ui.maxTextureSize = 256;
                ui.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (assetPath != IconPath) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        /// <summary>
        /// UI sounds are tiny and must fire without delay, so they stay decompressed in memory. The music loop is
        /// long, so it stays compressed instead (still in memory, which keeps the loop seamless on Android).
        /// </summary>
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/Audio/")) return;
            bool music = Path.GetFileNameWithoutExtension(assetPath).StartsWith("bgmusic");
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.45f : 0.7f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Contains(IconPath) || moved.Contains(IconPath)) EditorApplication.delayCall += ApplyIcon;
        }

        [MenuItem("BuildAR/Setup/Apply App Icon + Splash", priority = 25)]
        public static void ApplyIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"BuildAR: save your logo as {IconPath} (square PNG, 512 px or larger) first.");
                return;
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            var android = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(android))
            {
                var slots = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var slot in slots)
                {
                    // Adaptive icons: the logo (with its gradient) is the background layer, the foreground stays empty.
                    if (slot.maxLayerCount >= 2) slot.SetTextures(icon, EmptyForeground());
                    else slot.SetTexture(icon);
                }
                PlayerSettings.SetPlatformIcons(android, kind, slots);
            }

            PlayerSettings.SplashScreen.backgroundColor = SplashBackground;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            if (sprite != null) PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sprite) };

            AssetDatabase.SaveAssets();
            Debug.Log("BuildAR: app icon and splash screen updated from " + IconPath);
        }

        static Texture2D EmptyForeground()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(EmptyForegroundPath);
            if (existing != null) return existing;
            var tex = new Texture2D(432, 432, TextureFormat.RGBA32, false);
            tex.SetPixels32(new Color32[432 * 432]);
            File.WriteAllBytes(EmptyForegroundPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(EmptyForegroundPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(EmptyForegroundPath);
        }
    }
}
