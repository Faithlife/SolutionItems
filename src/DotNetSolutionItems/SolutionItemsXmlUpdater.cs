using System.Xml.Linq;

namespace DotNetSolutionItems;

internal static class SolutionItemsXmlUpdater
{
	public static SolutionItemsDocument Load(string solutionPath) =>
		new(solutionPath, XDocument.Load(solutionPath, LoadOptions.PreserveWhitespace));

	public static XComment? FindMarkerComment(XDocument document) =>
		document.Root?.Nodes().OfType<XComment>().FirstOrDefault(IsMarkerComment);

	public static bool HasUnmarkedSolutionItemsBlock(XDocument document) =>
		FindMarkerComment(document) is null && FindFirstOwnedFolder(document) is not null;

	public static IReadOnlyList<string> GetGlobs(XComment markerComment) =>
		SolutionItemsConfiguration.ParseGlobs(markerComment.Value);

	public static void Update(SolutionItemsDocument solutionDocument, IReadOnlyList<string> globs, GeneratedSolutionItems generatedItems, bool forceExistingSolutionItemsBlock = false)
	{
		var markerComment = FindMarkerComment(solutionDocument.Document);
		if (markerComment is null)
		{
			markerComment = new XComment(SolutionItemsConfiguration.FormatCommentValue(globs));
			InsertMarkerComment(solutionDocument.Document, markerComment, forceExistingSolutionItemsBlock);
		}
		else
		{
			markerComment.Value = SolutionItemsConfiguration.FormatCommentValue(globs);
		}

		ReplaceOwnedFolders(markerComment, generatedItems);
	}

	public static void RemoveManagedBlock(SolutionItemsDocument solutionDocument)
	{
		var markerComment = FindMarkerComment(solutionDocument.Document);
		if (markerComment is null)
			return;

		RemoveOwnedFolders(markerComment);
		markerComment.Remove();
	}

	private static bool IsMarkerComment(XComment comment) =>
		comment.Value.Trim().StartsWith(SolutionItemsConfiguration.MarkerPrefix, StringComparison.Ordinal);

	private static XElement? FindFirstOwnedFolder(XDocument document) =>
		document.Root?.Elements().FirstOrDefault(IsOwnedFolder);

	private static void InsertMarkerComment(XDocument document, XComment markerComment, bool forceExistingSolutionItemsBlock)
	{
		var root = document.Root ?? throw new InvalidOperationException("The solution XML document has no root element.");
		var firstOwnedFolder = FindFirstOwnedFolder(document);
		if (firstOwnedFolder is not null)
		{
			if (!forceExistingSolutionItemsBlock)
				throw new InvalidOperationException("Warning: an existing Solution Items block was found. Adding globs would replace that block and remove items that do not match the globs. Re-run with --force to continue.");

			firstOwnedFolder.AddBeforeSelf(markerComment);
			return;
		}

		var firstProject = root.Elements("Project").FirstOrDefault();
		if (firstProject is null)
			root.Add(new XText("\n  "), markerComment, new XText("\n"));
		else
			firstProject.AddBeforeSelf(markerComment, new XText("\n  "));
	}

	private static void ReplaceOwnedFolders(XComment markerComment, GeneratedSolutionItems generatedItems)
	{
		RemoveOwnedFolders(markerComment);
		RemoveWhitespaceAfter(markerComment);
		if (generatedItems.Folders.Count == 0)
			return;

		var nextElement = markerComment.NodesAfterSelf().OfType<XElement>().FirstOrDefault();
		var nodes = new List<XNode>();
		foreach (var folder in generatedItems.Folders)
		{
			nodes.Add(new XText("\n  "));
			nodes.Add(CreateFolderElement(folder));
		}

		nodes.Add(new XText(nextElement is null ? "\n" : "\n  "));

		markerComment.AddAfterSelf(nodes);
	}

	private static void RemoveWhitespaceAfter(XNode node)
	{
		foreach (var nextNode in node.NodesAfterSelf().TakeWhile(x => x is XText text && string.IsNullOrWhiteSpace(text.Value)).ToArray())
			nextNode.Remove();
	}

	private static void RemoveOwnedFolders(XComment markerComment)
	{
		while (true)
		{
			var nextElement = markerComment.NodesAfterSelf().OfType<XElement>().FirstOrDefault();
			if (nextElement is null || !IsOwnedFolder(nextElement))
				return;

			RemoveWhitespaceBefore(nextElement, markerComment);
			nextElement.Remove();
		}
	}

	private static void RemoveWhitespaceBefore(XElement element, XComment markerComment)
	{
		foreach (var node in markerComment.NodesAfterSelf().TakeWhile(x => x != element).ToArray())
		{
			if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
				text.Remove();
		}
	}

	private static bool IsOwnedFolder(XElement element) =>
		element.Name.LocalName == "Folder" &&
		((string?) element.Attribute("Name"))?.StartsWith("/Solution Items/", StringComparison.Ordinal) == true;

	private static XElement CreateFolderElement(GeneratedSolutionFolder folder)
	{
		var element = new XElement("Folder", new XAttribute("Name", folder.Name));
		if (folder.Files.Count == 0)
			return element;

		foreach (var file in folder.Files)
			element.Add(new XText("\n    "), new XElement("File", new XAttribute("Path", file)));

		element.Add(new XText("\n  "));
		return element;
	}
}
