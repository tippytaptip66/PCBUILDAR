using System;
using UnityEngine;

namespace BuildAR.AR
{
    /// <summary>
    /// Opens the phone's own photo picker (see Plugins/Android/GalleryPickerFragment.java) and returns the file
    /// the learner chose. Android's picker grants access to just that one file, so the app needs no storage
    /// permission and never sees the rest of the gallery.
    ///
    /// The copy it hands back is already shrunk and turned the right way up, and saved as a JPEG, so photos the
    /// phone keeps as HEIC or WebP open too.
    ///
    /// In the Editor an ordinary file dialog stands in for the gallery, so the photo scan can be tried in Play
    /// mode. Anywhere else this reports unavailable, and the scanner falls back to the pictures it can list from
    /// folders.
    /// </summary>
    public static class NativeGallery
    {
        const string JavaClass = "com.pcbuildar.gallery.GalleryPickerFragment";
        const string ListenerObject = "BuildARGalleryListener";
        const string ListenerMethod = "OnPhotoPicked";

        public static bool IsSupported =>
#if UNITY_EDITOR || UNITY_ANDROID
            true;
#else
            false;
#endif

        static Action<string> _pending;
#if UNITY_EDITOR
        static string _lastFolder;
#endif

        /// <summary>
        /// Shows the picker. The callback gets the path of a copy in the app's cache (the chosen file itself in
        /// the Editor), or null if the learner backed out or the picker isn't usable.
        /// </summary>
        public static void PickImage(Action<string> onPicked)
        {
            if (!IsSupported) { onPicked?.Invoke(null); return; }

#if UNITY_EDITOR
            string folder = _lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Choose a photo of a PC part", folder,
                new[] { "Images", "jpg,jpeg,png", "All files", "*" });
            if (!string.IsNullOrEmpty(path)) _lastFolder = System.IO.Path.GetDirectoryName(path);
            onPicked?.Invoke(string.IsNullOrEmpty(path) ? null : path);
#else
            _pending = onPicked;
            try
            {
                var listener = GameObject.Find(ListenerObject) ?? new GameObject(ListenerObject);
                if (listener.GetComponent<GalleryListener>() == null) listener.AddComponent<GalleryListener>();
                UnityEngine.Object.DontDestroyOnLoad(listener);

                using (var picker = new AndroidJavaClass(JavaClass))
                    picker.CallStatic("pick", ListenerObject, ListenerMethod);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BuildAR: the phone's photo picker isn't available. {e.Message}");
                Deliver(null);
            }
#endif
        }

        internal static void Deliver(string path)
        {
            var callback = _pending;
            _pending = null;
            callback?.Invoke(string.IsNullOrEmpty(path) ? null : path);
        }

        /// <summary>Receives the result from the Java side, which addresses it by GameObject name.</summary>
        class GalleryListener : MonoBehaviour
        {
            // ReSharper disable once UnusedMember.Local — called from Java by name.
            public void OnPhotoPicked(string path) => Deliver(path);
        }
    }
}
