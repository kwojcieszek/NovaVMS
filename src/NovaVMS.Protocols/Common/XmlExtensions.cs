using System.Xml.Linq;

namespace NovaVMS.Protocols.Common;

/// <summary>
/// Helpers for reading XML regardless of namespaces.
/// </summary>
internal static class XmlExtensions
{
    public static string ChildValue(this XElement parent, string localName)
    {
        var child = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
        if (child is null)
        {
            return string.Empty;
        }

        return child.Value.Trim();
    }

    public static IEnumerable<XElement> DescendantsByLocalName(this XContainer container, string localName)
    {
        return container.Descendants().Where(element => element.Name.LocalName == localName);
    }
}
