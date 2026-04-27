namespace DotNetSolutionItems;

internal sealed record ResolvedSolutionPath(string SolutionPath, string SolutionDirectory, string DisplayPath)
{
	public static ResolvedSolutionPath Resolve(string currentDirectory, string? solutionOption)
	{
		var candidatePath = string.IsNullOrWhiteSpace(solutionOption) ? currentDirectory : ResolvePath(currentDirectory, solutionOption);
		if (Directory.Exists(candidatePath))
		{
			var solutionPaths = Directory.GetFiles(candidatePath, "*.slnx", SearchOption.TopDirectoryOnly);
			if (solutionPaths.Length == 0)
				throw new InvalidOperationException($"No .slnx files were found in '{GetDisplayPath(currentDirectory, candidatePath)}'.");

			if (solutionPaths.Length > 1)
				throw new InvalidOperationException($"More than one .slnx file was found in '{GetDisplayPath(currentDirectory, candidatePath)}'. Use --solution to specify the solution file.");

			return Create(currentDirectory, solutionPaths[0]);
		}

		if (!string.Equals(Path.GetExtension(candidatePath), ".slnx", StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"The solution path '{GetDisplayPath(currentDirectory, candidatePath)}' must be a .slnx file or a directory containing one .slnx file.");

		if (!File.Exists(candidatePath))
			throw new InvalidOperationException($"The solution file '{GetDisplayPath(currentDirectory, candidatePath)}' was not found.");

		return Create(currentDirectory, candidatePath);
	}

	private static ResolvedSolutionPath Create(string currentDirectory, string solutionPath)
	{
		var fullSolutionPath = Path.GetFullPath(solutionPath);
		var solutionDirectory = Path.GetDirectoryName(fullSolutionPath) ?? throw new InvalidOperationException("The solution path has no containing directory.");
		return new ResolvedSolutionPath(fullSolutionPath, solutionDirectory, GetDisplayPath(currentDirectory, fullSolutionPath));
	}

	private static string ResolvePath(string currentDirectory, string path) =>
		Path.GetFullPath(path, currentDirectory);

	private static string GetDisplayPath(string currentDirectory, string path)
	{
		var relativePath = Path.GetRelativePath(currentDirectory, path);
		return relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathFullyQualified(relativePath) ? path : relativePath;
	}
}
