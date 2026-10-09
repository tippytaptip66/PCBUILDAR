package com.pcbuildar.gallery;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.ContentResolver;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Opens the phone's gallery so the learner can choose a photo of a PC part, and hands the file back to Unity.
 *
 * It runs as an invisible fragment attached to Unity's activity, which is what lets it receive the result without
 * the app declaring an extra activity in its manifest. On Android 13 and later it opens the system photo picker
 * (the learner's photos and albums); before that, the usual "choose a photo" chooser. Neither needs a storage
 * permission: the user grants access to the one picture they choose.
 *
 * A content:// URI can't be read by Unity, so the picture is decoded here and saved into the app's cache. On the
 * way it is shrunk to at most MAX_SIDE pixels (phone photos are far bigger than the scanner can use), turned the
 * right way up (phones store most photos sideways and note the turn in EXIF, which Unity ignores), and written as
 * a JPEG, which also makes HEIC and WebP photos readable by Unity.
 */
public class GalleryPickerFragment extends Fragment {

    private static final int REQUEST_CODE = 7411;
    private static final int MAX_SIDE = 1600;
    private static final String PICK_IMAGES = "android.provider.action.PICK_IMAGES";   // MediaStore, API 33

    private String listenerObject;
    private String listenerMethod;

    /** Called from C#. listenerObject is a GameObject name, listenerMethod one of its methods. */
    public static void pick(final String listenerObject, final String listenerMethod) {
        final Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                GalleryPickerFragment fragment = new GalleryPickerFragment();
                fragment.listenerObject = listenerObject;
                fragment.listenerMethod = listenerMethod;
                activity.getFragmentManager()
                        .beginTransaction()
                        .add(fragment, "BuildARGalleryPicker")
                        .commitAllowingStateLoss();
            }
        });
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (Build.VERSION.SDK_INT >= 33) {
            try {
                Intent photos = new Intent(PICK_IMAGES);
                photos.setType("image/*");
                startActivityForResult(photos, REQUEST_CODE);
                return;
            } catch (ActivityNotFoundException ignored) {
                // No photo picker on this build of Android: fall through to the chooser.
            }
        }
        Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        intent.setType("image/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        startActivityForResult(Intent.createChooser(intent, "Choose a photo"), REQUEST_CODE);
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_CODE) return;

        String path = "";
        try {
            if (resultCode == Activity.RESULT_OK && data != null && data.getData() != null) {
                path = saveToCache(data.getData());
            }
        } catch (Throwable e) {   // OutOfMemoryError included: a failed pick must not take the app down
            path = "";
        }

        UnityPlayer.UnitySendMessage(listenerObject, listenerMethod, path == null ? "" : path);

        try {
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        } catch (Exception ignored) {
        }
    }

    /** Decodes the picture small, turns it upright and saves it as a JPEG in the cache. */
    private String saveToCache(Uri uri) throws Exception {
        ContentResolver resolver = getActivity().getContentResolver();

        // Measure first, so a 50-megapixel photo is decoded at a fraction of its size instead of filling memory.
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream in = resolver.openInputStream(uri)) {
            BitmapFactory.decodeStream(in, null, bounds);
        }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return "";

        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = 1;
        while (Math.max(bounds.outWidth, bounds.outHeight) / (options.inSampleSize * 2) >= MAX_SIDE) {
            options.inSampleSize *= 2;
        }
        Bitmap bitmap;
        try (InputStream in = resolver.openInputStream(uri)) {
            bitmap = BitmapFactory.decodeStream(in, null, options);
        }
        if (bitmap == null) return "";

        int orientation = ExifInterface.ORIENTATION_NORMAL;
        try (InputStream in = resolver.openInputStream(uri)) {
            orientation = new ExifInterface(in).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL);
        } catch (Exception ignored) {
            // No EXIF (a PNG or a screenshot): keep it as stored.
        }

        Matrix matrix = orientationMatrix(orientation);
        float scale = Math.min(1f, (float) MAX_SIDE / Math.max(bitmap.getWidth(), bitmap.getHeight()));
        if (scale < 1f) matrix.postScale(scale, scale);
        if (!matrix.isIdentity()) {
            Bitmap upright = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), matrix, true);
            if (upright != bitmap) bitmap.recycle();
            bitmap = upright;
        }

        File cache = getActivity().getCacheDir();
        removeOldPicks(cache);
        File target = new File(cache, "picked_" + System.currentTimeMillis() + ".jpg");
        try (OutputStream out = new FileOutputStream(target)) {
            bitmap.compress(Bitmap.CompressFormat.JPEG, 92, out);
        } finally {
            bitmap.recycle();
        }
        return target.getAbsolutePath();
    }

    /** The turn (and mirror) that shows a picture stored with this EXIF orientation the right way up. */
    private static Matrix orientationMatrix(int orientation) {
        Matrix m = new Matrix();
        switch (orientation) {
            case ExifInterface.ORIENTATION_FLIP_HORIZONTAL: m.setScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_180: m.setRotate(180); break;
            case ExifInterface.ORIENTATION_FLIP_VERTICAL: m.setScale(1, -1); break;
            case ExifInterface.ORIENTATION_TRANSPOSE: m.setRotate(90); m.postScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_90: m.setRotate(90); break;
            case ExifInterface.ORIENTATION_TRANSVERSE: m.setRotate(-90); m.postScale(-1, 1); break;
            case ExifInterface.ORIENTATION_ROTATE_270: m.setRotate(-90); break;
            default: break;
        }
        return m;
    }

    /** Earlier picks are only needed while they are on screen; don't let them pile up in the cache. */
    private static void removeOldPicks(File cache) {
        File[] files = cache.listFiles();
        if (files == null) return;
        for (File f : files) {
            if (f.getName().startsWith("picked_")) {
                //noinspection ResultOfMethodCallIgnored
                f.delete();
            }
        }
    }
}
