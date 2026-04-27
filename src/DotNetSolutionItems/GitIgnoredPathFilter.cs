using System.Diagnostics;

namespace DotNetSolutionItems;

internal static class GitIgnoredPathFilter
{
	public static IReadOnlySet<string> GetIgnoredPaths(string directory, IEnumerable<string> paths)
	{
		var pathArray = paths.ToArray();
		if (pathArray.Length == 0)
			return new HashSet<string>(GetComparer());

		try
		{
			using var process = StartGitCheckIgnore(directory);
			foreach (var path in pathArray)
				process.StandardInput.Write(path + '\0');

			process.StandardInput.Close();
			var output = process.StandardOutput.ReadToEnd();
			_ = process.StandardError.ReadToEnd();
			process.WaitForExit();

			return process.ExitCode is 0 or 1 ? ParseIgnoredPaths(output) : new HashSet<string>(GetComparer());
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
		{
			return new HashSet<string>(GetComparer());
		}
	}

	private static Process StartGitCheckIgnore(string directory)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "git",
			WorkingDirectory = directory,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
		};
		startInfo.ArgumentList.Add("check-ignore");
		startInfo.ArgumentList.Add("--stdin");
		startInfo.ArgumentList.Add("-z");

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
	}

	private static HashSet<string> ParseIgnoredPaths(string output) =>
		output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(GetComparer());

	private static StringComparer GetComparer() =>
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}