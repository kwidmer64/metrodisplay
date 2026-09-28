namespace MetroDisplay.Server.Tests;

/// <summary>
/// A fresh directory under the system temp folder, deleted on dispose.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string FullPath { get; } = Path.Combine(Path.GetTempPath(), "metrodisplay-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(FullPath);

    public void Dispose() => Directory.Delete(FullPath, recursive: true);
}
