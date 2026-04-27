using NUnit.Framework;

namespace DotNetSolutionItems.Tests;

internal sealed class SolutionItemsCliTests
{
	[Test]
	public async Task DiscoveryFailsWhenCurrentDirectoryHasNoSolution()
	{
		using var directory = TemporarySolutionDirectory.Create();

		var result = await CliInvocation.InvokeAsync(["list"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.StandardError, Does.Contain("No .slnx files"));
		}
	}

	[Test]
	public async Task DiscoveryFailsWhenCurrentDirectoryHasMultipleSolutions()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("one.slnx", "<Solution />");
		directory.WriteFile("two.slnx", "<Solution />");

		var result = await CliInvocation.InvokeAsync(["list"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.StandardError, Does.Contain("More than one .slnx file"));
		}
	}

	[Test]
	public async Task SolutionOptionAcceptsExactFile()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["list", "--solution", Path.Combine(directory.RootPath, "repo.slnx")], Environment.CurrentDirectory);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardOutput, Does.Contain("README.md"));
		}
	}

	[Test]
	public async Task SolutionOptionAcceptsDirectory()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["list", "--solution", directory.RootPath], Environment.CurrentDirectory);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardOutput, Does.Contain("README.md"));
		}
	}

	[Test]
	public async Task AddCreatesMarkerAndManagedBlockBeforeProjects()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("build.ps1", "build");
		directory.WriteFile("src/App/App.csproj", "<Project />");

		var result = await CliInvocation.InvokeAsync(["add", "*"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Is.EqualTo("""
				<Solution>
				  <!-- dotnet-solution-items: * -->
				  <Folder Name="/Solution Items/">
				    <File Path="README.md" />
				    <File Path="build.ps1" />
				  </Folder>
				  <Project Path="src/App/App.csproj" />
				</Solution>
				"""));
		}
	}

	[Test]
	public async Task FirstAddFailsWhenUnmarkedSolutionItemsBlockExists()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");
		var originalSolution = directory.ReadFile("repo.slnx");

		var result = await CliInvocation.InvokeAsync(["add", "*"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.StandardError, Does.Contain("Warning"));
			Assert.That(result.StandardError, Does.Contain("--force"));
			Assert.That(directory.ReadFile("repo.slnx"), Is.EqualTo(originalSolution));
		}
	}

	[Test]
	public async Task FirstAddForceReplacesUnmarkedSolutionItemsBlock()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("old.txt", "old");

		var result = await CliInvocation.InvokeAsync(["add", "README.md", "--force"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<!-- dotnet-solution-items: README.md -->"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"README.md\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("old.txt"));
		}
	}

	[Test]
	public async Task AddAcceptsMultipleAndSemicolonSeparatedGlobs()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("Directory.Build.props", "<Project />");
		directory.WriteFile(".github/workflows/build.yaml", "build");

		var result = await CliInvocation.InvokeAsync(["add", "*.md; *.props", ".github/workflows/*"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<!-- dotnet-solution-items: *.md; *.props; .github/workflows/* -->"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"README.md\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"Directory.Build.props\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\".github/workflows/build.yaml\" />"));
		}
	}

	[Test]
	public async Task AddDoesNotDuplicateExistingGlob()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["add", " * "], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardOutput, Does.Contain("already present"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<!-- dotnet-solution-items: * -->"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("*; *"));
		}
	}

	[Test]
	public async Task RemoveUpdatesGeneratedBlock()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: *; !build.ps1 -->
			  <Folder Name="/Solution Items/">
			    <File Path="README.md" />
			  </Folder>
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("build.ps1", "build");

		var result = await CliInvocation.InvokeAsync(["remove", "!build.ps1"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<!-- dotnet-solution-items: * -->"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"build.ps1\" />"));
		}
	}

	[Test]
	public async Task RemoveAcceptsMultipleAndSemicolonSeparatedGlobs()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: *; *.md; docs/*; !README.md -->
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("build.ps1", "build");
		directory.WriteFile("docs/guide.md", "guide");

		var result = await CliInvocation.InvokeAsync(["remove", "*.md; docs/*", "!README.md"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<!-- dotnet-solution-items: * -->"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"README.md\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"build.ps1\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("docs/guide.md"));
		}
	}

	[Test]
	public async Task RemoveLastGlobRemovesManagedBlock()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			  <Folder Name="/Solution Items/">
			    <File Path="README.md" />
			  </Folder>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");

		var result = await CliInvocation.InvokeAsync(["remove", "*"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("dotnet-solution-items"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("/Solution Items/"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<Project Path=\"src/App/App.csproj\" />"));
		}
	}

	[Test]
	public async Task ListReportsGlobsAndFilesWithoutWritingSolution()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		var originalSolution = directory.ReadFile("repo.slnx");

		var result = await CliInvocation.InvokeAsync(["list"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardOutput, Does.Contain("Globs:"));
			Assert.That(result.StandardOutput, Does.Contain("  *"));
			Assert.That(result.StandardOutput, Does.Contain("  README.md"));
			Assert.That(directory.ReadFile("repo.slnx"), Is.EqualTo(originalSolution));
		}
	}

	[Test]
	public async Task UpdateAddsAndRemovesMatchingFiles()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"README.md\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("old.txt"));
		}
	}

	[Test]
	public async Task UpdateWarnsWhenMarkerCommentIsMissing()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", "<Solution />");
		var originalSolution = directory.ReadFile("repo.slnx");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Does.Contain("Warning"));
			Assert.That(directory.ReadFile("repo.slnx"), Is.EqualTo(originalSolution));
		}
	}

	[Test]
	public async Task IncludeAndExcludeGlobsInteractCorrectly()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: *; !build.sh -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("build.sh", "build");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("README.md"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("<File Path=\"build.sh\" />"));
		}
	}

	[Test]
	public async Task SolutionAndProjectFilesAreExcludedAutomatically()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: **/* -->
			</Solution>
			""");
		directory.WriteFile("src/App/App.csproj", "<Project />");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("README.md"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("repo.slnx"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("App.csproj"));
		}
	}

	[Test]
	public async Task StarMatchesRootFilesButNotRootFolders()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");
		directory.WriteFile("docs/readme.md", "docs");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("README.md"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("/Solution Items/docs/"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("docs/readme.md"));
		}
	}

	[Test]
	public async Task NestedFoldersAreGeneratedOnlyForMatchedFiles()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: .github/workflows/* -->
			</Solution>
			""");
		directory.WriteFile(".github/workflows/build.yaml", "build");
		directory.WriteFile(".github/ISSUE_TEMPLATE/config.yml", "config");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("/Solution Items/.github/"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("/Solution Items/.github/workflows/"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain(".github/workflows/build.yaml"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("ISSUE_TEMPLATE"));
		}
	}

	[Test]
	public async Task EmptyMatchesKeepMarkerCommentButRemoveGeneratedFolders()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: docs/* -->
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			</Solution>
			""");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("dotnet-solution-items: docs/*"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Not.Contain("/Solution Items/"));
		}
	}

	[Test]
	public async Task DefaultLimitFailsBeforeWritingWhenHit()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		for (var index = 0; index < 100; index++)
			directory.WriteFile($"file{index:000}.txt", "content");
		var originalSolution = directory.ReadFile("repo.slnx");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.EqualTo(1));
			Assert.That(result.StandardError, Does.Contain("limit of 100"));
			Assert.That(directory.ReadFile("repo.slnx"), Is.EqualTo(originalSolution));
		}
	}

	[Test]
	public async Task LimitOptionAllowsLargerBlock()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <!-- dotnet-solution-items: * -->
			</Solution>
			""");
		for (var index = 0; index < 100; index++)
			directory.WriteFile($"file{index:000}.txt", "content");

		var result = await CliInvocation.InvokeAsync(["update", "--limit", "102"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("file099.txt"));
		}
	}

	[Test]
	public async Task XmlOutsideOwnedBlockIsPreserved()
	{
		using var directory = TemporarySolutionDirectory.Create();
		directory.WriteFile("repo.slnx", """
			<Solution>
			  <Folder Name="/Other/">
			    <File Path="other.txt" />
			  </Folder>
			  <!-- dotnet-solution-items: * -->
			  <Folder Name="/Solution Items/">
			    <File Path="old.txt" />
			  </Folder>
			  <Project Path="src/App/App.csproj" />
			</Solution>
			""");
		directory.WriteFile("README.md", "readme");

		var result = await CliInvocation.InvokeAsync(["update"], directory.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<Folder Name=\"/Other/\">"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<File Path=\"other.txt\" />"));
			Assert.That(directory.ReadFile("repo.slnx"), Does.Contain("<Project Path=\"src/App/App.csproj\" />"));
		}
	}
}