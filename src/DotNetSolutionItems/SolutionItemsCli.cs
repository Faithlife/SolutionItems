using System.CommandLine;

namespace DotNetSolutionItems;

internal static class SolutionItemsCli
{
	private const int c_defaultLimit = 100;

	public static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		var solutionOption = new Option<string>("--solution")
		{
			Description = "Solution file or directory containing exactly one .slnx file. Defaults to the current directory.",
		};
		var limitOption = new Option<int>("--limit")
		{
			Description = "Maximum total generated Folder and File elements. Defaults to 100.",
			DefaultValueFactory = _ => c_defaultLimit,
		};
		var forceOption = new Option<bool>("--force")
		{
			Description = "Replace an existing unmarked Solution Items block when adding the first glob.",
		};
		var globArgument = new Argument<string[]>("glob")
		{
			Description = "Globs to add or remove. Arguments may contain semicolon-separated globs.",
			Arity = ArgumentArity.OneOrMore,
		};

		var rootCommand = new RootCommand("Maintains Solution Items in .slnx files.");
		rootCommand.SetAction(_ => InvokeHelpAsync(rootCommand, standardOutput, standardError, cancellationToken));

		var addCommand = new Command("add", "Add a glob and update the managed Solution Items.");
		addCommand.Options.Add(solutionOption);
		addCommand.Options.Add(limitOption);
		addCommand.Options.Add(forceOption);
		addCommand.Arguments.Add(globArgument);
		addCommand.SetAction(parseResult => ExecuteAddAsync(parseResult, solutionOption, limitOption, forceOption, globArgument, currentDirectory, standardOutput, standardError));
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

	private static Task<int> ExecuteAddAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, Option<bool> forceOption, Argument<string[]> globArgument, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document);
			if (markerComment is null && !parseResult.GetValue(forceOption) && SolutionItemsXmlUpdater.HasUnmarkedSolutionItemsBlock(solutionDocument.Document))
				throw new InvalidOperationException("Warning: an existing Solution Items block was found. Adding globs would replace that block and remove items that do not match the globs. Re-run with --force to continue.");

			var globs = markerComment is null ? [] : SolutionItemsXmlUpdater.GetGlobs(markerComment);
			var globsToAdd = SolutionItemsConfiguration.SplitGlobs(parseResult.GetValue(globArgument) ?? []);
			var updatedGlobs = SolutionItemsConfiguration.AddGlobs(globs, globsToAdd);
			var generatedItems = Expand(solutionPath, updatedGlobs, parseResult.GetValue(limitOption));

			SolutionItemsXmlUpdater.Update(solutionDocument, updatedGlobs, generatedItems, parseResult.GetValue(forceOption));
			solutionDocument.SaveIfChanged();
			standardOutput.WriteLine($"Added {updatedGlobs.Count - globs.Count} glob(s); {globsToAdd.Count - (updatedGlobs.Count - globs.Count)} already present. Generated {generatedItems.Folders.Count} folders and {generatedItems.Files.Count} files in '{solutionPath.DisplayPath}'.");
			return 0;
		});
	}

	private static Task<int> ExecuteRemoveAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, Argument<string[]> globArgument, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document) ?? throw new InvalidOperationException("The dotnet-solution-items marker comment was not found.");

			var globs = SolutionItemsXmlUpdater.GetGlobs(markerComment);
			var globsToRemove = SolutionItemsConfiguration.SplitGlobs(parseResult.GetValue(globArgument) ?? []);
			var updatedGlobs = SolutionItemsConfiguration.RemoveGlobs(globs, globsToRemove);
			if (updatedGlobs.Count == globs.Count)
			{
				standardOutput.WriteLine($"All specified globs are already absent from '{solutionPath.DisplayPath}'.");
				return 0;
			}

			if (updatedGlobs.Count == 0)
			{
				SolutionItemsXmlUpdater.RemoveManagedBlock(solutionDocument);
				solutionDocument.SaveIfChanged();
				standardOutput.WriteLine($"Removed {globs.Count} glob(s) and removed the managed Solution Items block from '{solutionPath.DisplayPath}'.");
				return 0;
			}

			var generatedItems = Expand(solutionPath, updatedGlobs, parseResult.GetValue(limitOption));
			SolutionItemsXmlUpdater.Update(solutionDocument, updatedGlobs, generatedItems);
			solutionDocument.SaveIfChanged();
			standardOutput.WriteLine($"Removed {globs.Count - updatedGlobs.Count} glob(s); {globsToRemove.Count - (globs.Count - updatedGlobs.Count)} already absent. Generated {generatedItems.Folders.Count} folders and {generatedItems.Files.Count} files in '{solutionPath.DisplayPath}'.");
			return 0;
		});
	}

	private static Task<int> ExecuteListAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, string currentDirectory, TextWriter standardOutput, TextWriter standardError)
	{
		return ExecuteAsync(parseResult, solutionOption, limitOption, currentDirectory, standardError, () =>
		{
			var solutionPath = ResolveSolution(parseResult, solutionOption, currentDirectory);
			var solutionDocument = SolutionItemsXmlUpdater.Load(solutionPath.SolutionPath);
			var markerComment = SolutionItemsXmlUpdater.FindMarkerComment(solutionDocument.Document) ?? throw new InvalidOperationException("The dotnet-solution-items marker comment was not found.");

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

	private static async Task<int> ExecuteAsync(ParseResult parseResult, Option<string> solutionOption, Option<int> limitOption, string currentDirectory, TextWriter standardError, Func<int> action)
	{
		try
		{
			_ = ResolveSolution(parseResult, solutionOption, currentDirectory);
			if (parseResult.GetValue(limitOption) < 1)
				throw new InvalidOperationException("--limit must be greater than zero.");

			return action();
		}
		catch (InvalidOperationException ex)
		{
			await standardError.WriteLineAsync(ex.Message);
			return 1;
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
