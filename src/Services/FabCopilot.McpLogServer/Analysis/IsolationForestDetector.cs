using FabCopilot.Contracts.Models;

namespace FabCopilot.McpLogServer.Analysis;

/// <summary>
/// Lightweight Isolation Forest anomaly detector for sensor data.
/// Each equipment gets its own detector with independent baseline learning.
/// </summary>
public sealed class IsolationForestDetector
{
    private readonly int _numTrees;
    private readonly int _sampleSize;
    private readonly Random _rng;
    private List<IsolationTree>? _forest;
    private double _averagePathLength;
    private string _modelVersion = "";

    public string EquipmentId { get; }
    public string SensorId { get; }
    public bool IsTrained => _forest is not null;
    public string ModelVersion => _modelVersion;
    public DateTimeOffset TrainedAt { get; private set; }

    public IsolationForestDetector(string equipmentId, string sensorId, int numTrees = 100, int sampleSize = 256, int? seed = null)
    {
        EquipmentId = equipmentId;
        SensorId = sensorId;
        _numTrees = numTrees;
        _sampleSize = sampleSize;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Trains the isolation forest on baseline (normal) sensor data.
    /// </summary>
    public void Train(double[] data)
    {
        if (data.Length == 0) return;

        var effectiveSampleSize = Math.Min(_sampleSize, data.Length);
        _forest = new List<IsolationTree>(_numTrees);

        for (var t = 0; t < _numTrees; t++)
        {
            var sample = SampleWithReplacement(data, effectiveSampleSize);
            var tree = BuildTree(sample, 0, MaxTreeHeight(effectiveSampleSize));
            _forest.Add(tree);
        }

        _averagePathLength = AveragePathLengthC(effectiveSampleSize);
        _modelVersion = $"v{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        TrainedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Computes the anomaly score for a single observation.
    /// Score closer to 1.0 = more anomalous. Score around 0.5 = normal.
    /// </summary>
    public double Score(double value)
    {
        if (_forest is null || _forest.Count == 0)
            return 0.5; // Untrained: neutral

        var totalPathLength = 0.0;
        foreach (var tree in _forest)
        {
            totalPathLength += PathLength(value, tree, 0);
        }

        var avgPathLength = totalPathLength / _forest.Count;
        // Anomaly score: s(x) = 2^(-E[h(x)] / c(n))
        var score = Math.Pow(2, -avgPathLength / _averagePathLength);
        return score;
    }

    /// <summary>
    /// Classifies an observation as Normal, Caution, or Anomaly.
    /// </summary>
    public AnomalySeverity Classify(double value, double cautionThreshold = 0.6, double anomalyThreshold = 0.75)
    {
        var score = Score(value);
        if (score >= anomalyThreshold) return AnomalySeverity.Anomaly;
        if (score >= cautionThreshold) return AnomalySeverity.Caution;
        return AnomalySeverity.Normal;
    }

    /// <summary>
    /// Batch scoring for a time series of sensor readings.
    /// </summary>
    public List<AnomalyResult> DetectAnomalies(IReadOnlyList<SensorReading> readings,
        double cautionThreshold = 0.6, double anomalyThreshold = 0.75)
    {
        var results = new List<AnomalyResult>();

        foreach (var reading in readings)
        {
            var score = Score(reading.Value);
            var severity = score >= anomalyThreshold ? AnomalySeverity.Anomaly
                : score >= cautionThreshold ? AnomalySeverity.Caution
                : AnomalySeverity.Normal;

            if (severity != AnomalySeverity.Normal)
            {
                results.Add(new AnomalyResult
                {
                    EquipmentId = reading.EquipmentId,
                    SensorId = reading.SensorId,
                    DetectedAt = reading.Timestamp,
                    Value = reading.Value,
                    Confidence = score,
                    Severity = severity,
                    Type = AnomalyType.SpikeUp, // Simplified; real system would classify type
                    Description = $"Isolation Forest anomaly score: {score:F3}"
                });
            }
        }

        return results;
    }

    // ── Tree Construction ────────────────────────────────────────────

    private IsolationTree BuildTree(double[] data, int depth, int maxDepth)
    {
        if (depth >= maxDepth || data.Length <= 1)
        {
            return new IsolationTree { Size = data.Length };
        }

        var min = data.Min();
        var max = data.Max();

        if (Math.Abs(max - min) < 1e-10)
        {
            return new IsolationTree { Size = data.Length };
        }

        var splitValue = min + _rng.NextDouble() * (max - min);

        var left = data.Where(x => x < splitValue).ToArray();
        var right = data.Where(x => x >= splitValue).ToArray();

        // Avoid degenerate splits
        if (left.Length == 0 || right.Length == 0)
        {
            return new IsolationTree { Size = data.Length };
        }

        return new IsolationTree
        {
            SplitValue = splitValue,
            Left = BuildTree(left, depth + 1, maxDepth),
            Right = BuildTree(right, depth + 1, maxDepth),
            Size = data.Length
        };
    }

    private static double PathLength(double value, IsolationTree node, int currentDepth)
    {
        if (node.IsLeaf)
        {
            return currentDepth + AveragePathLengthC(node.Size);
        }

        return value < node.SplitValue
            ? PathLength(value, node.Left!, currentDepth + 1)
            : PathLength(value, node.Right!, currentDepth + 1);
    }

    private double[] SampleWithReplacement(double[] data, int size)
    {
        var sample = new double[size];
        for (var i = 0; i < size; i++)
        {
            sample[i] = data[_rng.Next(data.Length)];
        }
        return sample;
    }

    private static int MaxTreeHeight(int sampleSize)
    {
        return (int)Math.Ceiling(Math.Log2(Math.Max(sampleSize, 2)));
    }

    /// <summary>
    /// Average path length of unsuccessful search in BST (Equation 1 from Isolation Forest paper).
    /// c(n) = 2H(n-1) - 2(n-1)/n where H(i) = ln(i) + 0.5772 (Euler's constant)
    /// </summary>
    internal static double AveragePathLengthC(int n)
    {
        if (n <= 1) return 0;
        if (n == 2) return 1;

        var harmonicNumber = Math.Log(n - 1) + 0.5772156649;
        return 2.0 * harmonicNumber - 2.0 * (n - 1) / n;
    }

    private sealed class IsolationTree
    {
        public double SplitValue { get; set; }
        public IsolationTree? Left { get; set; }
        public IsolationTree? Right { get; set; }
        public int Size { get; set; }
        public bool IsLeaf => Left is null && Right is null;
    }
}

/// <summary>
/// Manages per-equipment, per-sensor Isolation Forest detectors with model versioning.
/// </summary>
public sealed class AnomalyDetectorManager
{
    private readonly Dictionary<string, IsolationForestDetector> _detectors = new();

    /// <summary>
    /// Gets or creates a detector for the given equipment+sensor pair.
    /// </summary>
    public IsolationForestDetector GetOrCreate(string equipmentId, string sensorId, int? seed = null)
    {
        var key = $"{equipmentId}:{sensorId}";
        if (!_detectors.TryGetValue(key, out var detector))
        {
            detector = new IsolationForestDetector(equipmentId, sensorId, seed: seed);
            _detectors[key] = detector;
        }
        return detector;
    }

    /// <summary>
    /// Trains a detector with baseline data.
    /// </summary>
    public void TrainDetector(string equipmentId, string sensorId, double[] baselineData, int? seed = null)
    {
        var detector = GetOrCreate(equipmentId, sensorId, seed);
        detector.Train(baselineData);
    }

    /// <summary>
    /// Scores a reading against the trained detector.
    /// Returns null if no detector is trained for this sensor.
    /// </summary>
    public AnomalySeverity? Classify(string equipmentId, string sensorId, double value)
    {
        var key = $"{equipmentId}:{sensorId}";
        if (!_detectors.TryGetValue(key, out var detector) || !detector.IsTrained)
            return null;

        return detector.Classify(value);
    }

    /// <summary>
    /// Lists all trained detectors with their model versions.
    /// </summary>
    public IReadOnlyList<(string EquipmentId, string SensorId, string ModelVersion, DateTimeOffset TrainedAt)> ListModels()
    {
        return _detectors.Values
            .Where(d => d.IsTrained)
            .Select(d => (d.EquipmentId, d.SensorId, d.ModelVersion, d.TrainedAt))
            .ToList();
    }
}
