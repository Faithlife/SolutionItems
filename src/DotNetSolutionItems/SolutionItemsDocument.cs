using System.Xml.Linq;

namespace DotNetSolutionItems;

internal sealed class SolutionItemsDocument(string path, XDocument document)
{
	public XDocument Document { get; } = document;

	public void SaveIfChanged()
	{
		var newText = Document.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
		var oldText = File.Exists(path) ? File.ReadAllText(path) : "";
		if (oldText != newText)
			File.WriteAllText(path, newText);
	}
}
