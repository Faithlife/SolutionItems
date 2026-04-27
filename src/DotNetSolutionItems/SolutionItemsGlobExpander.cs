using GlobExpressions;

namespace DotNetSolutionItems;

internal static class SolutionItemsGlobExpander
{
	public static GeneratedSolutionItems Expand(string solutionDirectory, IReadOnlyList<string> globs, int limit)
	{
		if (limit < 1)
			throw new InvalidOperationException("--limit must be greater than zero.");

		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var includedPaths = new HashSet<string>(comparer);
		var excludedPaths = new HashSet<string>(comparer);

		foreach (var glob in globs)
		{
			var isExclude = glob.StartsWith('!');
			var pattern = isExclude ? glob[1..].Trim() : glob;
			if (pattern.Length == 0)
				continue;

			foreach (var path in ExpandFilePaths(solutionDirectory, pattern))
			{
				if (IsAutomaticallyExcluded(path))
					continue;

				if (isExclude)
					excludedPaths.Add(path);
				else
					includedPaths.Add(path);
			}
		}

		includedPaths.ExceptWith(excludedPaths);
		includedPaths.ExceptWith(GitIgnoredPathFilter.GetIgnoredPaths(solutionDirectory, includedPaths));

		var files = includedPaths.Order(StringComparer.Ordinal).ToArray();
		var folders = CreateFolders(files);
		var generatedItems = new GeneratedSolutionItems(folders, files);
		if (generatedItems.ElementCount >= limit)
			throw new InvalidOperationException($"Generated solution items reached the limit of {limit} elements. Pass --limit <n> to increase the limit if this result is intentional.");

		return generatedItems;
	}

	private static IEnumerable<string> ExpandFilePaths(string solutionDirectory, string pattern) =>
		Glob.Files(new DirectoryInfo(solutionDirectory), pattern, GlobOptions.None)
			.Select(x => ToRelativePath(solutionDirectory, x.FullName));

	private static GeneratedSolutionFolder[] CreateFolders(string[] files)
	{
		if (files.Length == 0)
			return [];

		var folderFiles = new SortedDictionary<string, List<string>>(StringComparer.Ordinal) { ["/Solution Items/"] = [] };
		foreach (var file in files)
		{
			var folderPath = GetFolderPath(file);
			if (!folderFiles.TryGetValue(folderPath, out var folderFileList))
			{
				folderFileList = [];
				folderFiles.Add(folderPath, folderFileList);
			}

			folderFileList.Add(file);
			AddAncestorFolders(folderFiles, folderPath);
		}

		return folderFiles.Select(x => new GeneratedSolutionFolder(x.Key, x.Value.Order(StringComparer.Ordinal).ToArray())).ToArray();
	}

	private static void AddAncestorFolders(SortedDictionary<string, List<string>> folderFiles, string folderPath)
	{
		var relativeFolderPath = folderPath["/Solution Items/".Length..].Trim('/');
		if (relativeFolderPath.Length == 0)
			return;

		var segments = relativeFolderPath.Split('/');
		var currentPath = "/Solution Items/";
		foreach (var segment in segments)
		{
			currentPath += segment + "/";
			if (!folderFiles.ContainsKey(currentPath))
				folderFiles.Add(currentPath, []);
		}
	}

	private static string GetFolderPath(string relativeFilePath)
	{
		var directoryPath = Path.GetDirectoryName(relativeFilePath)?.Replace('\\', '/') ?? "";
		return directoryPath.Length == 0 ? "/Solution Items/" : $"/Solution Items/{directoryPath}/";
	}

	private static bool IsAutomaticallyExcluded(string relativePath) =>
		string.Equals(Path.GetExtension(relativePath), ".slnx", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(Path.GetExtension(relativePath), ".csproj", StringComparison.OrdinalIgnoreCase);

	private static string ToRelativePath(string solutionDirectory, string path) =>
		Path.GetRelativePath(solutionDirectory, path).Replace('\\', '/');
}
