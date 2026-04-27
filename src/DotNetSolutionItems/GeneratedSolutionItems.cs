namespace DotNetSolutionItems;

internal sealed record GeneratedSolutionItems(IReadOnlyList<GeneratedSolutionFolder> Folders, IReadOnlyList<string> Files)
{
	public int ElementCount => Folders.Count + Files.Count;
}
