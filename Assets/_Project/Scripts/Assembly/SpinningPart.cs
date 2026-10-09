using UnityEngine;

namespace BuildAR.Assembly
{
    /// <summary>
    /// Turns a part at a steady speed — fan blades, mostly. Added by the model generator to the blade group of
    /// every fan, so they spin wherever the part appears: the 3D viewer, the scanner and the PC builder. In the PC
    /// builder they stand still until the finished PC is switched on, then spin up; switched off, they coast to a stop.
    /// </summary>
    public class SpinningPart : MonoBehaviour
    {
        [Tooltip("Revolutions per minute. Real case fans idle around 800–1200.")]
        public float rpm = 420f;
        [Tooltip("Axis to turn around, in the part's own space.")]
        public Vector3 axis = Vector3.forward;

        [Tooltip("Most it may turn in one frame (0 = no limit). Keep it under half the gap between blades, or on a " +
                 "slow frame rate the blades seem to stand still or run backwards, like wagon wheels in a film.")]
        public float maxDegreesPerFrame;

        const float SpinUpSeconds = 1.5f, SpinDownSeconds = 3f;

        float _speed = -1f;   // share of full speed; the first frame starts it where it should be, without a run-up

        void Update()
        {
            bool powered = AssemblyManager.HasPower;
            float target = powered ? 1f : 0f;
            _speed = _speed < 0f ? target : Mathf.MoveTowards(_speed, target, Time.deltaTime / (powered ? SpinUpSeconds : SpinDownSeconds));
            if (_speed <= 0f) return;

            float step = rpm * 6f * Time.deltaTime * Mathf.SmoothStep(0f, 1f, _speed);   // rpm → degrees per second
            if (maxDegreesPerFrame > 0f) step = Mathf.Clamp(step, -maxDegreesPerFrame, maxDegreesPerFrame);
            transform.Rotate(axis, step, Space.Self);
        }
    }
}
