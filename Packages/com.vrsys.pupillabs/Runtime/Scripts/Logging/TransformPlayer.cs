using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace VRSYS.PupilLabs
{

    /// <summary>
    /// Plays back a timestamped position/rotation CSV written by TransformRecorder against a target
    /// transform in real time, snapping to the latest sample at or before the current playback time
    /// (no interpolation). Fully self-contained: playback starts from this file's own first sample and
    /// stops itself once it runs past the last one, with no duration/end time exposed for external
    /// synchronization - stop and restart the scene manually if you need to replay it.
    /// </summary>
    public class TransformPlayer : MonoBehaviour
    {
        private struct Sample
        {
            public double Timestamp;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        [SerializeField] private Transform target;
        [SerializeField] public float playbackSpeed = 1f;
        [SerializeField] public bool playOnStart = true;

        [Tooltip("CSV written by TransformRecorder, relative to Application.persistentDataPath.")]
        [SerializeField] public string csvPath;
        [SerializeField] public string fileSuffix;

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
                lastApplied = sampleIndex;
                sampleIndex++;
            }

            if (lastApplied >= 0 && target != null)
            {
                target.SetPositionAndRotation(samples[lastApplied].Position, samples[lastApplied].Rotation);
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
                Debug.LogWarning($"TransformPlayer: CSV file not found: {path}");
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
                samples.Add(new Sample
                {
                    Timestamp = ParseDouble(Field(row, columns, "timestamp")),
                    Position = new Vector3(
                        ParseFloat(Field(row, columns, "position_x")),
                        ParseFloat(Field(row, columns, "position_y")),
                        ParseFloat(Field(row, columns, "position_z"))),
                    Rotation = new Quaternion(
                        ParseFloat(Field(row, columns, "rotation_x")),
                        ParseFloat(Field(row, columns, "rotation_y")),
                        ParseFloat(Field(row, columns, "rotation_z")),
                        ParseFloat(Field(row, columns, "rotation_w"))),
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
    }

}