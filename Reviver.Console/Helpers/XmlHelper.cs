using System.Xml;
using System.Xml.Linq;

namespace StoneFlyLabs.Reviver.Helpers;

public static class XmlHelper
{
    public static string TryFormat(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        try
        {
            return XDocument.Parse(input).ToString();
        }
        catch
        {
            return input;
        }
    }

    public static bool IsValid(string input, out string error)
    {
        try
        {
            XDocument.Parse(input);
            error = string.Empty;
            return true;
        }
        catch (XmlException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
