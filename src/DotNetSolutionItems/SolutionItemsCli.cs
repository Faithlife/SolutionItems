using System.CommandLine;

namespace DotNetSolutionItems;

internal static class SolutionItemsCli
{
	private const int DefaultLimit = 100;

	public static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		var solutionOption = new Option<string>("--solution")
		{
			Description = "Solution file or directory containing exactly one .slnx file. Defaults to the current directory.",
		};
		var limitOption = new Option<int>("--limit")
		{
			Description = "Maximum total generated Folder and File elements. Defaults to 100.",
			DefaultValueFactory = _ => DefaultLimit,
		};
		var globArgument = new Argument<string>("glob")
		{
			Description = "Glob to add or remove.",
		};

		var rootCommand = new RootCommand("Maintains Solution Items in .slnx files.");
		rootCommand.SetAction(_ => InvokeHelpAsync(rootCommand, standardOutput, standardError, cancellationToken));

		var addCommand = new Command("add", "Add a glob and update the managed Solution Items.");
		addCommand.Options.Add(solutionOption);
		addCommand.Options.Add(limitOption);
		addCommand.Arguments.Add(globArgument);
		addCommand.SetAction(parseResult => ExecuteAddAsync(parseResult, solutionOption, limitOption, globArgument, currentDirectory, standardOutput, standardError));
		rootCommand.Subcommands.Add(addCommand);

		var removeCommand = new Command("remove", "Remove a glob and update the managed Solution Items.");
		removeCommand.Options.Add(solutionOption);
		removeCommand.Options.Add(limitOption);
		removeCommand.Arguments.Add(globArgument);
		removeCommand.SetAction(parseResult => ExecuteRemoveAsync(parseResult, solutionOption, limitOption, globArgument, currentDirectory, standardOutput, standardError));
		rootCommand.Subcommands.Add(removeCommand);

		var listCommand = new Command("list", "List configured globs and matched files.");
		listCommand.Options.Add(solutionOption);
		listCommand.Options.Add(limitOption);
		listCommand.SetAction(parseResult => ExecuteListAsync(parseResult, solutionOption, limitOption, currentDirectory, standardOutput, standardError));
		rootCommand.Subcommands.Add(listCommand);

		var updateCommand = new Command("update", "Update the managed Solution Items.");
		updateCommand.Options.Add(solutionOption);
		updateCommand.Options.Add(limitOption);
		updateCommand.SetAction(parseResult => ExecuteUpdateAsync(parseResult, solutionOption, limitOption, currentDirectory, standardOutput, standardError));
		rootCommand.Subcommands.Add(updateCommand);

		var parseResult = rootCommand.Parse(args);
		return await InvokeParseResultAsync(parseResult, standardOutput, standardError, cancellationToken);
	}

	private static Task<int> ExecuteAddAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, Argument<string> globArgument, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document);
			var globs = markerComment is null ? [] : SolutionItemsXmlUpdater.GetGlobs(markerComment);
			var glob = parseResult.GetValue(globArgument) ?? throw new InvalidOperationException("Missing glob.");
			var updatedGlobs = SolutionItemsConfiguration.AddGlob(globs, glob);
			var generatedItems = Expand(solutionPath, updatedGlobs, parseResult.GetValue(limitOption));

			SolutionItemsXmlUpdater.Update(solutionDocument, updatedGlobs, generatedItems);
			solutionDocument.SaveIfChanged();
			var message = updatedGlobs.Count == globs.Count ? "already present" : "added";
			standardOutput.WriteLine($"Glob '{SolutionItemsConfiguration.TrimGlob(glob)}' {message}. Generated {generatedItems.Folders.Count} folders and {generatedItems.Files.Count} files in '{solutionPath.DisplayPath}'.");
			return 0;
		});
	}

	private static Task<int> ExecuteRemoveAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, Argument<string> globArgument, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document);
			if (markerComment is null)
				throw new InvalidOperationException("The dotnet-solution-items marker comment was not found.");

			var globs = SolutionItemsXmlUpdater.GetGlobs(markerComment);
			var glob = parseResult.GetValue(globArgument) ?? throw new InvalidOperationException("Missing glob.");
			var updatedGlobs = SolutionItemsConfiguration.RemoveGlob(globs, glob);
			if (updatedGlobs.Count == globs.Count)
			{
				standardOutput.WriteLine($"Glob '{SolutionItemsConfiguration.TrimGlob(glob)}' is already absent from '{solutionPath.DisplayPath}'.");
				return 0;
			}

			if (updatedGlobs.Count == 0)
			{
				SolutionItemsXmlUpdater.RemoveManagedBlock(solutionDocument);
				solutionDocument.SaveIfChanged();
				standardOutput.WriteLine($"Removed glob '{SolutionItemsConfiguration.TrimGlob(glob)}' and removed the managed Solution Items block from '{solutionPath.DisplayPath}'.");
				return 0;
			}

			var generatedItems = Expand(solutionPath, updatedGlobs, parseResult.GetValue(limitOption));
			SolutionItemsXmlUpdater.Update(solutionDocument, updatedGlobs, generatedItems);
			solutionDocument.SaveIfChanged();
			standardOutput.WriteLine($"Removed glob '{SolutionItemsConfiguration.TrimGlob(glob)}'. Generated {generatedItems.Folders.Count} folders and {generatedItems.Files.Count} files in '{solutionPath.DisplayPath}'.");
			return 0;
		});
	}

	private static Task<int> ExecuteListAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document);
			if (markerComment is null)
				throw new InvalidOperationException("The dotnet-solution-items marker comment was not found.");

			var globs = SolutionItemsXmlUpdater.GetGlobs(markerComment);
			var generatedItems = Expand(solutionPath, globs, parseResult.GetValue(limitOption));
			standardOutput.WriteLine("Globs:");
			foreach (var glob in globs)
				standardOutput.WriteLine($"  {glob}");

			standardOutput.WriteLine("Files:");
			foreach (var file in generatedItems.Files)
				standardOutput.WriteLine($"  {file}");

			return 0;
		});
	}

	private static Task<int> ExecuteUpdateAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document);
			if (markerComment is null)
			{
				standardError.WriteLine($"Warning: the dotnet-solution-items marker comment was not found in '{solutionPath.DisplayPath}'.");
				return 0;
			}

			var globs = SolutionItemsXmlUpdater.GetGlobs(markerComment);
			if (globs.Count == 0)
			{
				standardError.WriteLine($"Warning: the dotnet-solution-items marker comment in '{solutionPath.DisplayPath}' has no globs.");
				return 0;
			}

			var generatedItems = Expand(solutionPath, globs, parseResult.GetValue(limitOption));
			SolutionItemsXmlUpdater.Update(solutionDocument, globs, generatedItems);
			solutionDocument.SaveIfChanged();
			standardOutput.WriteLine($"Generated {generatedItems.Folders.Count} folders and {generatedItems.Files.Count} files in '{solutionPath.DisplayPath}'.");
			return 0;
		});
	}

	private static Task<int> ExecuteAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, string currentDirectory, TextWriter standardError, Func<int> action)
	{
		try
		{
			_ = ResolveSolution(parseResult, solutionOption, currentDirectory);
			if (parseResult.GetValue(limitOption) < 1)
				throw new InvalidOperationException("--limit must be greater than zero.");

			return Task.FromResult(action());
		}
		catch (InvalidOperationException ex)
		{
			standardError.WriteLine(ex.Message);
			return Task.FromResult(1);
		}
	}

	private static GeneratedSolutionItems Expand(ResolvedSolutionPath solutionPath, IReadOnlyList<string> globs, int limit)
	{
		try
		{
			return SolutionItemsGlobExpander.Expand(solutionPath.SolutionDirectory, globs, limit);
		}
		catch (InvalidOperationException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"{ex.Message} Solution: '{solutionPath.DisplayPath}'.", ex);
		}
	}

	private static ResolvedSolutionPath ResolveSolution(ParseResult parseResult, Option<string> solutionOption, string currentDirectory) =>
		ResolvedSolutionPath.Resolve(currentDirectory, parseResult.GetValue(solutionOption));

	private static Task<int> InvokeHelpAsync(RootCommand rootCommand, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken) =>
		InvokeParseResultAsync(rootCommand.Parse(["--help"]), standardOutput, standardError, cancellationToken);

	private static async Task<int> InvokeParseResultAsync(ParseResult parseResult, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		var originalOutput = Console.Out;
		var originalError = Console.Error;

		try
		{
			Console.SetOut(standardOutput);
			Console.SetError(standardError);
			return await parseResult.InvokeAsync(cancellationToken: cancellationToken);
		}
		finally
		{
			Console.SetOut(originalOutput);
			Console.SetError(originalError);
			await standardOutput.FlushAsync(cancellationToken);
			await standardError.FlushAsync(cancellationToken);
		}
	}
}
