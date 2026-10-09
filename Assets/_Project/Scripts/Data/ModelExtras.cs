using System;
using UnityEngine;

namespace BuildAR.Data
{
    /// <summary>
    /// A fan on a component's model file. Import Component Models cuts whatever the model has there out of its mesh
    /// (generated models bake a fan into one lump that can't turn cleanly) and builds a real one in its place: a
    /// spinning rotor with proper blades inside a closed fan well, with an RGB ring round it.
    ///
    /// Positions are given in the model file's own space so they survive any turn the importer (or Model Rotation
    /// Euler) gives the part: 0–1 across the mesh's bounds on each axis, as Unity imports it (the FBX's x negated).
    /// Sizes are fractions of the mesh's largest side.
    /// </summary>
    [Serializable]
    public class ModelFan
    {
        [Tooltip("Middle of the fan on the face it sits in, 0–1 across the mesh's bounds on each axis.")]
        public Vector3 center = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("The way the fan faces (out of that face), in the mesh's own axes.")]
        public Vector3 facing = Vector3.up;

        [Tooltip("Radius of the blades, as a fraction of the mesh's largest side.")]
        public float radius = 0.1f;

        [Tooltip("How far behind the face the model's own fan reaches, as a fraction of the mesh's largest side. " +
                 "Everything in that cylinder is cut away; keep it short of whatever sits behind the fan.")]
        public float depth = 0.05f;

        [Tooltip("How far behind the face the fan starts, for a fan mounted behind a wall (a case's rear fan behind " +
                 "its grille). The wall in front is kept.")]
        public float inset;

        [Tooltip("Build the whole fan — square frame and the struts behind the motor — for a fan that stands on its " +
                 "own. Off for a fan in a shroud or on a heatsink, which the model already frames.")]
        public bool frame;

        [Tooltip("Number of blades.")]
        [Range(3, 13)] public int blades = 9;

        [Tooltip("Turning speed. Kept slow enough to see the blades — real fans are too fast to film.")]
        public float rpm = 150f;

        [Tooltip("Put a glowing RGB ring round the fan.")]
        public bool rgbRing = true;
    }

    /// <summary>
    /// An I/O panel Import Component Models builds onto a case model, in the same model-space terms as
    /// <see cref="ModelFan"/>: the case's rear port shield, or the buttons and ports on its top. The panel is laid
    /// on the model's surface where a line along <see cref="facing"/> through <see cref="center"/> meets it, and
    /// scaled so it is <see cref="width"/> wide.
    /// </summary>
    [Serializable]
    public class ModelIoPanel
    {
        public enum Kind
        {
            [Tooltip("The motherboard's rear ports seen through the case's I/O cut-out: a single column, top to bottom.")]
            RearPorts,
            [Tooltip("Power and reset buttons, LEDs, audio and USB, on a bezel.")]
            TopButtons,
        }

        public Kind kind;

        [Tooltip("Middle of the panel, 0–1 across the mesh's bounds on each axis. It needn't be exactly on the surface.")]
        public Vector3 center = new Vector3(0.5f, 0.5f, 1f);

        [Tooltip("The way the panel faces (out of the surface), in the mesh's own axes.")]
        public Vector3 facing = Vector3.forward;

        [Tooltip("Which way is up on the panel: along its height for the rear ports, away from the front for the top buttons.")]
        public Vector3 up = Vector3.right;

        [Tooltip("Width of the panel as a fraction of the mesh's largest side.")]
        public float width = 0.05f;
    }

    /// <summary>
    /// A labeled feature on a component's model, shown as a tappable dot in the 3D viewer and as a row in its Parts
    /// tab. Import Component Models (and BuildAR ▸ Setup ▸ Label Model Parts) turns each into a ModelHotspot on the
    /// model. Same model-space terms as <see cref="ModelFan"/>; measure the spot on the model's surface.
    /// </summary>
    [Serializable]
    public class ModelLabel
    {
        public string title;
        [TextArea] public string description;
        public BuildAR.Viewer.ModelHotspot.Kind kind;

        [Tooltip("The spot on the model's surface, 0–1 across the mesh's bounds on each axis.")]
        public Vector3 center = new Vector3(0.5f, 0.5f, 1f);

        [Tooltip("The way the surface faces there (out of it), in the mesh's own axes. The dot dims when that side " +
                 "is turned away from the camera.")]
        public Vector3 facing = Vector3.up;
    }

    /// <summary>An RGB light bar on a component's model, in the same model-space terms as <see cref="ModelFan"/>.</summary>
    [Serializable]
    public class ModelLightBar
    {
        [Tooltip("Middle of the bar, 0–1 across the mesh's bounds on each axis (a little past 0 or 1 sits it on the outside).")]
        public Vector3 center = new Vector3(0.5f, 0.5f, 1f);

        [Tooltip("Size of the bar as a fraction of the mesh's bounds on each axis.")]
        public Vector3 size = new Vector3(0.8f, 0.5f, 0.05f);
    }
}
