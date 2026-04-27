namespace DotNetSolutionItems;

internal static class SolutionItemsConfiguration
{
	public const string MarkerPrefix = "dotnet-solution-items:";

	public static IReadOnlyList<string> ParseGlobs(string markerCommentValue)
	{
		var markerText = markerCommentValue.Trim();
		if (!markerText.StartsWith(MarkerPrefix, StringComparison.Ordinal))
			throw new InvalidOperationException("The comment is not a dotnet-solution-items marker comment.");

		return SplitGlobs([markerText[MarkerPrefix.Length..]]);
	}

	public static string FormatCommentValue(IReadOnlyList<string> globs) =>
		$" {MarkerPrefix} {string.Join("; ", globs)} ";

	public static IReadOnlyList<string> SplitGlobs(IReadOnlyList<string> globArguments)
	{
		var globs = globArguments
			.SelectMany(x => x.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
			.Select(TrimGlob)
			.ToArray();
		if (globs.Length == 0)
			throw new InvalidOperationException("At least one glob must be specified.");

		return globs;
	}

	public static IReadOnlyList<string> AddGlobs(IReadOnlyList<string> globs, IReadOnlyList<string> globsToAdd)
	{
		var updatedGlobs = globs.ToList();
		foreach (var glob in globsToAdd)
		{
			if (!updatedGlobs.Contains(glob, StringComparer.Ordinal))
				updatedGlobs.Add(glob);
		}

		return updatedGlobs;
	}

	public static IReadOnlyList<string> RemoveGlobs(IReadOnlyList<string> globs, IReadOnlyList<string> globsToRemove)
	{
		return globs.Where(x => !globsToRemove.Contains(x, StringComparer.Ordinal)).ToArray();
	}

	public static string TrimGlob(string glob)
	{
		var trimmedGlob = glob.Trim();
		if (trimmedGlob.Length == 0)
			throw new InvalidOperationException("The glob must not be empty.");

		return trimmedGlob;
	}
}
