using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace TradeMonitor.ServiceInterface;

// One session for the process. The file is optional; a missing model leaves Ready false.
public sealed class OnnxForecastRunner : IForecastRunner, IDisposable
{
    readonly string path;
    InferenceSession? session;

    public OnnxForecastRunner(string path)
    {
        this.path = path ?? "";
    }

    public string Name => ForecastOptions.ModelName;
    public bool Ready => ForecastFiles.ModelExists(path);

    public ForecastBatch Run(IReadOnlyList<float[]> rows)
    {
        if (!Ready || rows.Count == 0)
            return new ForecastBatch();
        var length = rows[0].Length;
        if (length == 0 || rows.Any(row => row.Length != length))
            return new ForecastBatch();
        session ??= new InferenceSession(path);
        var tensor = new DenseTensor<float>(new[] { rows.Count, length });
        for (var batch = 0; batch < rows.Count; batch++)
        {
            for (var i = 0; i < length; i++)
                tensor[batch, i] = rows[batch][i];
        }

        var inputName = session.InputMetadata.Keys.FirstOrDefault() ?? "past_values";
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
        using var results = session.Run(inputs);
        var mean = Tensor(results, "mean_predictions", 1);
        var full = Tensor(results, "full_predictions", 2);
        if (mean == null || full == null)
            return new ForecastBatch();
        var horizon = mean.Dimensions.Length > 1 ? mean.Dimensions[1] : mean.Length;
        var quantiles = full.Dimensions.Length > 2 ? full.Dimensions[2] : 9;
        var means = new float[rows.Count][];
        var bands = new float[rows.Count][][];
        for (var batch = 0; batch < rows.Count; batch++)
        {
            means[batch] = new float[horizon];
            bands[batch] = new float[horizon][];
            for (var step = 0; step < horizon; step++)
            {
                means[batch][step] = mean[batch, step];
                bands[batch][step] = new float[quantiles];
                for (var q = 0; q < quantiles; q++)
                    bands[batch][step][q] = full[batch, step, q];
            }
        }

        return new ForecastBatch { Means = means, Quantiles = bands };
    }

    public void Dispose() => session?.Dispose();

    static DenseTensor<float>? Tensor(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name, int fallbackIndex)
    {
        var match = results.FirstOrDefault(result => result.Name == name) ?? results.ElementAtOrDefault(fallbackIndex);
        var tensor = match?.AsTensor<float>();
        if (tensor == null)
            return null;
        return tensor as DenseTensor<float> ?? tensor.ToDenseTensor();
    }
}
