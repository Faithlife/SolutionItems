namespace DotNetSolutionItems.Tests;

internal sealed class TemporarySolutionDirectory : IDisposable
{
	private TemporarySolutionDirectory(string rootPath)
	{
		RootPath = rootPath;
	}

	public string RootPath { get; }

	public static TemporarySolutionDirectory Create()
	{
		var rootPath = TemporaryDirectoryPath.Create();
		Directory.CreateDirectory(rootPath);
		return new TemporarySolutionDirectory(rootPath);
	}

	public string ReadFile(string relativePath) => File.ReadAllText(Path.Combine(RootPath, relativePath));

	public void WriteFile(string relativePath, string contents)
	{
		var path = Path.Combine(RootPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
	}

	public bool FileExists(string relativePath) => File.Exists(Path.Combine(RootPath, relativePath));

	public void Dispose()
	{
		if (Directory.Exists(RootPath))
			Directory.Delete(RootPath, recursive: true);
	}
}
