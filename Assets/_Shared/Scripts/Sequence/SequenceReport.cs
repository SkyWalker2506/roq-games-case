using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Sequencing
{
    /// <summary>The juice techniques a sequence can report having actually fired.</summary>
    public enum JuiceEvent
    {
        /// <summary>Wind-up before the main move (pull back, dip, compress).</summary>
        Anticipation,
        /// <summary>Volume-preserving deform on a moving or landing object.</summary>
        SquashStretch,
        /// <summary>Motion that passes the target and settles back (Back/Elastic landing).</summary>
        Overshoot,
        /// <summary>Particle burst played on arrival or contact.</summary>
        ImpactVFX,
        /// <summary>Random noise camera shake.</summary>
        CameraShake,
        /// <summary>Directional camera kick.</summary>
        CameraPunch,
        /// <summary>Time-scale freeze on impact.</summary>
        Hitstop,
        /// <summary>Trail or streak left behind a moving object.</summary>
        Trail,
        /// <summary>Mesh/sprite deformation such as peel, curl, bend or shatter.</summary>
        Deform
    }

    /// <summary>One named phase of a sequence, with the time it started and how long it ran.</summary>
    [Serializable]
    public struct SequenceStep
    {
        /// <summary>Phase name, e.g. "flight" or "impact".</summary>
        public string name;
        /// <summary>Seconds from sequence start to the beginning of this step.</summary>
        public float startTime;
        /// <summary>Length of the step in seconds; 0 while it is still open.</summary>
        public float duration;
    }

    /// <summary>A juice technique that fired, with the moment it fired at.</summary>
    [Serializable]
    public struct SequenceJuiceEventRecord
    {
        /// <summary>The <see cref="JuiceEvent"/> name.</summary>
        public string type;
        /// <summary>Seconds from sequence start.</summary>
        public float time;
        /// <summary>Free-form context, e.g. which object it was applied to.</summary>
        public string detail;
    }

    /// <summary>
    /// Evidence log for one interaction sequence: which phases ran, how long each took, and which juice
    /// techniques actually fired. Records events at the moment they are triggered, never "it exists in the
    /// scene". Serializes to JSON so acceptance checks can read it.
    /// </summary>
    [Serializable]
    public class SequenceReport
    {
        /// <summary>Name of the case or sequence this report belongs to.</summary>
        public string caseName = "";
        /// <summary>True once the sequence reached its end without being interrupted.</summary>
        public bool completed;
        /// <summary>Total sequence length in seconds, filled in on completion.</summary>
        public float totalDuration;
        /// <summary>Phases in the order they ran.</summary>
        public List<SequenceStep> steps = new List<SequenceStep>(8);
        /// <summary>Juice techniques that fired, in order.</summary>
        public List<SequenceJuiceEventRecord> events = new List<SequenceJuiceEventRecord>(16);

        int _openStep = -1;

        /// <summary>Clears the report and names it for a fresh run.</summary>
        public void Reset(string sequenceName)
        {
            caseName = sequenceName;
            completed = false;
            totalDuration = 0f;
            steps.Clear();
            events.Clear();
            _openStep = -1;
        }

        /// <summary>Opens a named step at <paramref name="time"/> seconds from sequence start, closing any step still open.</summary>
        public void BeginStep(string stepName, float time)
        {
            EndStep(time);
            steps.Add(new SequenceStep { name = stepName, startTime = time, duration = 0f });
            _openStep = steps.Count - 1;
        }

        /// <summary>Closes the currently open step at <paramref name="time"/> seconds from sequence start.</summary>
        public void EndStep(float time)
        {
            if (_openStep < 0) return;
            SequenceStep step = steps[_openStep];
            step.duration = Mathf.Max(0f, time - step.startTime);
            steps[_openStep] = step;
            _openStep = -1;
        }

        /// <summary>Records that <paramref name="juiceEvent"/> fired at <paramref name="time"/> seconds from sequence start.</summary>
        public void Fire(JuiceEvent juiceEvent, float time, string detail = null)
        {
            events.Add(new SequenceJuiceEventRecord
            {
                type = juiceEvent.ToString(),
                time = time,
                detail = detail ?? ""
            });
        }

        /// <summary>Marks the sequence finished and stamps its total duration.</summary>
        public void Complete(float time)
        {
            EndStep(time);
            totalDuration = time;
            completed = true;
        }

        /// <summary>How many times <paramref name="juiceEvent"/> fired during this run.</summary>
        public int Count(JuiceEvent juiceEvent)
        {
            string key = juiceEvent.ToString();
            int n = 0;
            for (int i = 0; i < events.Count; i++) if (events[i].type == key) n++;
            return n;
        }

        /// <summary>True if <paramref name="juiceEvent"/> fired at least once.</summary>
        public bool Fired(JuiceEvent juiceEvent)
        {
            return Count(juiceEvent) > 0;
        }

        /// <summary>Serializes the whole report to JSON, which is what the acceptance checks read.</summary>
        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }
    }
}
