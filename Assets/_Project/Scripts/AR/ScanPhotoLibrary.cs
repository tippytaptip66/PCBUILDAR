using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BuildAR.AR
{
    /// <summary>
    /// The pictures the scanner can run on instead of the live camera: samples shipped with the app, and any
    /// photos the learner has put on the device.
    ///
    /// Where to put your own:
    ///   • before building — drop .jpg/.png files into Assets/_Project/Resources/ScanSamples/ and they travel
    ///     inside the app;
    ///   • on the phone — copy them over USB into Android/data/com.pcbuildar.app/files/photos/, which needs no
    ///     storage permission.
    /// The public Pictures and DCIM folders are tried too, and simply skipped when Android won't allow it.
    /// </summary>
    public static class ScanPhotoLibrary
    {
        public const string SamplesResource = "ScanSamples";

        public struct Entry
        {
            public string name;
            /// <summary>Set for a file on disk; null for a sample inside the app.</summary>
            public string path;
            /// <summary>Set for a sample inside the app; null for a file on disk.</summary>
            public Texture2D bundled;
        }

        static readonly string[] Extensions = { ".jpg", ".jpeg", ".png" };

        /// <summary>Everything available, samples first.</summary>
        public static List<Entry> All()
        {
            var entries = new List<Entry>();

            foreach (var texture in Resources.LoadAll<Texture2D>(SamplesResource))
                if (texture != null) entries.Add(new Entry { name = texture.name, bundled = texture });

            foreach (var folder in Folders())
            {
                string[] files;
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    files = Directory.GetFiles(folder);
                }
                catch (Exception) { continue; }   // Android may refuse the folder; that's fine

                Array.Sort(files);
                foreach (var file in files)
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, extension) < 0) continue;
                    entries.Add(new Entry { name = Path.GetFileNameWithoutExtension(file), path = file });
                }
            }
            return entries;
        }

        /// <summary>The app's own folder first — it is the one that always works without a permission.</summary>
        static IEnumerable<string> Folders()
        {
            yield return Path.Combine(Application.persistentDataPath, "photos");
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return "/storage/emulated/0/Pictures/PCBuildAR";
            yield return "/storage/emulated/0/DCIM/PCBuildAR";
#endif
        }

        /// <summary>
        /// Longest side a photo is kept at. A phone photo is often 4000 pixels or more across: far more than the
        /// model (640) or the screen can use, and 50 MB or so of memory each.
        /// </summary>
        public const int MaxPhotoSide = 1600;

        /// <summary>
        /// Reads an entry into a texture, turned the right way up and shrunk to <see cref="MaxPhotoSide"/>.
        /// Returns null when the file can't be read. A file on disk gives a new texture the caller owns; a sample
        /// inside the app is returned as it is.
        /// </summary>
        public static Texture2D Load(Entry entry)
        {
            if (entry.bundled != null) return entry.bundled;
            try
            {
                if (string.IsNullOrEmpty(entry.path) || !File.Exists(entry.path)) return null;
                byte[] bytes = File.ReadAllBytes(entry.path);
                var original = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!original.LoadImage(bytes)) { UnityEngine.Object.Destroy(original); return null; }

                var texture = original;
                float scale = (float)MaxPhotoSide / Mathf.Max(original.width, original.height);
                if (scale < 1f)
                    texture = Shrink(original, Mathf.Max(1, Mathf.RoundToInt(original.width * scale)),
                                               Mathf.Max(1, Mathf.RoundToInt(original.height * scale)));

                // Phones store most photos sideways and note the turn in the EXIF data, which Unity ignores.
                int orientation = JpegOrientation(bytes);
                if (orientation > 1)
                {
                    var upright = Reorient(texture, orientation);
                    if (texture != original) UnityEngine.Object.Destroy(texture);
                    texture = upright;
                }

                if (texture != original) UnityEngine.Object.Destroy(original);
                texture.name = entry.name;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"BuildAR: couldn't open '{entry.path}'. {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Scales a picture down on the GPU, halving it until one last step reaches the size. A single big jump
        /// skips most of the pixels and leaves edges jagged; halving averages them.
        /// </summary>
        static Texture2D Shrink(Texture2D source, int width, int height)
        {
            Texture current = source;
            RenderTexture step = null;
            int w = source.width, h = source.height;
            while (w / 2 >= width && h / 2 >= height)
            {
                w /= 2; h /= 2;
                var next = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(current, next);
                if (step != null) RenderTexture.ReleaseTemporary(step);
                current = step = next;
            }

            var last = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(current, last);
            if (step != null) RenderTexture.ReleaseTemporary(step);

            var result = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            RenderTexture.active = last;
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            result.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(last);
            return result;
        }

        /// <summary>A copy of the picture turned (and mirrored, for 2, 4, 5 and 7) as its EXIF orientation says.</summary>
        static Texture2D Reorient(Texture2D source, int orientation)
        {
            int w = source.width, h = source.height;
            bool turned = orientation >= 5;
            int ow = turned ? h : w, oh = turned ? w : h;
            var from = source.GetPixels32();
            var to = new Color32[from.Length];

            // x, y: a pixel of the upright picture; sx, sy: where it is in the stored one. Both top-left origin.
            for (int y = 0; y < oh; y++)
            {
                for (int x = 0; x < ow; x++)
                {
                    int sx, sy;
                    switch (orientation)
                    {
                        case 2: sx = w - 1 - x; sy = y; break;            // mirrored
                        case 3: sx = w - 1 - x; sy = h - 1 - y; break;    // upside down
                        case 4: sx = x; sy = h - 1 - y; break;            // flipped
                        case 5: sx = y; sy = x; break;                    // mirrored and turned
                        case 6: sx = y; sy = h - 1 - x; break;            // needs a quarter turn clockwise
                        case 7: sx = w - 1 - y; sy = h - 1 - x; break;    // mirrored and turned the other way
                        case 8: sx = w - 1 - y; sy = x; break;            // needs a quarter turn anticlockwise
                        default: sx = x; sy = y; break;
                    }
                    // Texture rows run bottom-up.
                    to[(oh - 1 - y) * ow + x] = from[(h - 1 - sy) * w + sx];
                }
            }

            var result = new Texture2D(ow, oh, TextureFormat.RGB24, false);
            result.SetPixels32(to);
            result.Apply(false);
            return result;
        }

        /// <summary>The EXIF orientation of a JPEG (1 to 8, 1 being upright), or 1 when it has none.</summary>
        static int JpegOrientation(byte[] data)
        {
            try
            {
                if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return 1;
                int i = 2;
                while (i + 4 <= data.Length && data[i] == 0xFF)
                {
                    int marker = data[i + 1];
                    if (marker == 0xFF) { i++; continue; }              // padding before a marker
                    if (marker == 0xDA || marker == 0xD9) return 1;     // image data: EXIF always comes before it
                    int length = (data[i + 2] << 8) | data[i + 3];
                    if (marker == 0xE1 && length >= 16 && i + 2 + length <= data.Length &&
                        data[i + 4] == 'E' && data[i + 5] == 'x' && data[i + 6] == 'i' && data[i + 7] == 'f')
                        return TiffOrientation(data, i + 10, i + 2 + length);
                    i += 2 + length;
                }
            }
            catch (IndexOutOfRangeException) { }
            return 1;
        }

        /// <summary>Finds the orientation tag (0x0112) in the first directory of an EXIF block.</summary>
        static int TiffOrientation(byte[] d, int tiff, int end)
        {
            bool little = d[tiff] == 'I';
            int U16(int at) => little ? d[at] | d[at + 1] << 8 : d[at] << 8 | d[at + 1];
            int U32(int at) => little ? U16(at) | U16(at + 2) << 16 : U16(at) << 16 | U16(at + 2);

            int directory = tiff + U32(tiff + 4);
            if (directory < tiff || directory + 2 > end) return 1;
            int count = U16(directory);
            for (int n = 0; n < count; n++)
            {
                int entry = directory + 2 + n * 12;
                if (entry + 12 > end) break;
                if (U16(entry) != 0x0112) continue;
                int value = U16(entry + 8);
                return value >= 1 && value <= 8 ? value : 1;
            }
            return 1;
        }

        /// <summary>Makes sure the drop-in folder exists, so it shows up over USB.</summary>
        public static string EnsurePhotoFolder()
        {
            string folder = Path.Combine(Application.persistentDataPath, "photos");
            try { Directory.CreateDirectory(folder); } catch (Exception) { }
            return folder;
        }
    }
}
