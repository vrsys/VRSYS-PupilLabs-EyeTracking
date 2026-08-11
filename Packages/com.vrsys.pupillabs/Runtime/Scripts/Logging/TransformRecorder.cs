using System;
using System.Globalization;
using System.IO;
using UnityEngine;


namespace VRSYS.PupilLabs
{


    /// <summary>
    /// Records a target transform's world position/rotation to a timestamped CSV, one row per frame
    /// while recording. Generic and study-agnostic (only needs a Transform reference), so it can be
    /// reused for head tracking or any other transform outside this project.
    /// </summary>
    public class TransformRecorder : MonoBehaviour
    {
        private const string CsvHeader =
            "timestamp,position_x,position_y,position_z," +
            "rotation_x,rotation_y,rotation_z,rotation_w";

        [SerializeField] private Transform target;

        [Tooltip("Used to build the output file name: p{participantId}_block{n}_{timestamp}_{fileSuffix}.csv")]
        [SerializeField] private string fileSuffix = "head";

        private StreamWriter writer;
        private string participantId;

        public bool IsRecording => writer != null;

        public void BeginSession(string participantId)
        {
            this.participantId = participantId;
        }

        public void StartRecording(string fileIdentifier)
        {
            StopRecording();

            string directory = Path.Combine(Application.persistentDataPath, "StudyData");
            Directory.CreateDirectory(directory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"{fileIdentifier}_{timestamp}_{fileSuffix}.csv";

            string filePath = Path.Combine(directory, filename);

            writer = new StreamWriter(filePath, append: false);
            writer.WriteLine(CsvHeader);
        }

        public void StartRecording(int trialBlockNumber)
        {
            string fileIdentifier = $"p{participantId}_block{trialBlockNumber}";
            StartRecording(fileIdentifier);
        }

        public void StopRecording()
        {
            writer?.Dispose();
            writer = null;
        }

        public void EndSession()
        {
            StopRecording();
        }

        private void Update()
        {
            if (writer == null || target == null)
            {
                return;
            }

            Vector3 position = target.position;
            Quaternion rotation = target.rotation;

            writer.WriteLine(string.Join(",",
                FormatDouble(Time.realtimeSinceStartupAsDouble),
                FormatFloat(position.x), FormatFloat(position.y), FormatFloat(position.z),
                FormatFloat(rotation.x), FormatFloat(rotation.y),
                FormatFloat(rotation.z), FormatFloat(rotation.w)));
        }

        private void OnApplicationQuit()
        {
            StopRecording();
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}