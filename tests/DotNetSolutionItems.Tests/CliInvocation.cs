namespace DotNetSolutionItems.Tests;

internal static class CliInvocation
{
	public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory)
	{
		using var standardOutput = new StringWriter();
		using var standardError = new StringWriter();

		var exitCode = await SolutionItemsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, CancellationToken.None);

		return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
	}
}
