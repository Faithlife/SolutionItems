namespace DotNetSolutionItems;

internal static class SolutionItemsConfiguration
{
	public const string MarkerPrefix = "dotnet-solution-items:";

	public static IReadOnlyList<string> ParseGlobs(string markerCommentValue)
	{
		var markerText = markerCommentValue.Trim();
		if (!markerText.StartsWith(MarkerPrefix, StringComparison.Ordinal))
			throw new InvalidOperationException("The comment is not a dotnet-solution-items marker comment.");

		return markerText[MarkerPrefix.Length..]
			.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
			.ToArray();
	}

	public static string FormatCommentValue(IReadOnlyList<string> globs) =>
		$" {MarkerPrefix} {string.Join("; ", globs)} ";

	public static IReadOnlyList<string> AddGlob(IReadOnlyList<string> globs, string glob)
	{
		var trimmedGlob = TrimGlob(glob);
		return globs.Contains(trimmedGlob, StringComparer.Ordinal) ? globs : [.. globs, trimmedGlob];
	}

	public static IReadOnlyList<string> RemoveGlob(IReadOnlyList<string> globs, string glob)
	{
		var trimmedGlob = TrimGlob(glob);
		return globs.Where(x => !string.Equals(x, trimmedGlob, StringComparison.Ordinal)).ToArray();
	}

	public static string TrimGlob(string glob)
	{
		var trimmedGlob = glob.Trim();
		if (trimmedGlob.Length == 0)
			throw new InvalidOperationException("The glob must not be empty.");

		return trimmedGlob;
	}
}
