using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;


namespace VRSYS.PupilLabs
{


    /// <summary>
    /// Plays back a gaze CSV written by GazeDataLogger against a target transform in real time,
    /// snapping to the latest sample at or before the current playback time (no interpolation).
    /// Fully self-contained: playback starts from this file's own first sample and stops itself once
    /// it runs past the last one, with no duration/end time exposed for external synchronization -
    /// stop and restart the scene manually if you need to replay it.
    /// </summary>
    public class GazeSamplePlayer : MonoBehaviour
    {
        private struct Sample
        {
            public double Timestamp;
            public Vector3? CalibratedGazeDirection;
            public Vector3? CalibratedGazeOrigin;
        }

        [SerializeField] private Transform target;
        [SerializeField] public float playbackSpeed = 1f;
        [SerializeField] public bool playOnStart = true;

        [Tooltip("Gaze CSV written by GazeDataLogger.StartRecording, relative to Application.persistentDataPath.")]
        [SerializeField] public string csvPath;

        private readonly List<Sample> samples = new();
        private int sampleIndex;
        private double playbackOrigin;
        private double playbackElapsed;

        public bool IsLoaded { get; private set; }
        public bool IsPlaying { get; private set; }

        private void Start()
        {
            if (playOnStart)
            {
                Load();
                Play();
            }
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                return;
            }

            playbackElapsed += Time.deltaTime * playbackSpeed;
            double playbackTime = playbackOrigin + playbackElapsed;

            int lastApplied = -1;
            while (sampleIndex < samples.Count && samples[sampleIndex].Timestamp <= playbackTime)
            {
                if (samples[sampleIndex].CalibratedGazeDirection.HasValue)
                {
                    lastApplied = sampleIndex;
                }

                sampleIndex++;
            }

            if (lastApplied >= 0 && target != null)
            {
                // Gaze values are local to the head/camera transform.
                target.localPosition = samples[lastApplied].CalibratedGazeOrigin!.Value;
                target.localRotation = Quaternion.LookRotation(samples[lastApplied].CalibratedGazeDirection!.Value);
            }

            if (sampleIndex >= samples.Count)
            {
                IsPlaying = false;
            }
        }

        [ContextMenu("Load")]
        public void Load()
        {
            samples.Clear();
            samples.AddRange(ParseCsv(ResolvePersistentDataPath(csvPath)));

            sampleIndex = 0;
            playbackElapsed = 0;
            playbackOrigin = samples.Count > 0 ? samples[0].Timestamp : 0;

            IsLoaded = true;
        }

        [ContextMenu("Play")]
        public void Play()
        {
            if (!IsLoaded)
            {
                Load();
            }

            IsPlaying = samples.Count > 0;
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            IsPlaying = false;
        }

        [ContextMenu("Restart")]
        public void Restart()
        {
            sampleIndex = 0;
            playbackElapsed = 0;
            IsPlaying = samples.Count > 0;
        }

        private static string ResolvePersistentDataPath(string relativePath)
        {
            return string.IsNullOrEmpty(relativePath) ? relativePath : Path.Combine(Application.persistentDataPath, relativePath);
        }

        private static List<Sample> ParseCsv(string path)
        {
            var samples = new List<Sample>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"GazeSamplePlayer: CSV file not found: {path}");
                return samples;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return samples;
            }

            var columns = new Dictionary<string, int>();
            string[] header = lines[0].Split(',');
            for (int i = 0; i < header.Length; i++)
            {
                columns[header[i]] = i;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] row = lines[i].Split(',');

                // unity_time is GazeDataLogger's best available estimate of capture time (device clock +
                // measured offset where available, falling back to packet-arrival time otherwise).
                samples.Add(new Sample
                {
                    Timestamp = ParseDouble(Field(row, columns, "unity_time")),
                    CalibratedGazeDirection = ParseVector3OrNull(row, columns, "calibrated_gaze_dir"),
                    CalibratedGazeOrigin = ParseVector3OrNull(row, columns, "calibrated_gaze_origin"),
                });
            }

            samples.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return samples;
        }

        private static string Field(string[] row, Dictionary<string, int> columns, string name)
        {
            return columns.TryGetValue(name, out int index) && index < row.Length ? row[index] : string.Empty;
        }

        private static float ParseFloat(string value)
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string value)
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        private static Vector3? ParseVector3OrNull(string[] row, Dictionary<string, int> columns, string prefix)
        {
            string x = Field(row, columns, $"{prefix}_x");
            string y = Field(row, columns, $"{prefix}_y");
            string z = Field(row, columns, $"{prefix}_z");

            if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y) || string.IsNullOrEmpty(z))
            {
                return null;
            }

            return new Vector3(
                float.Parse(x, CultureInfo.InvariantCulture),
                float.Parse(y, CultureInfo.InvariantCulture),
                float.Parse(z, CultureInfo.InvariantCulture));
        }
    }
}