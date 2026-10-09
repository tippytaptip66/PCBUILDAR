using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildAR.AR.Detection
{
    /// <summary>
    /// One region of a still picture made ready for a detector: copied out whole at its own shape, padded square
    /// when the model wants a square, and able to map the boxes found in it back onto the whole picture.
    ///
    /// Camera frames are centre-cropped square, which is fine when the part is held up in the middle of the view.
    /// A photo is framed however the learner took it, so cropping it the same way cut the sides off a landscape
    /// shot before the model ever saw them. Padding keeps the whole region in view instead.
    /// </summary>
    public sealed class StillFrame : IDisposable
    {
        /// <summary>The padding colour Ultralytics letterboxes its training pictures with (114, 114, 114).</summary>
        static readonly Color32 Pad = new Color32(114, 114, 114, 255);

        /// <summary>The pixels to hand to the model.</summary>
        public Texture2D Texture { get; private set; }

        readonly Rect _region;    // the part of the picture that was copied, 0..1 with the origin top-left
        readonly Rect _content;   // where that part sits inside Texture, 0..1 with the origin top-left

        StillFrame(Texture2D texture, Rect region, Rect content)
        {
            Texture = texture;
            _region = region;
            _content = content;
        }

        /// <summary>
        /// Copies <paramref name="region"/> of the picture (0..1, origin top-left) scaled so its longer side is
        /// <paramref name="size"/> pixels. With <paramref name="square"/> it is centred in a grey square of that size.
        /// </summary>
        public static StillFrame Read(Texture source, Rect region, int size, bool square)
        {
            region = Clamp(region);
            float cw = region.width * source.width, ch = region.height * source.height;
            float k = size / Mathf.Max(1f, Mathf.Max(cw, ch));
            int w = Mathf.Clamp(Mathf.RoundToInt(cw * k), 1, size), h = Mathf.Clamp(Mathf.RoundToInt(ch * k), 1, size);

            // A blit samples the source by UV, which runs bottom-up, so the region's top edge is 1 - yMax.
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target, new Vector2(region.width, region.height), new Vector2(region.x, 1f - region.yMax));
            var pixels = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            if (!square || w == h)
            {
                pixels.Apply(false);
                return new StillFrame(pixels, region, new Rect(0f, 0f, 1f, 1f));
            }

            int side = Mathf.Max(w, h), padX = (side - w) / 2, padY = (side - h) / 2;
            var padded = new Color32[side * side];
            for (int i = 0; i < padded.Length; i++) padded[i] = Pad;
            var rows = pixels.GetPixels32();
            for (int y = 0; y < h; y++) Array.Copy(rows, y * w, padded, (y + padY) * side + padX, w);
            Object.Destroy(pixels);

            var frame = new Texture2D(side, side, TextureFormat.RGBA32, false);
            frame.SetPixels32(padded);
            frame.Apply(false);

            // Texture rows run bottom-up: padY rows of padding sit under the picture, the rest above it.
            float top = side - padY - h;
            return new StillFrame(frame, region, new Rect((float)padX / side, top / side, (float)w / side, (float)h / side));
        }

        /// <summary>A box found in <see cref="Texture"/> (0..1, top-left) as a box in the whole picture.</summary>
        public Rect ToPicture(Rect box)
        {
            Vector2 min = ToPicture(new Vector2(box.xMin, box.yMin)), max = ToPicture(new Vector2(box.xMax, box.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        Vector2 ToPicture(Vector2 p)
        {
            // Clamped, so a box that spills into the padding stops at the edge of the picture.
            float u = Mathf.Clamp01((p.x - _content.x) / _content.width);
            float v = Mathf.Clamp01((p.y - _content.y) / _content.height);
            return new Vector2(_region.x + u * _region.width, _region.y + v * _region.height);
        }

        static Rect Clamp(Rect r)
        {
            float x = Mathf.Clamp01(r.xMin), y = Mathf.Clamp01(r.yMin);
            float x2 = Mathf.Clamp(r.xMax, x, 1f), y2 = Mathf.Clamp(r.yMax, y, 1f);
            return x2 - x < 0.001f || y2 - y < 0.001f ? new Rect(0f, 0f, 1f, 1f) : Rect.MinMaxRect(x, y, x2, y2);
        }

        public void Dispose()
        {
            if (Texture != null) Object.Destroy(Texture);
            Texture = null;
        }
    }
}
