namespace DotNetSolutionItems.Tests;

internal static class TemporaryDirectoryPath
{
	public static string Create() => Path.Combine(Path.GetTempPath(), $"dotnet-solution-items-{Guid.NewGuid():N}");
}
